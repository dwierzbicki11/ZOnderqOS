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
    command = ["xorriso", "-indev", str(iso), "-find", "/", "-type", "f", "-exec", "echo", "--"]
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
        # Do not infer file existence from xorriso -find textual formatting:
        # the ISO tool quotes names differently across versions. Extract the
        # exact path the bootloader will consume and validate the bytes.
        locations = (
            "/boot/limine/limine.conf", "/boot/limine.conf", "/limine/limine.conf",
            "/limine.conf", "/EFI/BOOT/limine.conf"
        )
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            kernel = root / "candidate.elf"
            try:
                extract(args.iso, "/boot/ZonderqOS.elf", kernel)
            except subprocess.CalledProcessError as exc:
                raise ValueError(
                    "ISO does not contain boot():/boot/ZonderqOS.elf; "
                    "Cosmos SDK output path and Limine config must match"
                ) from exc
            image = kernel.read_bytes()
            if len(image) < 64 or image[:4] != b"\\x7fELF" or image[4] != 2 or image[5] != 1:
                raise ValueError("ISO kernel is not a nontruncated little-endian ELF64")
            if int.from_bytes(image[18:20], "little") != 62:
                raise ValueError("ISO kernel is not an x86_64 ELF")
            config = None
            for path in locations:
                dest = root / "limine.conf"
                try:
                    extract(args.iso, path, dest)
                    config = path
                    break
                except subprocess.CalledProcessError:
                    if dest.exists():
                        dest.unlink()
            if config is None:
                raise ValueError("No recognized Limine config in ISO")
            validate_config(dest.read_text(encoding="utf-8"))
        print("ISO BOOT CONFIG PASS: embedded ELF64 x86_64, entry 3 recovery, A/B preserved")
        return 0
    except (OSError, ValueError, subprocess.CalledProcessError) as exc:
        print("ISO BOOT CONFIG FAIL:", exc, file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
