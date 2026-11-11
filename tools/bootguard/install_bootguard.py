#!/usr/bin/env python3
"""Install independent EFI BootGuard, with explicit disk and recovery checks.

Default mode DRY RUN. Never automatically modifies a foreign ESP.
For isolated directories: --fixture --apply.
For real mounted volumes: --real-device --apply, and --limine-sha256 HASH.

Original Limine BOOTX64.EFI is preserved as EFI/BOOT/LIMINE.EFI.
EFI BootGuard is placed at EFI/BOOT/BOOTX64.EFI only after readback.
Only then is BOOTGUARD_PREBOOT=1 written to ZONDERQDATA/boot/boot_state.cfg.
"""
import argparse
import hashlib
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

STATE_MAGIC = "# ZonderqOS A/B Bootloader State Configuration"
SHA256 = re.compile(r"[0-9a-fA-F]{64}\Z")


def sha256(path: Path) -> str:
    with path.open("rb") as source:
        h = hashlib.sha256()
        for block in iter(lambda: source.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def check_pe_x64_efi(path: Path):
    content = path.read_bytes()
    if len(content) < 256 or content[:2] != b"MZ":
        raise ValueError(f"Not PE/EFI: {path}")
    offset = int.from_bytes(content[0x3c:0x40], "little")
    if offset < 64 or offset + 26 > len(content) or content[offset:offset+4] != b"PE\0\0":
        raise ValueError("Invalid PE header: " + str(path))
    machine = int.from_bytes(content[offset+4:offset+6], "little")
    optional_magic = int.from_bytes(content[offset+24:offset+26], "little")
    if machine != 0x8664 or optional_magic != 0x20b:
        raise ValueError("Expected x86_64 PE32+ EFI application: " + str(path))
    subsystem = int.from_bytes(content[offset+24+68:offset+24+70], "little")
    if subsystem != 10:
        raise ValueError("PE subsystem is not EFI_APPLICATION: " + str(path))


def managed_path(root: Path, *parts: str) -> Path:
    if root.is_symlink():
        raise ValueError("Symlink mount root")
    current = root
    for part in parts:
        current /= part
        if current.is_symlink():
            raise ValueError("Symlink component: " + str(current))
    return current


def read_state(path: Path) -> str:
    text = path.read_text(encoding="ascii")
    if not text.startswith(STATE_MAGIC + "\n"):
        raise ValueError("Not a ZonderqOS boot state")
    if len(text) > 4096:
        raise ValueError("Boot state too large")
    if text.count("BOOTGUARD_PREBOOT=") != 1 or text.count("BOOTGUARD_FALLBACK=") != 1:
        raise ValueError("Missing or duplicate BootGuard flags")
    if "BOOTGUARD_PREBOOT=0\n" not in text:
        raise ValueError("Already armed or invalid BootGuard marker")
    if "BOOTGUARD_FALLBACK=0\n" not in text:
        raise ValueError("Recovery pending, cannot replace the EFI loader")
    if "PENDING_SLOT=\n" not in text:
        raise ValueError("Pending update, cannot replace the EFI loader")
    return text


def check_mount_label(root: Path, label: str):
    if not os.path.ismount(root):
        raise ValueError(f"Real-device mode requires a mountpoint: {root}")
    p = subprocess.run(["findmnt", "-n", "-o", "LABEL", "--target", str(root)],
                       capture_output=True, text=True, check=True)
    actual = p.stdout.strip()
    if actual != label:
        raise ValueError(f"Refusing volume {root}, expected {label!r}, got {actual!r}")


def flush_copy(src: Path, dst: Path, expected: str):
    if dst.exists():
        raise ValueError("Leftover installation staging file: " + str(dst))
    with src.open("rb") as inp, dst.open("xb") as out:
        shutil.copyfileobj(inp, out, length=1 << 20)
        out.flush()
        os.fsync(out.fileno())
    if sha256(dst) != expected:
        raise IOError("EFI readback SHA256 mismatch: " + str(dst))


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--esp", type=Path, required=True)
    p.add_argument("--data", type=Path, required=True)
    p.add_argument("--bootguard-efi", type=Path, required=True)
    p.add_argument("--limine-sha256", required=True)
    p.add_argument("--apply", action="store_true")
    modes = p.add_mutually_exclusive_group(required=True)
    modes.add_argument("--fixture", action="store_true")
    modes.add_argument("--real-device", action="store_true")
    args = p.parse_args()
    try:
        esp = args.esp.resolve(strict=True)
        data = args.data.resolve(strict=True)
        if not esp.is_dir() or not data.is_dir() or esp == data:
            raise ValueError("Separate ESP and ZONDERQDATA directories required")
        if args.real_device:
            check_mount_label(data, "ZONDERQDATA")
            if not os.path.ismount(esp):
                raise ValueError("ESP must be an explicitly mounted volume")
        if not SHA256.fullmatch(args.limine_sha256):
            raise ValueError("Expected original Limine SHA-256 required")
        state = managed_path(data, "boot", "boot_state.cfg")
        original = managed_path(esp, "EFI", "BOOT", "BOOTX64.EFI")
        backup = managed_path(esp, "EFI", "BOOT", "LIMINE.EFI")
        guard = args.bootguard_efi.resolve(strict=True)
        if not original.is_file() or not state.is_file() or backup.exists():
            raise ValueError("Missing BOOTX64.EFI/state or LIMINE.EFI already exists")
        if original.resolve() == guard:
            raise ValueError("Cannot use currently installed Limine as BootGuard")
        check_pe_x64_efi(guard)
        check_pe_x64_efi(original)
        expected = args.limine_sha256.lower()
        if sha256(original) != expected:
            raise ValueError("Original Limine SHA-256 mismatch")
        state_text = read_state(state)
        if not args.apply:
            print("DRY RUN PASS: paths, PE files, hash, state; no writes")
            return 0

        limine_tmp = backup.with_suffix(".EFI.part")
        guard_tmp = original.with_suffix(".EFI.part")
        if limine_tmp.exists() or guard_tmp.exists():
            raise ValueError("Interrupted previous install; manual inspection required")
        flush_copy(original, limine_tmp, expected)
        os.replace(limine_tmp, backup)
        guard_sha = sha256(guard)
        flush_copy(guard, guard_tmp, guard_sha)
        os.replace(guard_tmp, original)
        if sha256(original) != guard_sha or sha256(backup) != expected:
            raise IOError("Installed EFI binaries failed readback")
        new_text = state_text.replace("BOOTGUARD_PREBOOT=0\n", "BOOTGUARD_PREBOOT=1\n")
        with state.open("r+b") as out:
            content = new_text.encode("ascii")
            if len(content) != len(state_text.encode("ascii")):
                raise IOError("State length changed unexpectedly")
            out.seek(0)
            out.write(content)
            out.flush()
            os.fsync(out.fileno())
        print("INSTALLED: Limine backed up, BootGuard staged, preboot state armed")
        return 0
    except (ValueError, OSError, subprocess.CalledProcessError, UnicodeError) as exc:
        print("STOP: " + str(exc), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
