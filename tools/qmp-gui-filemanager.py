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

    before = read_ppm(prefix + ".filemanager-before.ppm")
    palette = read_ppm(prefix + ".filemanager-palette.ppm")
    after = read_ppm(prefix + ".filemanager-after.ppm")
    for label, first, second in (
        ("palette-open", before, palette),
        ("app-vs-desktop", before, after),
        ("app-vs-palette", palette, after),
    ):
        changed, samples = difference(first, second)
        fraction = changed / max(1, samples)
        print(f"[GUI-QMP] {label}: changed {changed}/{samples} sampled pixels ({fraction:.1%})")
        if fraction < 0.12:
            raise RuntimeError(label + ": insufficient framebuffer change; inspect PPMs")
    print("[GUI-QMP] PASS significant framebuffer changes after real QMP keyboard actions; visual app identity requires inspection.")


if __name__ == "__main__":
    main()
