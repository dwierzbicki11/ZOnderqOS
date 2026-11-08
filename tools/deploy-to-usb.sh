#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KERNEL_BIN="$ROOT/output-3095/updates-package/ZonderqOS.elf"
UPDATES_DIR="$ROOT/output-3095/updates-package"

echo "=== ZOnderqOS USB Deployer ==="

# Find partition with label ZONDERQDATA
DEV="$(lsblk -lno PATH,LABEL | awk '$2=="ZONDERQDATA"{print $1}' | head -n 1 || true)"

if [[ -z "$DEV" ]]; then
  echo "[BLAD] Nie wykryto partycji ZONDERQDATA na podlaczonym pendrive!"
  echo "Podlacz pendrive z systemem ZOnderqOS do portu USB i sprobuj ponownie."
  exit 1
fi

echo "-> Wykryto partycje ZONDERQDATA na urzadzeniu: $DEV"

# Mount using udisksctl (no sudo password required)
MOUNT_INFO="$(udisksctl mount -b "$DEV" --no-user-interaction 2>/dev/null || true)"
MOUNT_PATH="$(lsblk -lno MOUNTPOINT "$DEV" | head -n 1 || true)"

if [[ -z "$MOUNT_PATH" || ! -d "$MOUNT_PATH" ]]; then
  echo "[BLAD] Nie udalo sie zamontowac partycji $DEV."
  exit 1
fi

echo "-> Zamontowano w: $MOUNT_PATH"

# 1. Update boot kernel in both slots and root
mkdir -p "$MOUNT_PATH/boot/slot_a"
mkdir -p "$MOUNT_PATH/boot/slot_b"
echo "-> Kopiowanie nowego kernela do Slotu A ($MOUNT_PATH/boot/slot_a/ZonderqOS.elf)..."
cp -f "$KERNEL_BIN" "$MOUNT_PATH/boot/slot_a/ZonderqOS.elf"
echo "-> Kopiowanie nowego kernela do Slotu B ($MOUNT_PATH/boot/slot_b/ZonderqOS.elf)..."
cp -f "$KERNEL_BIN" "$MOUNT_PATH/boot/slot_b/ZonderqOS.elf"
echo "-> Kopiowanie nowego kernela do /boot/ZonderqOS.elf..."
cp -f "$KERNEL_BIN" "$MOUNT_PATH/boot/ZonderqOS.elf"

KERNEL_SHA="$(sha256sum "$KERNEL_BIN" | awk '{print $1}')"
echo "$KERNEL_SHA" > "$MOUNT_PATH/boot/slot_a/kernel.sha256"
echo "$KERNEL_SHA" > "$MOUNT_PATH/boot/slot_b/kernel.sha256"

# 2. Update incoming packages in /updates
mkdir -p "$MOUNT_PATH/updates"
echo "-> Kopiowanie pakietu aktualizacji do $MOUNT_PATH/updates/..."
cp -rf "$UPDATES_DIR"/* "$MOUNT_PATH/updates/"

echo "-> Synchronizacja buforow USB (sync)..."
sync

echo "-> Bezpieczne odmontowywanie partycji USB..."
udisksctl unmount -b "$DEV" --no-user-interaction || true

echo "=== Pomyslnie zaktualizowano pendrive ZOnderqOS! ==="
echo "Mozesz bezpiecznie odpiac pendrive i uruchomic laptopa."
