#!/usr/bin/env python3
"""
Fail-closed, in-place ISO update of a specific, verified one-stick installation.

The SanDisk 114.6 GiB USB contains a Limine hybrid ISO in sectors below 65536
and a FAT32 ZONDERQDATA primary partition beginning at sector 65536.
Never flash a complete ISO over the stick's MBR: the fourth entry disappears.
Instead, copy all new ISO sectors EXCEPT sector 0, then write a COMPOSED MBR
containing the new Limine bootstrap/ISO entries and the old ZONDERQDATA entry.
Every device, geometry and filesystem check must pass before any writes.

This is not an installer or a general-purpose disk partition editor.
"""
from __future__ import annotations

import argparse
from datetime import datetime
import os
from pathlib import Path
import stat
import struct
import subprocess
import sys

SECTOR = 512
DATA_START = 65536
EXPECTED_DATA_TYPE = 0x0C
EXPECTED_DISK_BYTES = 123048296448
EXPECTED_DEVICE = "/dev/sdb"
COPY_CHUNK = 1024 * 1024
LABEL = b"ZONDERQDATA"


def refuse(message: str) -> None:
    raise RuntimeError(message)


def command(*argv: str) -> str:
    return subprocess.check_output(argv, text=True).strip()


def entry(mbr: bytes, index: int) -> tuple[int, int, int]:
    off = 446 + index * 16
    item = mbr[off:off + 16]
    return item[4], *struct.unpack_from("<II", item, 8)


def check_partition_table(disk_mbr: bytes, iso_mbr: bytes, iso_size: int, device_bytes: int) -> bytes:
    if len(disk_mbr) != SECTOR or len(iso_mbr) != SECTOR:
        refuse("Incomplete MBR.")
    if disk_mbr[510:] != b"\x55\xaa" or iso_mbr[510:] != b"\x55\xaa":
        refuse("Missing DOS MBR magic.")
    if iso_size % SECTOR or iso_size < SECTOR * 64:
        refuse("ISO has an unexpected size/alignment.")
    if iso_size > DATA_START * SECTOR:
        refuse("New ISO overlaps the ZONDERQDATA partition. STOP; do not flash.")

    disk_data = entry(disk_mbr, 3)
    if disk_data != (EXPECTED_DATA_TYPE, DATA_START, (device_bytes // SECTOR) - DATA_START):
        refuse(f"Unexpected USB FAT32 partition #4: {disk_data!r}; no writes allowed.")
    if entry(iso_mbr, 3)[2] != 0:
        refuse("ISO unexpectedly defines a fourth MBR partition; refusing to overwrite data.")

    end_sectors = []
    for i in range(3):
        old_type, old_start, old_count = entry(disk_mbr, i)
        new_type, new_start, new_count = entry(iso_mbr, i)
        if new_type != old_type or new_start != old_start or new_count < 1:
            refuse(f"ISO partition {i + 1} moved/changed type; cannot update in place.")
        if new_start < 1 or new_start + new_count > DATA_START:
            refuse(f"ISO partition {i + 1} overlaps FAT32 data or MBR.")
        end_sectors.append((new_start, new_start + new_count))
    for i in range(3):
        for j in range(i + 1, 3):
            a, b = end_sectors[i], end_sectors[j]
            if a[0] < b[1] and b[0] < a[1]:
                refuse(f"ISO partitions {i + 1} and {j + 1} overlap.")
    # Preserve USB-specific disk signature + exact partition-4 bytes;
    # accept newly built Limine's BIOS stage-1 and updated ISO partition sizes.
    return (
        iso_mbr[:440] + disk_mbr[440:446]
        + iso_mbr[446:494] + disk_mbr[494:510] + iso_mbr[510:512]
    )


def validate_usb(device: str) -> int:
    if os.path.realpath(device) != EXPECTED_DEVICE:
        refuse(f"Only the verified {EXPECTED_DEVICE} is supported.")
    if not stat.S_ISBLK(os.stat(device).st_mode):
        refuse("Target is not a block device.")
    if command("lsblk", "-dn", "-o", "TRAN", device) != "usb":
        refuse("Target is not connected as USB.")
    model = command("lsblk", "-dn", "-o", "MODEL", device)
    if "SanDisk" not in model:
        refuse(f"Unexpected USB model: {model!r}.")
    size = int(command("blockdev", "--getsize64", device))
    if size != EXPECTED_DISK_BYTES:
        refuse(f"Unexpected device size: {size}.")
    mounts = command("lsblk", "-ln", "-o", "MOUNTPOINTS", device)
    if any(line.strip() for line in mounts.splitlines()):
        refuse("A partition on this USB stick is mounted. Unmount it first.")
    return size


def copy_exact(source, dest, count: int) -> None:
    pending = count
    while pending:
        chunk = source.read(min(COPY_CHUNK, pending))
        if not chunk:
            refuse("Unexpected EOF while updating ISO area.")
        dest.write(chunk)
        pending -= len(chunk)


def main() -> None:
    parser = argparse.ArgumentParser(description="Safely update hybrid ISO, preserve sdb4 FAT32.")
    parser.add_argument("--apply", action="store_true", help="Write after all safety checks and confirmation.")
    parser.add_argument("device", help="The verified SanDisk: /dev/sdb.")
    parser.add_argument("iso", type=Path, help="Previously compiled ZonderqOS-3094-disk.iso.")
    args = parser.parse_args()

    if os.geteuid() != 0:
        refuse("Run with sudo to read and update the USB block device.")
    device = os.path.realpath(args.device)
    capacity = validate_usb(device)
    iso = args.iso.resolve(strict=True)
    iso_size = iso.stat().st_size
    with iso.open("rb") as src, open(device, "rb", buffering=0) as disk:
        new_mbr = src.read(SECTOR)
        disk_mbr = disk.read(SECTOR)
        merged_mbr = check_partition_table(disk_mbr, new_mbr, iso_size, capacity)

        disk.seek(DATA_START * SECTOR + 71)
        actual_label = disk.read(len(LABEL))
        if actual_label != LABEL:
            refuse(f"USB data volume label is {actual_label!r}, not ZONDERQDATA.")

    print(f"[3094-USB] Target: {device}, SanDisk USB ({capacity} bytes)")
    print(f"[3094-USB] New ISO: {iso} ({iso_size} bytes)")
    print(f"[3094-USB] Existing FAT32 /dev/sdb4: starts sector {DATA_START}, remains unchanged.")
    print("[3094-USB] MBR will use new Limine boot code + first 3 ISO entries;")
    print("[3094-USB] old disk identifier and 4th ZONDERQDATA partition entry preserved.")
    print("[3094-USB] Checks: PASS. No write to sdb4 sectors is planned.")

    if not args.apply:
        print("[3094-USB] DRY RUN only. Back up important sdb4 files before --apply.")
        return

    reply = input(f"To apply, type exactly: UPDATE {EXPECTED_DEVICE}\n> ").strip()
    if reply != f"UPDATE {EXPECTED_DEVICE}":
        refuse("User confirmation did not match; no changes made.")

    # A 32-MiB boot-region backup includes the original MBR and its bootloader
    # but excludes partition 4: don't silently claim it backs up user files.
    backups = iso.parent / "usb-boot-backups"
    backups.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    backup_path = backups / f"zonderq-sdb-first32m-{stamp}.img"
    with open(device, "rb", buffering=0) as disk, backup_path.open("xb") as out:
        copy_exact(disk, out, DATA_START * SECTOR)
        out.flush()
        os.fsync(out.fileno())
    print(f"[3094-USB] Saved original first 32 MiB to {backup_path}")

    # Recheck after the backup: fail closed if media or partition metadata
    # changed while it was being read.
    validate_usb(device)
    with open(device, "rb", buffering=0) as disk:
        if disk.read(SECTOR) != disk_mbr:
            refuse("Device MBR changed before update. Aborting.")
        disk.seek(DATA_START * SECTOR + 71)
        if disk.read(len(LABEL)) != LABEL:
            refuse("Data partition signature changed. Aborting.")

    print("[3094-USB] Updating only sectors 1..ISO-end. Saving sector 0 last...")
    with iso.open("rb") as src, open(device, "r+b", buffering=0) as disk:
        src.seek(SECTOR)
        disk.seek(SECTOR)
        copy_exact(src, disk, iso_size - SECTOR)
        os.fsync(disk.fileno())
        # This commit point installs new BIOS loader + MBR entries in a
        # single sector, preserving partition 4.
        disk.seek(0)
        disk.write(merged_mbr)
        os.fsync(disk.fileno())

    with iso.open("rb") as src, open(device, "rb", buffering=0) as disk:
        if disk.read(SECTOR) != merged_mbr:
            refuse("MBR read-back verification FAILED.")
        src.seek(SECTOR)
        left = iso_size - SECTOR
        while left:
            n = min(COPY_CHUNK, left)
            if src.read(n) != disk.read(n):
                refuse("ISO read-back verification FAILED.")
            left -= n
        disk.seek(DATA_START * SECTOR + 71)
        if disk.read(len(LABEL)) != LABEL:
            refuse("FAT32 data volume label changed unexpectedly.")

    print("[3094-USB] PASS: new ISO installed, sdb4 FAT32 region untouched.")
    print("[3094-USB] The USB remains a BIOS/UEFI Limine disk + persistent ZONDERQDATA.")
    print("[3094-USB] Select UEFI: SanDisk in the laptop boot menu when available.")


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, OSError, subprocess.CalledProcessError) as exc:
        print(f"[3094-USB][STOP] {exc}", file=sys.stderr)
        sys.exit(1)
