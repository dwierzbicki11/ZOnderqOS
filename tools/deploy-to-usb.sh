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

# 2. Update /etc/os-release and /boot/boot_state.cfg to prevent kernel-disk mismatch
mkdir -p "$MOUNT_PATH/etc"
cat > "$MOUNT_PATH/etc/os-release" <<'EOF'
NAME="ZOnderqOS"
VERSION="1.0.1"
ID=zonderqos
ID_LIKE=cosmos
VERSION_ID=1.0.1
PRETTY_NAME="ZOnderqOS 1.0.1 (Cosmos 3.0.95)"
BUILD_ID=3110
VARIANT="Workstation"
VARIANT_ID=workstation
HOME_URL="https://github.com/zonderq/ZOnderqOS"
SUPPORT_URL="https://github.com/zonderq/ZOnderqOS/issues"
BUG_REPORT_URL="https://github.com/zonderq/ZOnderqOS/issues"
EOF

cat > "$MOUNT_PATH/boot/boot_state.cfg" <<'EOF'
ACTIVE_SLOT=A
FALLBACK_SLOT=B
PENDING_SLOT=
BOOT_ATTEMPTS=0
SLOT_A_STATUS=Healthy
SLOT_B_STATUS=Healthy
SLOT_A_VERSION=1.0.1
SLOT_A_BUILD=3110
SLOT_B_VERSION=1.0.1
SLOT_B_BUILD=3110
EOF

# 2.5. Write autonomous Limine configuration pointing Entry 1 to active Slot A
mkdir -p "$MOUNT_PATH/boot/limine"
cat > "$MOUNT_PATH/boot/limine.conf" <<'EOF'
# Cosmos Gen3 / Limine v10 A/B dual-slot boot configuration.
timeout: 2
default_entry: 1

/ZOnderqOS
    protocol: limine
    resolution: 1366x768x32
    path: fslabel(ZONDERQDATA):/boot/slot_a/ZonderqOS.elf

/ZOnderqOS (Poprzednia wersja / Slot B)
    protocol: limine
    resolution: 1366x768x32
    path: fslabel(ZONDERQDATA):/boot/slot_b/ZonderqOS.elf

/ZOnderqOS (Kopia zapasowa ISO / Ratunkowa)
    protocol: limine
    resolution: 1366x768x32
    path: boot():/boot/ZonderqOS.elf
EOF

cp -f "$MOUNT_PATH/boot/limine.conf" "$MOUNT_PATH/limine.conf"
cp -f "$MOUNT_PATH/boot/limine.conf" "$MOUNT_PATH/boot/limine/limine.conf"

# 3. Update incoming packages in /updates and /var/updates/incoming
mkdir -p "$MOUNT_PATH/updates"
mkdir -p "$MOUNT_PATH/var/updates/incoming"
echo "-> Kopiowanie pakietu aktualizacji do $MOUNT_PATH/updates/ i /var/updates/incoming/..."
cp -rf "$UPDATES_DIR"/* "$MOUNT_PATH/updates/"
cp -rf "$UPDATES_DIR"/* "$MOUNT_PATH/var/updates/incoming/"

# Weryfikacja obecnosci plikow aktualizacji
if [[ ! -f "$MOUNT_PATH/updates/update.manifest" || ! -f "$MOUNT_PATH/updates/ZonderqOS.elf" ]]; then
    echo "[BLAD] Pliki aktualizacji nie zostaly poprawnie skopiowane do /updates!"
    exit 1
fi
echo "-> Zweryfikowano: pakiet aktualizacji (wersja $(awk -F= '$1=="VERSION"{print $2}' "$MOUNT_PATH/updates/update.manifest")) jest gotowy w /updates."

echo "-> Synchronizacja buforow USB (sync)..."
sync

echo "-> Bezpieczne odmontowywanie partycji USB..."
udisksctl unmount -b "$DEV" --no-user-interaction || true

echo "=== Pomyslnie zaktualizowano pendrive ZOnderqOS! ==="
echo "Mozesz bezpiecznie odpiac pendrive i uruchomic laptopa."
