#!/usr/bin/env bash
# Launch the full ZonderqOS Gen3 GUI with a dedicated persistent QEMU FAT32 disk.
# The image is created only once. Never operates on a host block device.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

if [[ "${1:-}" == "--build" ]]; then
  bash ./run.sh --build
elif [[ $# -ne 0 ]]; then
  echo "Usage: bash tools/run-game-qemu.sh [--build]" >&2
  exit 2
fi

ISO="$ROOT/output-3095/ZonderqOS-3095-disk.iso"
DISK_DIR="$ROOT/output-3095/qemu"
DISK="$DISK_DIR/games-data.img"

for cmd in qemu-system-x86_64 truncate sfdisk mkfs.vfat; do
  command -v "$cmd" >/dev/null || {
    echo "[GAMES-QEMU][ERROR] Missing tool: $cmd" >&2
    exit 1
  }
done
[[ -s "$ISO" ]] || {
  echo "[GAMES-QEMU][ERROR] Missing GUI ISO: $ISO (run with --build)" >&2
  exit 1
}

mkdir -p "$DISK_DIR"
if [[ ! -e "$DISK" ]]; then
  echo "[GAMES-QEMU] Creating 256 MiB private QEMU data disk..."
  truncate -s 256M "$DISK"
  printf 'label: dos\nunit: sectors\nstart=2048, type=c\n' | sfdisk "$DISK"
  mkfs.vfat -I -F 32 -n ZONDERQDATA -h 2048 --offset 2048 "$DISK"
fi

echo "[GAMES-QEMU] Booting GUI ISO with persistent disk: $DISK"
exec qemu-system-x86_64 \
  -M q35 -accel tcg -cpu Nehalem -smp 1 -m 2G \
  -drive "file=$ISO,if=none,id=cosmoscd,format=raw,readonly=on" \
  -device ide-cd,drive=cosmoscd,bootindex=0 -boot d \
  -device ich9-ahci,id=ahci0 \
  -drive "file=$DISK,if=none,id=ahcidisk0,format=raw" \
  -device ide-hd,drive=ahcidisk0,bus=ahci0.0 \
  -device qemu-xhci,id=xhci -device usb-mouse,bus=xhci.0 \
  -vga std -serial stdio -no-reboot
