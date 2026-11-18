#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ISO_PATH="${1:-$ROOT_DIR/output-x64/ZonderqOS.iso}"

if [ ! -s "$ISO_PATH" ]; then
    echo "ERROR: ISO not found at $ISO_PATH"
    exit 1
fi

make -C "$ROOT_DIR/tools/bootguard" all >/dev/null
BOOTGUARD_EFI="$ROOT_DIR/tools/bootguard/build/bootguard.efi"
test -s "$BOOTGUARD_EFI"

WORK_DIR="$(mktemp -d /tmp/zonderqos-iso-patch-XXXXXX)"
trap 'rm -rf "$WORK_DIR"' EXIT

mkdir -p "$WORK_DIR/iso_root"
xorriso -osirrox on -indev "$ISO_PATH" -extract / "$WORK_DIR/iso_root" >/dev/null 2>&1
chmod -R u+w "$WORK_DIR/iso_root"

if [ ! -f "$WORK_DIR/iso_root/EFI/BOOT/LIMINE.EFI" ]; then
    cp "$WORK_DIR/iso_root/EFI/BOOT/BOOTX64.EFI" "$WORK_DIR/iso_root/EFI/BOOT/LIMINE.EFI"
fi
cp "$BOOTGUARD_EFI" "$WORK_DIR/iso_root/EFI/BOOT/BOOTX64.EFI"
cp "$ROOT_DIR/Bootloader/limine.conf" "$WORK_DIR/iso_root/boot/limine/limine.conf"

if [ -f "$WORK_DIR/iso_root/boot/limine/limine-uefi-cd.bin" ]; then
    mcopy -o -i "$WORK_DIR/iso_root/boot/limine/limine-uefi-cd.bin" "$WORK_DIR/iso_root/EFI/BOOT/LIMINE.EFI" ::/EFI/BOOT/LIMINE.EFI
    mcopy -o -i "$WORK_DIR/iso_root/boot/limine/limine-uefi-cd.bin" "$BOOTGUARD_EFI" ::/EFI/BOOT/BOOTX64.EFI
fi

xorriso -as mkisofs -b boot/limine/limine-bios-cd.bin \
    -no-emul-boot -boot-load-size 4 -boot-info-table \
    --efi-boot boot/limine/limine-uefi-cd.bin \
    -efi-boot-part --efi-boot-image --protective-msdos-label \
    "$WORK_DIR/iso_root" -o "$WORK_DIR/patched.iso" >/dev/null 2>&1

mv "$WORK_DIR/patched.iso" "$ISO_PATH"
echo "[PATCH-ISO][OK] Installed BootGuard -> LIMINE.EFI -> ZonderqOS.elf into $ISO_PATH"
