#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ISO_PATH="${1:-$ROOT_DIR/output-x64/ZonderqOS.iso}"
WORK_DIR="$(mktemp -d /tmp/zonderqos-real-bootguard-XXXXXX)"
trap 'rm -rf "$WORK_DIR"' EXIT

if [ ! -s "$ISO_PATH" ]; then
    echo "FAIL: ISO not found at $ISO_PATH"
    exit 1
fi

make -C "$ROOT_DIR/tools/bootguard" all >/dev/null

# Extract production Limine BOOTX64.EFI and ZonderqOS.elf from the built x64 ISO
mkdir -p "$WORK_DIR/iso_extract"
if command -v xorriso >/dev/null 2>&1; then
if xorriso -osirrox on -indev "$ISO_PATH" -extract /EFI/BOOT/LIMINE.EFI "$WORK_DIR/iso_extract/limine_x64.efi" >/dev/null 2>&1; then
        true
    else
        xorriso -osirrox on -indev "$ISO_PATH" -extract /EFI/BOOT/BOOTX64.EFI "$WORK_DIR/iso_extract/limine_x64.efi" >/dev/null 2>&1
    fi
    xorriso -osirrox on -indev "$ISO_PATH" -extract /boot/ZonderqOS.elf "$WORK_DIR/iso_extract/ZonderqOS.elf" >/dev/null 2>&1
else
    echo "FAIL: xorriso required to extract production Limine and ELF from ISO"
    exit 1
fi

test -s "$WORK_DIR/iso_extract/limine_x64.efi"
test -s "$WORK_DIR/iso_extract/ZonderqOS.elf"

# Create disposable 64 MiB FAT32 disk image with BootGuard -> Limine -> ZonderqOS.elf
IMG="$WORK_DIR/bootguard_real.img"
truncate -s 64M "$IMG"
mkfs.vfat -F 32 -n ZONDERQDATA "$IMG" >/dev/null

mmd -i "$IMG" ::/EFI ::/EFI/BOOT ::/boot ::/boot/slot_a ::/boot/limine
mcopy -i "$IMG" "$ROOT_DIR/tools/bootguard/build/bootguard.efi" ::/EFI/BOOT/BOOTX64.EFI
mcopy -i "$IMG" "$WORK_DIR/iso_extract/limine_x64.efi" ::/EFI/BOOT/LIMINE.EFI
mcopy -i "$IMG" "$WORK_DIR/iso_extract/ZonderqOS.elf" ::/boot/slot_a/ZonderqOS.elf
mcopy -i "$IMG" "$ROOT_DIR/Bootloader/limine.conf" ::/boot/limine/limine.conf

OVMF_FW="/usr/share/ovmf/OVMF.fd"
if [ ! -f "$OVMF_FW" ]; then
    OVMF_FW="/usr/share/OVMF/OVMF_CODE_4M.fd"
fi

SERIAL_LOG="$WORK_DIR/serial.log"
timeout 30 qemu-system-x86_64 \
    -machine q35 \
    -m 4096 \
    -smp 1 \
    -bios "$OVMF_FW" \
    -drive "file=$IMG,format=raw,if=ide" \
    -serial "file:$SERIAL_LOG" \
    -display none \
    -no-reboot || true

echo "=== OVMF Real Chainload Serial Output ==="
cat "$SERIAL_LOG" | tail -n 30
if grep -qiE "ZonderqOS.*Kernel|Cosmos|\[MEM\]|\[BOOT\]" "$SERIAL_LOG"; then
    echo "REAL BOOTGUARD CHAINLOAD OVMF PASS"
    exit 0
else
    echo "REAL BOOTGUARD CHAINLOAD OVMF: no kernel/bootguard marker on COM1"
    exit 1
fi
