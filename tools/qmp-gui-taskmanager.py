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

        # Escape is a non-destructive close path in TaskManagerModernApp.
        qmp.send_keys(channel, "esc")
        time.sleep(2)
        qmp.screenshot(channel, prefix + ".taskmanager-closed.ppm")

    desktop = qmp.read_ppm(prefix + ".taskmanager-desktop.ppm")
    palette = qmp.read_ppm(prefix + ".taskmanager-palette.ppm")
    processes = qmp.read_ppm(prefix + ".taskmanager-processes.ppm")
    performance = qmp.read_ppm(prefix + ".taskmanager-performance.ppm")
    restored = qmp.read_ppm(prefix + ".taskmanager-processes-restored.ppm")
    closed = qmp.read_ppm(prefix + ".taskmanager-closed.ppm")
    measure("palette-open", desktop, palette, minimum=0.06)
    measure("app-open", desktop, processes, minimum=0.12)
    measure("performance-tab", processes, performance, minimum=0.025)
    measure("processes-tab-restored", processes, restored, maximum=0.15)
    measure("desktop-restored", desktop, closed, maximum=0.10)
    print("[TASK-QMP] PASS: real Task Manager launch, PS/2 tab navigation "
          "and Escape close changed/restored the guest framebuffer; "
          "the persistent system log must independently confirm app identity.")


if __name__ == "__main__":
    main()
