#!/usr/bin/env python3
"""Verify the actual produced ISO boots its embedded rescue kernel by default.

This is a read-only, fail-closed packaging gate. It does not infer boot from
a successful C# compile. QEMU still needs to boot the same exact ISO.
"""
import argparse
from pathlib import Path
import re
import subprocess
import sys
import tempfile


def iso_paths(iso):
    command = ["xorriso", "-indev", str(iso), "-find", "/", "-type", "f", "-print"]
    output = subprocess.run(command, check=True, capture_output=True, text=True).stdout
    return {line.strip() for line in output.splitlines() if line.strip().startswith("/")}


def extract(iso, member, dest):
    subprocess.run(
        ["xorriso", "-osirrox", "on", "-indev", str(iso),
         "-extract", member, str(dest)],
        check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)


def validate_config(content):
    lines = content.splitlines()
    entries = []
    global_options = {}
    active = None
    for line in lines:
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        if line.startswith("/"):
            active = {"name": line[1:], "options": {}}
            entries.append(active)
            continue
        if ":" not in line:
            raise ValueError("Malformed Limine line: " + line)
        name, value = (x.strip() for x in line.split(":", 1))
        if not name or not value:
            raise ValueError("Empty Limine option: " + line)
        destination = global_options if active is None else active["options"]
        if name in destination:
            raise ValueError("Duplicate Limine option: " + name)
        destination[name] = value

    try:
        default = int(global_options["default_entry"])
    except (KeyError, ValueError) as exc:
        raise ValueError("Missing or invalid default_entry") from exc
    if len(entries) < 3 or default != 3:
        raise ValueError("Fresh ISO must select its bundled recovery entry 3, not empty A/B slots")
    candidate = entries[default - 1]
    if "recovery" not in candidate["name"].lower() and "ratunkow" not in candidate["name"].lower():
        raise ValueError("Default Limine entry is not the recovery image")
    opts = candidate["options"]
    if opts.get("protocol", "").lower() != "limine":
        raise ValueError("Recovery entry does not use the Limine protocol")
    if opts.get("path", opts.get("kernel_path")) != "boot():/boot/ZonderqOS.elf":
        raise ValueError("Recovery entry must load the ISO's own embedded ELF")
    for slot in ("slot_a", "slot_b"):
        if not any(slot in e["options"].get("path", "").lower() for e in entries):
            raise ValueError("Missing distinct A/B entry: " + slot)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("iso", type=Path)
    args = ap.parse_args()
    if not args.iso.is_file():
        ap.error("ISO missing")
    try:
        paths = iso_paths(args.iso)
        if "/boot/ZonderqOS.elf" not in paths:
            raise ValueError("ISO is missing /boot/ZonderqOS.elf")
        locations = (
            "/boot/limine/limine.conf", "/boot/limine.conf", "/limine/limine.conf",
            "/limine.conf", "/EFI/BOOT/limine.conf"
        )
        config = next((p for p in locations if p in paths), None)
        if config is None:
            raise ValueError("No Limine config in ISO")
        with tempfile.TemporaryDirectory() as directory:
            dest = Path(directory) / "limine.conf"
            extract(args.iso, config, dest)
            validate_config(dest.read_text(encoding="utf-8"))
        print("ISO BOOT CONFIG PASS: recovery entry 3 points to embedded kernel, A/B preserved")
        return 0
    except (OSError, ValueError, subprocess.CalledProcessError) as exc:
        print("ISO BOOT CONFIG FAIL:", exc, file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
