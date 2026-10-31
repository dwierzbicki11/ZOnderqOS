#!/usr/bin/env python3
"""Real, opt-in QMP Task Manager interaction probe on a disposable FAT32 guest.

This script sends PS/2 input to the running kernel and measures real QMP
framebuffers. The guest must have *only* a synthetic test disk attached.
A QMP acknowledgement alone is never counted as a successful interaction.
"""
import importlib.util
from pathlib import Path
import socket
import sys
import time

_spec = importlib.util.spec_from_file_location(
    "qmp_gui_filemanager", Path(__file__).with_name("qmp-gui-filemanager.py"))
qmp = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(qmp)


def expected_taskmanager_bounds(frame_width, frame_height):
    """Model the Window constructor's constrained 1040x690 at (165,110)."""
    if frame_width <= 0 or frame_height <= 44:
        raise ValueError("invalid framebuffer")
    desktop_height = frame_height - 44
    margin = 12 if frame_width >= 1000 and desktop_height >= 700 else 0
    width = min(1040, frame_width - 2 * margin)
    height = min(690, desktop_height - 2 * margin)
    x = max(margin, min(165, frame_width - width - margin))
    y = max(margin, min(110, desktop_height - height - margin))
    return x, y, width, height


def nav_target(bounds, page):
    """Match TaskManagerModernView.NavAt/DrawNav, including short windows."""
    x, y, width, height = bounds
    view_x, view_y = x + 10, y + 40
    view_height = max(1, height - 50)
    if page not in (0, 1):
        raise ValueError("only safe read-only pages are probed")
    if view_height < 300:
        return view_x + 6 + page * 80 + 38, view_y + 60 + 16
    return view_x + 52, view_y + 60 + page * 42 + 20


def measure(label, before, after, minimum=None, maximum=None):
    changed, samples = qmp.difference(before, after)
    fraction = changed / max(1, samples)
    print(f"[TASK-QMP] {label}: {changed}/{samples} pixels ({fraction:.1%})", flush=True)
    if minimum is not None and fraction < minimum:
        raise RuntimeError(f"{label}: insufficient real framebuffer change")
    if maximum is not None and fraction > maximum:
        raise RuntimeError(f"{label}: framebuffer did not return to expected view")
    return fraction



def resize_steps(delta):
    """Bound each PS/2 relative movement; a single large QMP delta can saturate."""
    if isinstance(delta, bool) or not isinstance(delta, int):
        raise TypeError("resize delta must be an integer")
    while delta:
        step = max(-48, min(48, delta))
        yield step
        delta -= step


def resize_bottom_edge(channel, delta):
    """Resize a guest window with a real held PS/2 button and guaranteed release."""
    try:
        qmp.mouse_button(channel, True)
        for step in resize_steps(delta):
            qmp.move_mouse_relative(channel, 0, step, delay=0.15)
    finally:
        qmp.mouse_button(channel, False)
    time.sleep(0.6)


def main():
    if len(sys.argv) != 3:
        raise SystemExit("usage: qmp-gui-taskmanager.py <qmp-socket> <output-prefix>")
    prefix = sys.argv[2]
    with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as connection:
        connection.settimeout(20)
        connection.connect(sys.argv[1])
        channel = connection.makefile("rwb", buffering=0)
        if "QMP" not in qmp.json.loads(channel.readline()):
            raise RuntimeError("QMP greeting missing")
        qmp.command(channel, {"execute": "qmp_capabilities"})
        qmp.screenshot(channel, prefix + ".taskmanager-desktop.ppm")

        # The registered ID is task-manager; searching "task" avoids any
        # dependency on the Polish display label.
        qmp.send_keys(channel, "alt", "spc")
        time.sleep(1)
        for key in "task":
            qmp.send_keys(channel, key)
        time.sleep(1)
        qmp.screenshot(channel, prefix + ".taskmanager-palette.ppm")
        qmp.send_keys(channel, "ret")
        time.sleep(4)
        qmp.screenshot(channel, prefix + ".taskmanager-processes.ppm")

        frame_width, frame_height, _ = qmp.read_ppm(prefix + ".taskmanager-processes.ppm")
        qmp.verify_expected_framebuffer(frame_width, frame_height)
        bounds = expected_taskmanager_bounds(frame_width, frame_height)
        # Use the actual framebuffer and source-derived window geometry.
        # Never click END TASK or any disk/file operation.
        x, y = nav_target(bounds, 1)
        qmp.move_mouse_to_from_corner(channel, x, y, frame_width, frame_height)
        qmp.click_mouse(channel)
        time.sleep(1)
        qmp.screenshot(channel, prefix + ".taskmanager-performance.ppm")

        x, y = nav_target(bounds, 0)
        qmp.move_mouse_to_from_corner(channel, x, y, frame_width, frame_height)
        qmp.click_mouse(channel)
        time.sleep(1)
        qmp.screenshot(channel, prefix + ".taskmanager-processes-restored.ppm")

        # Prove that the *guest* responds to plain keyboard page navigation,
        # not just a successful QMP send-key acknowledgement. Both transitions
        # are read-only and checked against captured real framebuffer pixels.
        qmp.send_keys(channel, "right")
        time.sleep(1)
        qmp.screenshot(channel, prefix + ".taskmanager-performance-keyboard.ppm")
        qmp.send_keys(channel, "left")
        time.sleep(1)
        qmp.screenshot(channel, prefix + ".taskmanager-processes-keyboard.ppm")

        # Exercise the minimum-height, wide Task Manager layout using a real
        # bottom-edge drag. No END TASK, disk, or file operations are touched.
        # Keep screenshots as evidence: a pixel difference alone does not
        # prove every compact row or footer label is correctly drawn.
        resize_x = bounds[0] + bounds[2] // 2
        resize_y = bounds[1] + bounds[3] - 3
        shrink = min(470, max(0, bounds[3] - 220))
        if shrink:
            qmp.move_mouse_to_from_corner(channel, resize_x, resize_y,
                                          frame_width, frame_height)
            qmp.screenshot(channel, prefix + ".taskmanager-resize-before.ppm")
            resize_bottom_edge(channel, -shrink)
            qmp.screenshot(channel, prefix + ".taskmanager-short-wide.ppm")
            resize_bottom_edge(channel, shrink)
            qmp.screenshot(channel, prefix + ".taskmanager-resize-restored.ppm")
        else:
            print("[TASK-QMP] short-window-resize: SKIP no legal height reduction")

        # Escape is a non-destructive close path in TaskManagerModernApp.
        qmp.send_keys(channel, "esc")
        time.sleep(2)
        qmp.screenshot(channel, prefix + ".taskmanager-closed.ppm")

    desktop = qmp.read_ppm(prefix + ".taskmanager-desktop.ppm")
    palette = qmp.read_ppm(prefix + ".taskmanager-palette.ppm")
    processes = qmp.read_ppm(prefix + ".taskmanager-processes.ppm")
    performance = qmp.read_ppm(prefix + ".taskmanager-performance.ppm")
    restored = qmp.read_ppm(prefix + ".taskmanager-processes-restored.ppm")
    keyboard_performance = qmp.read_ppm(prefix + ".taskmanager-performance-keyboard.ppm")
    keyboard_restored = qmp.read_ppm(prefix + ".taskmanager-processes-keyboard.ppm")
    closed = qmp.read_ppm(prefix + ".taskmanager-closed.ppm")
    measure("palette-open", desktop, palette, minimum=0.06)
    measure("app-open", desktop, processes, minimum=0.12)
    measure("performance-tab", processes, performance, minimum=0.025)
    measure("processes-tab-restored", processes, restored, maximum=0.15)
    measure("keyboard-right-performance", restored, keyboard_performance, minimum=0.025)
    measure("keyboard-left-transition", keyboard_performance, keyboard_restored, minimum=0.025)
    measure("keyboard-processes-restored", restored, keyboard_restored, maximum=0.15)
    if shrink:
        resize_before = qmp.read_ppm(prefix + ".taskmanager-resize-before.ppm")
        short = qmp.read_ppm(prefix + ".taskmanager-short-wide.ppm")
        resize_restored = qmp.read_ppm(prefix + ".taskmanager-resize-restored.ppm")
        measure("short-window-resize", resize_before, short, minimum=0.015)
        measure("short-window-restored", resize_before, resize_restored, maximum=0.10)
    measure("desktop-restored", desktop, closed, maximum=0.10)
    print("[TASK-QMP] PASS: real Task Manager launch, PS/2 tab navigation, "
          "keyboard page navigation, bottom-edge resize where supported and Escape close changed/restored "
          "the guest framebuffer; compact layout semantics require visual review; "
          "the persistent system log must independently confirm app identity.")


if __name__ == "__main__":
    main()
