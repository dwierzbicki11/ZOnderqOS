#!/usr/bin/env python3
"""Opt-in real QMP keyboard + framebuffer probe on a disposable FAT32 guest.

A large framebuffer change is evidence of GUI response, not a semantic proof
that the intended app rendered correctly. Preserve screenshots for inspection.
Never connect this probe to a guest with host disks or real credentials.
"""
import json
import os
import socket
import sys
import time


def command(channel, payload):
    channel.write((json.dumps(payload) + "\r\n").encode("ascii"))
    while True:
        reply = json.loads(channel.readline())
        if "error" in reply:
            raise RuntimeError("QMP rejected command: " + str(reply["error"]))
        if "return" in reply:
            return


def send_keys(channel, *codes):
    command(channel, {
        "execute": "send-key",
        "arguments": {
            "keys": [{"type": "qcode", "data": code} for code in codes],
            "hold-time": 90,
        },
    })
    time.sleep(0.2)



def move_mouse_relative(channel, dx, dy, delay=0.8):
    # QEMU's emulated PS/2 mouse receives real relative pointer events.
    # No host input devices or host disk partitions are exposed to the guest.
    events = []
    if dx:
        events.append({"type": "rel", "data": {"axis": "x", "value": dx}})
    if dy:
        events.append({"type": "rel", "data": {"axis": "y", "value": dy}})
    if events:
        command(channel, {"execute": "input-send-event", "arguments": {"events": events}})
        time.sleep(delay)


def move_mouse_to_from_corner(channel, target_x, target_y, width, height):
    """Anchor a PS/2 relative pointer at (0, 0), then reach a screen point.

    The QMP relative mouse has no position query. Small deltas avoid PS/2
    packet saturation; first move beyond the screen bounds so the result is
    independent of the cursor position left by the login or keyboard probe.
    """
    if width <= 0 or height <= 0 or not (0 <= target_x < width and 0 <= target_y < height):
        raise ValueError("QMP mouse target is outside the framebuffer")
    dx, dy = width + 64, height + 64
    while dx or dy:
        step_x, step_y = min(64, dx), min(64, dy)
        move_mouse_relative(channel, -step_x, -step_y, delay=0.06)
        dx -= step_x
        dy -= step_y
    dx, dy = target_x, target_y
    while dx or dy:
        step_x, step_y = min(64, dx), min(64, dy)
        move_mouse_relative(channel, step_x, step_y, delay=0.06)
        dx -= step_x
        dy -= step_y
    time.sleep(0.8)



def mouse_button(channel, pressed, button="left"):
    """Send an actual emulated mouse button transition, not a keyboard key.

    This primitive is deliberately separate from the default screenshot gate:
    a QMP acknowledgement does not prove the guest handled a click.
    """
    if button not in ("left", "right", "middle"):
        raise ValueError("unsupported QMP mouse button: " + str(button))
    if not isinstance(pressed, bool):
        raise TypeError("pressed must be a bool")
    command(channel, {
        "execute": "input-send-event",
        "arguments": {"events": [{
            "type": "btn",
            "data": {"button": button, "down": pressed},
        }]},
    })
    time.sleep(0.15)


def click_mouse(channel, button="left"):
    """Always release the button, even if the guest rejects the press."""
    try:
        mouse_button(channel, True, button)
    finally:
        mouse_button(channel, False, button)


def drag_mouse_relative(channel, dx, dy):
    """Hold the guest left button while moving, releasing even on QMP error."""
    try:
        mouse_button(channel, True)
        move_mouse_relative(channel, dx, dy, delay=0.3)
    finally:
        mouse_button(channel, False)
    time.sleep(0.6)


def screenshot(channel, path):
    command(channel, {"execute": "screendump", "arguments": {"filename": path}})


def read_ppm(path):
    with open(path, "rb") as image:
        if image.readline().strip() != b"P6":
            raise ValueError("not a P6 QMP framebuffer: " + path)
        dimensions = image.readline()
        while dimensions.startswith(b"#"):
            dimensions = image.readline()
        width, height = map(int, dimensions.split())
        if image.readline().strip() != b"255":
            raise ValueError("unexpected PPM depth")
        pixels = image.read()
    if width <= 0 or height <= 0:
        raise ValueError("invalid framebuffer dimensions: " + path)

    packed_row = width * 3
    packed_size = packed_row * height
    if len(pixels) == packed_size:
        return width, height, pixels

    # QEMU's virtio-vga screendump can emit 4-byte-aligned P6 scanlines.
    # Real 1366x768 captures have 4098 RGB bytes + two ZERO padding bytes
    # per row: the header is 1366x768, but the payload is 3,148,800 bytes.
    # Accept only that exact stride, remove padding before pixel comparisons,
    # and continue rejecting truncated or arbitrary trailing image data.
    stride = (packed_row + 3) & ~3
    if stride == packed_row or len(pixels) != stride * height:
        raise ValueError("incomplete framebuffer: " + path)
    unpacked = bytearray(packed_size)
    for row in range(height):
        source = row * stride
        target = row * packed_row
        if any(pixels[source + packed_row:source + stride]):
            raise ValueError("nonzero QMP framebuffer row padding: " + path)
        unpacked[target:target + packed_row] = pixels[source:source + packed_row]
    return width, height, bytes(unpacked)


def minimum_palette_changed_samples(sample_count):
    """Fixed-size launcher must remain detectable on Full HD and higher.

    Pure percentages unfairly demand that the same fixed-width palette grow
    with the entire framebuffer. Keep the 12% gate on compact modes, capped
    at 5000 changed sampled pixels for large screens. An unchanged image or
    small cursor/toast still fails by a wide margin.
    """
    if sample_count < 1:
        raise ValueError("empty framebuffer sample")
    return max(1, min(int(sample_count * 0.12), 5000))


def difference(first, second, upper_only=False):
    """Sample GUI changes; optionally ignore transient lower-screen toasts.

    The welcome notification can expire between the desktop baseline and
    Alt+F4. A full-screen desktop-restore comparison would mistake that
    unrelated toast expiry for a File Manager window left open.
    """
    width, height, a = first
    other_width, other_height, b = second
    if (width, height) != (other_width, other_height):
        raise ValueError("framebuffer dimensions changed during probe")
    left, right = width // 6, width * 5 // 6
    top = height // 7
    bottom = height * 3 // 5 if upper_only else height * 6 // 7
    changed = 0
    samples = 0
    for y in range(top, bottom, 4):
        for x in range(left, right, 4):
            offset = (y * width + x) * 3
            samples += 1
            if any(abs(a[offset + channel] - b[offset + channel]) > 24 for channel in range(3)):
                changed += 1
    return changed, samples


def changed_pixels_in_region(first, second, left, top, right, bottom, threshold=24):
    """Count real framebuffer pixel changes in a clipped cursor-sized region.

    Unlike the coarse 4px desktop sampling grid, this cannot miss a narrow
    software cursor because of sample-grid alignment. A clock/toast outside
    both cursor rectangles cannot satisfy either independent movement gate.
    """
    width, height, before = first
    other_width, other_height, after = second
    if (width, height) != (other_width, other_height):
        raise ValueError("framebuffer dimensions changed during cursor probe")
    if len(before) != width * height * 3 or len(after) != width * height * 3:
        raise ValueError("incomplete framebuffer during cursor probe")
    changed = 0
    for y in range(max(0, top), min(height, bottom)):
        for x in range(max(0, left), min(width, right)):
            offset = (y * width + x) * 3
            if any(abs(before[offset + channel] - after[offset + channel]) > threshold
                   for channel in range(3)):
                changed += 1
    return changed


def expected_filemanager_bounds(frame_width, frame_height):
    """Source-derived initial Window bounds, not proof of rendered geometry.

    FileManager starts at (120,78) with size (820,520); Window constrains
    this to the actual desktop (screen height minus the 44px taskbar).
    """
    if frame_width <= 0 or frame_height <= 44:
        raise ValueError("invalid GUI framebuffer dimensions")
    desktop_height = frame_height - 44
    margin = 12 if frame_width >= 1000 and desktop_height >= 700 else 0
    available_width = frame_width - 2 * margin
    available_height = desktop_height - 2 * margin
    width = max(min(320, available_width), min(820, available_width))
    height = max(min(220, available_height), min(520, available_height))
    x = max(margin, min(120, frame_width - width - margin))
    y = max(margin, min(78, desktop_height - height - margin))
    return x, y, width, height


def verify_expected_framebuffer(width, height):
    """Fail closed when a requested 800x600/1366x768 mode was not obtained."""
    expected = os.environ.get("SERVICE_QEMU_EXPECT_FRAMEBUFFER", "")
    if not expected:
        return
    try:
        wanted_width, wanted_height = map(int, expected.split("x"))
    except (ValueError, TypeError):
        raise ValueError("invalid SERVICE_QEMU_EXPECT_FRAMEBUFFER; use WIDTHxHEIGHT")
    if wanted_width <= 0 or wanted_height <= 0:
        raise ValueError("expected framebuffer dimensions must be positive")
    if (width, height) != (wanted_width, wanted_height):
        raise RuntimeError(
            f"requested framebuffer {expected}, actual {width}x{height}; "
            "do not count this boot as a resolution test")


def main():
    if len(sys.argv) != 3:
        raise SystemExit("usage: qmp-gui-filemanager.py <qmp-socket> <output-prefix>")

    prefix = sys.argv[2]
    with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as connection:
        connection.settimeout(20)
        connection.connect(sys.argv[1])
        channel = connection.makefile("rwb", buffering=0)
        if "QMP" not in json.loads(channel.readline()):
            raise RuntimeError("QMP greeting missing")
        command(channel, {"execute": "qmp_capabilities"})
        screenshot(channel, prefix + ".filemanager-before.ppm")

        # Open global command palette (Alt+Space), then select File Manager.
        send_keys(channel, "alt", "spc")
        time.sleep(1.5)
        for key in "file":
            send_keys(channel, key)
        time.sleep(1.5)
        screenshot(channel, prefix + ".filemanager-palette.ppm")
        # Shift+Enter must leave the launcher open. Only plain Enter may
        # start an application; verify the guest framebuffer, not QMP ACK.
        send_keys(channel, "shift", "ret")
        time.sleep(1.0)
        screenshot(channel, prefix + ".filemanager-palette-shift-enter.ppm")
        send_keys(channel, "ret")
        time.sleep(6)
        screenshot(channel, prefix + ".filemanager-after.ppm")

        # A keyboard-only launch cannot prove that the guest mouse works.
        # The cursor left by login may be outside the central diff crop,
        # especially at 640x480. Home the real PS/2 cursor to a known,
        # sampled interior position BEFORE capturing the baseline. A QMP
        # acknowledgement alone still cannot satisfy the pixel-change gate.
        frame_width, frame_height, _ = read_ppm(prefix + ".filemanager-after.ppm")
        verify_expected_framebuffer(frame_width, frame_height)
        mouse_start_x = frame_width // 2 - 30
        mouse_start_y = frame_height // 2
        move_mouse_to_from_corner(channel, mouse_start_x, mouse_start_y,
                                  frame_width, frame_height)
        screenshot(channel, prefix + ".filemanager-mouse-before.ppm")
        move_mouse_relative(channel, 60, 0)
        screenshot(channel, prefix + ".filemanager-mouse-after.ppm")

        # Exercise the real File Manager filter and Escape clear path using
        # only guest keyboard events. The deliberately improbable query
        # avoids opening or modifying any FAT32 files.
        for key in "zzzzzzzz":
            send_keys(channel, key)
        time.sleep(0.8)
        screenshot(channel, prefix + ".filemanager-filtered.ppm")
        send_keys(channel, "esc")
        time.sleep(1.0)
        screenshot(channel, prefix + ".filemanager-filter-cleared.ppm")

        # Real PS/2 button integration: click the taskbar notification tile,
        # require a large central-framebuffer change, then click again to
        # dismiss. This never touches a disk action or file-list item.
        # The pointer is anchored to the screen corner first: QMP's PS/2
        # device has relative coordinates only, and earlier cursor positions
        # are not a trustworthy basis for a click target.
        window_x, window_y, window_width, window_height = expected_filemanager_bounds(
            frame_width, frame_height)
        tray_width = 278 if frame_width >= 560 else 132
        notification_x = frame_width - tray_width + 26
        notification_y = frame_height - 22
        move_mouse_to_from_corner(channel, notification_x, notification_y,
                                  frame_width, frame_height)
        screenshot(channel, prefix + ".notification-click-before.ppm")
        click_mouse(channel)
        time.sleep(1.2)
        screenshot(channel, prefix + ".notification-click-open.ppm")
        click_mouse(channel)
        time.sleep(1.2)
        screenshot(channel, prefix + ".notification-click-closed.ppm")

        # Opt-in real PS/2 Start/taskbar regression. The Start button must
        # toggle the menu; clicking the notification tile must dismiss Start
        # and open notifications on the same press. The second click closes
        # notifications, leaving the original File Manager unobscured.
        probe_start_taskbar = os.environ.get("SERVICE_QEMU_PROBE_START_TASKBAR") == "1"
        if probe_start_taskbar:
            screenshot(channel, prefix + ".start-taskbar-before.ppm")
            move_mouse_to_from_corner(channel, 22, frame_height - 22,
                                      frame_width, frame_height)
            click_mouse(channel)
            time.sleep(1.0)
            move_mouse_to_from_corner(channel, notification_x, notification_y,
                                      frame_width, frame_height)
            screenshot(channel, prefix + ".start-taskbar-open.ppm")
            click_mouse(channel)
            time.sleep(1.0)
            screenshot(channel, prefix + ".start-taskbar-notifications.ppm")
            click_mouse(channel)
            time.sleep(1.0)
            screenshot(channel, prefix + ".start-taskbar-dismissed.ppm")

        # A full-width File Manager cannot be dragged horizontally at
        # 800x600; still exercise a real bottom-edge resize at that mode.
        # Coordinates are derived from Window.ConstrainToDesktop, never
        # hard-coded for QEMU's previous 1024x768 framebuffer.
        can_drag = (frame_width - window_width >= 80 and
                    frame_height - 44 - window_height >= 40)
        can_resize = window_height >= 300 and window_width >= 320
        if can_drag:
            title_x = window_x + window_width // 2
            title_y = window_y + 22
            move_mouse_to_from_corner(channel, title_x, title_y,
                                      frame_width, frame_height)
            screenshot(channel, prefix + ".filemanager-drag-before.ppm")
            drag_mouse_relative(channel, 48, 28)
            screenshot(channel, prefix + ".filemanager-drag-moved.ppm")
            drag_mouse_relative(channel, -48, -28)
            screenshot(channel, prefix + ".filemanager-drag-restored.ppm")
        else:
            print("[GUI-QMP] drag-window: SKIP no legal 48x28 window displacement")

        if can_resize:
            resize_x = window_x + window_width // 2
            resize_y = window_y + window_height - 3
            move_mouse_to_from_corner(channel, resize_x, resize_y,
                                      frame_width, frame_height)
            screenshot(channel, prefix + ".filemanager-resize-before.ppm")
            drag_mouse_relative(channel, 0, -64)
            screenshot(channel, prefix + ".filemanager-resize-smaller.ppm")
            drag_mouse_relative(channel, 0, 64)
            screenshot(channel, prefix + ".filemanager-resize-restored.ppm")
        else:
            print("[GUI-QMP] resize-window: SKIP window too small for safe resize")

        # Optional regression: a File Manager context menu is modal for
        # Alt+F4. The first shortcut must dismiss the menu, not close the
        # window; the final Alt+F4 below must still close it normally.
        # No file operation is selected or performed by this probe.
        probe_modal_alt_f4 = os.environ.get("SERVICE_QEMU_PROBE_MODAL_ALT_F4") == "1"
        if probe_modal_alt_f4:
            # Hit the six-pixel gap between grid tiles: this opens the
            # menu without selecting/highlighting a file, so its dismissal
            # can be compared against an unchanged client-area baseline.
            modal_x = window_x + 10 + 176 + 8 + 112 + 2
            modal_y = window_y + 40 + 50 + 20
            move_mouse_to_from_corner(channel, modal_x, modal_y,
                                      frame_width, frame_height)
            screenshot(channel, prefix + ".filemanager-modal-before.ppm")
            click_mouse(channel, "right")
            time.sleep(0.9)
            screenshot(channel, prefix + ".filemanager-modal-open.ppm")
            send_keys(channel, "alt", "f4")
            time.sleep(0.9)
            screenshot(channel, prefix + ".filemanager-modal-dismissed.ppm")

        # Optional real guest regression for maximized caption dragging.
        # Run after ordinary drag/resize so this probe cannot invalidate
        # their source-derived coordinates. It never touches disk content.
        probe_max_drag = os.environ.get("SERVICE_QEMU_PROBE_MAX_DRAG") == "1" and can_drag
        if probe_max_drag:
            maximize_x = window_x + window_width - 49
            maximize_y = window_y + 19
            move_mouse_to_from_corner(channel, maximize_x, maximize_y,
                                      frame_width, frame_height)
            screenshot(channel, prefix + ".filemanager-maxdrag-before.ppm")
            click_mouse(channel)
            time.sleep(1.0)
            screenshot(channel, prefix + ".filemanager-maxdrag-maximized.ppm")
            # Grab a safe central caption point, far from caption controls.
            move_mouse_to_from_corner(channel, frame_width // 2, 20,
                                      frame_width, frame_height)
            drag_mouse_relative(channel, 50, 45)
            screenshot(channel, prefix + ".filemanager-maxdrag-restored.ppm")

        # Alt+F4 must close the active File Manager and reveal the desktop.
        # This also exercises the real guest's global window shortcut path.
        send_keys(channel, "alt", "f4")
        time.sleep(2)
        screenshot(channel, prefix + ".filemanager-closed.ppm")

    before = read_ppm(prefix + ".filemanager-before.ppm")
    palette = read_ppm(prefix + ".filemanager-palette.ppm")
    shift_enter = read_ppm(prefix + ".filemanager-palette-shift-enter.ppm")
    changed, samples = difference(palette, shift_enter, upper_only=True)
    fraction = changed / max(1, samples)
    print(f"[GUI-QMP] palette-shift-enter: changed {changed}/{samples} sampled pixels ({fraction:.1%})")
    if fraction > 0.06:
        raise RuntimeError("Shift+Enter unexpectedly launched an app or changed the palette")
    after = read_ppm(prefix + ".filemanager-after.ppm")
    mouse_before = read_ppm(prefix + ".filemanager-mouse-before.ppm")
    mouse_after = read_ppm(prefix + ".filemanager-mouse-after.ppm")
    filtered = read_ppm(prefix + ".filemanager-filtered.ppm")
    filter_cleared = read_ppm(prefix + ".filemanager-filter-cleared.ppm")
    closed = read_ppm(prefix + ".filemanager-closed.ppm")
    for label, first, second in (
        ("palette-open", before, palette),
        ("app-vs-desktop", before, after),
        ("app-vs-palette", palette, after),
        ("app-closed", after, closed),
    ):
        changed, samples = difference(first, second)
        fraction = changed / max(1, samples)
        print(f"[GUI-QMP] {label}: changed {changed}/{samples} sampled pixels ({fraction:.1%})")
        required = minimum_palette_changed_samples(samples) if label == "palette-open" \
            else max(1, int(samples * 0.12))
        if changed < required:
            raise RuntimeError(label + ": insufficient framebuffer change; inspect PPMs " +
                               f"({changed} changed, {required} required)")
    # Require separate changes at the cursor's old and new positions.
    # The previous 4px sampling grid saw only 6 pixels at 800x600 even
    # though real QEMU frames changed 113 pixels in EACH cursor rectangle.
    if probe_modal_alt_f4:
        modal_before = read_ppm(prefix + ".filemanager-modal-before.ppm")
        modal_open = read_ppm(prefix + ".filemanager-modal-open.ppm")
        modal_dismissed = read_ppm(prefix + ".filemanager-modal-dismissed.ppm")
        # Compare a region within the menu, away from the stationary cursor
        # and taskbar. This catches accidental window closure as well as a
        # menu that failed to open/dismiss; a QMP ACK cannot satisfy it.
        region = (modal_x + 25, modal_y + 25, modal_x + 145, modal_y + 105)
        opened_pixels = changed_pixels_in_region(modal_before, modal_open, *region, threshold=8)
        dismissed_pixels = changed_pixels_in_region(modal_before, modal_dismissed, *region, threshold=8)
        print(f"[GUI-QMP] modal-alt-f4: opened={opened_pixels}, "
              f"remaining={dismissed_pixels} changed pixels in 120x80 region")
        if opened_pixels < 500 or dismissed_pixels * 3 >= opened_pixels:
            raise RuntimeError("File Manager modal Alt+F4 did not dismiss "
                               "the context menu while preserving the window")

    # Keep this a real framebuffer gate; never accept QMP ACK alone.
    old_cursor_pixels = changed_pixels_in_region(
        mouse_before, mouse_after,
        mouse_start_x - 4, mouse_start_y - 4,
        mouse_start_x + 24, mouse_start_y + 28)
    new_cursor_pixels = changed_pixels_in_region(
        mouse_before, mouse_after,
        mouse_start_x + 60 - 4, mouse_start_y - 4,
        mouse_start_x + 60 + 24, mouse_start_y + 28)
    print(f"[GUI-QMP] mouse-move: old={old_cursor_pixels} new={new_cursor_pixels} "
          "changed full-resolution pixels")
    if old_cursor_pixels < 32 or new_cursor_pixels < 32:
        raise RuntimeError("PS/2 pointer move not visible at both guest cursor positions")

    # Escape must restore the same File Manager viewport after clearing
    # the search query. This is a real QEMU keyboard regression, not merely
    # evidence that QMP accepted the keystroke.
    # Compare against the post-mouse baseline so pointer movement cannot
    # masquerade as a successful search update.
    changed, samples = difference(mouse_after, filtered)
    fraction = changed / max(1, samples)
    print(f"[GUI-QMP] search-filter: changed {changed}/{samples} sampled pixels ({fraction:.1%}); visual inspection still required")
    # A successful QMP send-key is not proof that the File Manager accepted
    # the query. Require a measurable change in the central file-list area.
    # Both real 1024x768 boot probes previously measured 1.1%; allow margin
    # for other layouts while rejecting unchanged/stale framebuffers.
    if fraction < 0.003:
        raise RuntimeError("File Manager search did not visibly update; inspect PPMs")
    changed, samples = difference(mouse_after, filter_cleared)
    fraction = changed / max(1, samples)
    print(f"[GUI-QMP] filter-cleared: changed {changed}/{samples} sampled pixels ({fraction:.1%})")
    if fraction > 0.10:
        raise RuntimeError("Escape did not restore the File Manager viewport; inspect PPMs")

    # Compare at a stationary cursor location so moving the pointer alone
    # cannot produce a false positive for a successful mouse click.
    notification_before = read_ppm(prefix + ".notification-click-before.ppm")
    notification_open = read_ppm(prefix + ".notification-click-open.ppm")
    notification_closed = read_ppm(prefix + ".notification-click-closed.ppm")
    changed, samples = difference(notification_before, notification_open)
    fraction = changed / max(1, samples)
    print(f"[GUI-QMP] notification-click-open: changed {changed}/{samples} sampled pixels ({fraction:.1%})")
    if fraction < 0.05:
        raise RuntimeError("PS/2 click did not visibly open notification center; inspect PPMs")
    changed, samples = difference(notification_before, notification_closed)
    fraction = changed / max(1, samples)
    print(f"[GUI-QMP] notification-click-closed: changed {changed}/{samples} sampled pixels ({fraction:.1%})")
    if fraction > 0.10:
        raise RuntimeError("second PS/2 click did not restore File Manager viewport; inspect PPMs")

    if probe_start_taskbar:
        start_before = read_ppm(prefix + ".start-taskbar-before.ppm")
        start_open = read_ppm(prefix + ".start-taskbar-open.ppm")
        start_dismissed = read_ppm(prefix + ".start-taskbar-dismissed.ppm")
        # Compare inside the painted Start menu, away from the tray, clock,
        # and stationary PS/2 cursor. A QMP send-event ACK is not sufficient.
        start_region = (80, 80, min(420, frame_width - 20), min(400, frame_height - 70))
        start_pixels = changed_pixels_in_region(start_before, start_open, *start_region, threshold=8)
        remaining_pixels = changed_pixels_in_region(start_before, start_dismissed,
                                                    *start_region, threshold=8)
        print(f"[GUI-QMP] start-taskbar: opened={start_pixels}, remaining={remaining_pixels} "
              "changed pixels after notification toggle")
        if start_pixels < 1500 or remaining_pixels * 3 >= start_pixels:
            raise RuntimeError("Start menu did not open and dismiss through the real taskbar click")

    if can_drag:
        drag_before = read_ppm(prefix + ".filemanager-drag-before.ppm")
        drag_moved = read_ppm(prefix + ".filemanager-drag-moved.ppm")
        drag_restored = read_ppm(prefix + ".filemanager-drag-restored.ppm")
        changed, samples = difference(drag_before, drag_moved)
        fraction = changed / max(1, samples)
        print(f"[GUI-QMP] drag-window-moved: changed {changed}/{samples} sampled pixels ({fraction:.1%}); 2% min allows Full HD crop")
        if fraction < 0.02:
            raise RuntimeError("PS/2 drag did not visibly move File Manager; inspect PPMs")
        changed, samples = difference(drag_before, drag_restored)
        fraction = changed / max(1, samples)
        print(f"[GUI-QMP] drag-window-restored: changed {changed}/{samples} sampled pixels ({fraction:.1%})")
        if fraction > 0.10:
            raise RuntimeError("reverse PS/2 drag did not restore File Manager; inspect PPMs")

    if can_resize:
        resize_before = read_ppm(prefix + ".filemanager-resize-before.ppm")
        resize_smaller = read_ppm(prefix + ".filemanager-resize-smaller.ppm")
        resize_restored = read_ppm(prefix + ".filemanager-resize-restored.ppm")
        changed, samples = difference(resize_before, resize_smaller)
        fraction = changed / max(1, samples)
        print(f"[GUI-QMP] resize-window-smaller: changed {changed}/{samples} sampled pixels ({fraction:.1%})")
        if fraction < 0.015:
            raise RuntimeError("PS/2 bottom-edge resize did not visibly shrink File Manager; inspect PPMs")
        changed, samples = difference(resize_before, resize_restored)
        fraction = changed / max(1, samples)
        print(f"[GUI-QMP] resize-window-restored: changed {changed}/{samples} sampled pixels ({fraction:.1%})")
        if fraction > 0.10:
            raise RuntimeError("reverse PS/2 resize did not restore File Manager; inspect PPMs")

    if probe_max_drag:
        max_before = read_ppm(prefix + ".filemanager-maxdrag-before.ppm")
        maximized = read_ppm(prefix + ".filemanager-maxdrag-maximized.ppm")
        restored = read_ppm(prefix + ".filemanager-maxdrag-restored.ppm")
        changed, samples = difference(max_before, maximized)
        fraction = changed / max(1, samples)
        print(f"[GUI-QMP] maxdrag-maximize: changed {changed}/{samples} sampled pixels ({fraction:.1%})")
        if fraction < 0.03:
            raise RuntimeError("maximize button did not visibly maximize File Manager")
        changed, samples = difference(maximized, restored)
        fraction = changed / max(1, samples)
        print(f"[GUI-QMP] maxdrag-restore: changed {changed}/{samples} sampled pixels ({fraction:.1%})")
        if fraction < 0.03:
            raise RuntimeError("maximized caption drag did not visibly restore File Manager")

    # Closing a window should return to the same desktop, not another large
    # overlay. Small differences from the clock/toasts are tolerated.
    # Use the stable upper desktop: at 640x480 a welcome toast in the
    # lower-right corner can expire while the File Manager is open. The
    # File Manager occupies the upper region too, so a window left open
    # still fails this strict 10% restoration gate.
    changed, samples = difference(before, closed, upper_only=True)
    fraction = changed / max(1, samples)
    print(f"[GUI-QMP] desktop-restored-upper: changed {changed}/{samples} "
          f"sampled pixels ({fraction:.1%})")
    if fraction > 0.10:
        raise RuntimeError("Alt+F4 did not restore the desktop; inspect PPMs")
    print("[GUI-QMP] PASS real palette launch, filter/Escape, PS/2 notification clicks, window drag/resize and Alt+F4 close changed the guest framebuffer; visual app identity requires inspection.")


if __name__ == "__main__":
    main()
