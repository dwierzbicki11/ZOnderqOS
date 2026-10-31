#!/usr/bin/env python3
"""Opt-in real QMP keyboard + framebuffer probe on a disposable FAT32 guest.

A large framebuffer change is evidence of GUI response, not a semantic proof
that the intended app rendered correctly. Preserve screenshots for inspection.
Never connect this probe to a guest with host disks or real credentials.
"""
import json
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
    if width <= 0 or height <= 0 or len(pixels) != width * height * 3:
        raise ValueError("incomplete framebuffer: " + path)
    return width, height, pixels


def difference(first, second):
    width, height, a = first
    other_width, other_height, b = second
    if (width, height) != (other_width, other_height):
        raise ValueError("framebuffer dimensions changed during probe")
    left, right = width // 6, width * 5 // 6
    top, bottom = height // 7, height * 6 // 7
    changed = 0
    samples = 0
    for y in range(top, bottom, 4):
        for x in range(left, right, 4):
            offset = (y * width + x) * 3
            samples += 1
            if any(abs(a[offset + channel] - b[offset + channel]) > 24 for channel in range(3)):
                changed += 1
    return changed, samples


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
        send_keys(channel, "ret")
        time.sleep(6)
        screenshot(channel, prefix + ".filemanager-after.ppm")

        # A keyboard-only launch cannot prove that the guest mouse works.
        # Capture a real PS/2 relative move before changing the file filter.
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
        frame_width, frame_height, _ = read_ppm(prefix + ".filemanager-after.ppm")
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

        # Verify a real guest window drag, not merely a cursor move. At the
        # QEMU 1024x768 mode File Manager starts at (120, 78) with a 38px
        # titlebar. At narrower modes a window may fill the whole desktop,
        # leaving no legal displacement; those modes need separate tests.
        if frame_width >= 1000 and frame_height >= 700:
            move_mouse_to_from_corner(channel, frame_width // 2, 100,
                                      frame_width, frame_height)
            screenshot(channel, prefix + ".filemanager-drag-before.ppm")
            drag_mouse_relative(channel, 48, 28)
            screenshot(channel, prefix + ".filemanager-drag-moved.ppm")
            drag_mouse_relative(channel, -48, -28)
            screenshot(channel, prefix + ".filemanager-drag-restored.ppm")
        else:
            print("[GUI-QMP] drag-window: SKIP framebuffer too small for a movable File Manager")

        # Alt+F4 must close the active File Manager and reveal the desktop.
        # This also exercises the real guest's global window shortcut path.
        send_keys(channel, "alt", "f4")
        time.sleep(2)
        screenshot(channel, prefix + ".filemanager-closed.ppm")

    before = read_ppm(prefix + ".filemanager-before.ppm")
    palette = read_ppm(prefix + ".filemanager-palette.ppm")
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
        if fraction < 0.12:
            raise RuntimeError(label + ": insufficient framebuffer change; inspect PPMs")
    # The software cursor is rendered into the guest framebuffer. A small,
    # nonzero central-region change is required for real PS/2 pointer motion;
    # it is not evidence of clicking, dragging or touchpad compatibility.
    mouse_changed, mouse_samples = difference(mouse_before, mouse_after)
    print(f"[GUI-QMP] mouse-move: changed {mouse_changed}/{mouse_samples} sampled pixels")
    if mouse_changed < 8:
        raise RuntimeError("PS/2 pointer move not visible in guest framebuffer")

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

    if frame_width >= 1000 and frame_height >= 700:
        drag_before = read_ppm(prefix + ".filemanager-drag-before.ppm")
        drag_moved = read_ppm(prefix + ".filemanager-drag-moved.ppm")
        drag_restored = read_ppm(prefix + ".filemanager-drag-restored.ppm")
        changed, samples = difference(drag_before, drag_moved)
        fraction = changed / max(1, samples)
        print(f"[GUI-QMP] drag-window-moved: changed {changed}/{samples} sampled pixels ({fraction:.1%})")
        if fraction < 0.03:
            raise RuntimeError("PS/2 drag did not visibly move File Manager; inspect PPMs")
        changed, samples = difference(drag_before, drag_restored)
        fraction = changed / max(1, samples)
        print(f"[GUI-QMP] drag-window-restored: changed {changed}/{samples} sampled pixels ({fraction:.1%})")
        if fraction > 0.10:
            raise RuntimeError("reverse PS/2 drag did not restore File Manager; inspect PPMs")

    # Closing a window should return to the same desktop, not another large
    # overlay. Small differences from the clock/toasts are tolerated.
    changed, samples = difference(before, closed)
    fraction = changed / max(1, samples)
    print(f"[GUI-QMP] desktop-restored: changed {changed}/{samples} sampled pixels ({fraction:.1%})")
    if fraction > 0.10:
        raise RuntimeError("Alt+F4 did not restore the desktop; inspect PPMs")
    print("[GUI-QMP] PASS real palette launch, filter/Escape, PS/2 notification clicks, window drag and Alt+F4 close changed the guest framebuffer; visual app identity requires inspection.")


if __name__ == "__main__":
    main()
