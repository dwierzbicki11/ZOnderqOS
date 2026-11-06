#!/usr/bin/env bash
# Interactive, disk-backed ZonderqOS Gen3 desktop on stock Cosmos 3.0.95.
# Uses ONLY a persistent regular-file disk image; never a host block device.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ISO="$ROOT/output-3095/ZonderqOS-3095-disk.iso"
OUT="$ROOT/output-3095/qemu-gui"
DISK="$OUT/zonderqdata.img"

fail() { printf '[ZONDERQ-QEMU][ERROR] %s\n' "$*" >&2; exit 1; }

for cmd in qemu-system-x86_64 sfdisk mkfs.vfat truncate; do
  command -v "$cmd" >/dev/null 2>&1 || fail "Missing $cmd. Install qemu-system-x86, fdisk and dosfstools."
done
[[ -s "$ISO" ]] || fail "Missing disk-backed ISO. Run: ./run.sh --build"
[[ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]] || fail "No graphical display; start this on your desktop."

mkdir -p "$OUT"
if [[ ! -e "$DISK" ]]; then
  TMP="$OUT/.zonderqdata.img.tmp.$$"
  [[ ! -e "$TMP" ]] || fail "Temporary image already exists: $TMP"
  cleanup() { rm -f -- "$TMP"; }
  trap cleanup EXIT
  truncate -s 256M "$TMP"
  printf 'label: dos\nunit: sectors\nstart=2048, type=c\n' | sfdisk "$TMP" >/dev/null
  # FAT32 begins at LBA 2048 (1 MiB), matching the partition table.
  mkfs.vfat -I -F 32 -n ZONDERQDATA -h 2048 --offset 2048 "$TMP" >/dev/null
  mv -- "$TMP" "$DISK"
  trap - EXIT
  printf '[ZONDERQ-QEMU] Created dedicated FAT32 image: %s\n' "$DISK"
else
  [[ -f "$DISK" && ! -L "$DISK" ]] || fail "Disk path is not a regular file: $DISK"
  printf '[ZONDERQ-QEMU] Reusing persistent disk image: %s\n' "$DISK"
fi

printf '[ZONDERQ-QEMU] Booting full GUI, 1 vCPU, stock Cosmos 3.0.95.\n'
exec qemu-system-x86_64 \
  -M q35 -accel tcg -cpu Nehalem -smp 1 -m 2048M \
  -vga std \
  -drive "file=$ISO,if=none,id=cosmoscd,format=raw,readonly=on" \
  -device ide-cd,drive=cosmoscd,bootindex=0 -boot d \
  -device ich9-ahci,id=ahci0 \
  -drive "file=$DISK,if=none,id=ahcidisk0,format=raw,cache=directsync" \
  -device ide-hd,drive=ahcidisk0,bus=ahci0.0 \
  -device qemu-xhci,id=xhci -device usb-mouse,bus=xhci.0 \
  -monitor none -serial stdio -no-reboot
