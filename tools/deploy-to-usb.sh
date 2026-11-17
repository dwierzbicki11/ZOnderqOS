#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KERNEL_BIN="$ROOT/output-3095/updates-package/ZonderqOS.elf"
UPDATES_DIR="$ROOT/output-3095/updates-package"
MAX_IMAGE_BYTES=$((32 * 1024 * 1024)) # 32 MiB slot/ISO safety guard

echo "=== ZOnderqOS USB Deployer ==="

for cmd in lsblk udisksctl sha256sum stat awk cp mkdir sync date; do
  if ! command -v "$cmd" >/dev/null 2>&1; then
    echo "[BLAD] Brakuje wymaganego narzedzia: $cmd" >&2
    exit 1
  fi
done

if [[ ! -f "$KERNEL_BIN" || ! -s "$KERNEL_BIN" ]]; then
  echo "[BLAD] Brak pliku kernela: $KERNEL_BIN" >&2
  exit 1
fi

KERNEL_BYTES="$(stat -c%s "$KERNEL_BIN")"
if (( KERNEL_BYTES <= 0 || KERNEL_BYTES > MAX_IMAGE_BYTES )); then
  echo "[BLAD] Obraz kernela $KERNEL_BIN ma niedozwolony rozmiar ($KERNEL_BYTES B; limit <= $MAX_IMAGE_BYTES B / 32 MiB)." >&2
  exit 1
fi

if [[ ! -d "$UPDATES_DIR" || ! -f "$UPDATES_DIR/update.manifest" || ! -f "$UPDATES_DIR/ZonderqOS.elf" ]]; then
  echo "[BLAD] Niekompletny katalog aktualizacji: $UPDATES_DIR" >&2
  exit 1
fi

# Find partition with label ZONDERQDATA
DEV="$(lsblk -lno PATH,LABEL | awk '$2=="ZONDERQDATA"{print $1; exit}' || true)"

if [[ -z "$DEV" ]]; then
  echo "[BLAD] Nie wykryto partycji ZONDERQDATA na podlaczonym pendrive!" >&2
  echo "Podlacz pendrive z systemem ZOnderqOS do portu USB i sprobuj ponownie." >&2
  exit 1
fi

echo "-> Wykryto partycje ZONDERQDATA na urzadzeniu: $DEV"

# Mount using udisksctl (no sudo password required)
udisksctl mount -b "$DEV" --no-user-interaction >/dev/null 2>&1 || true
MOUNT_PATH="$(lsblk -lno MOUNTPOINT "$DEV" | awk 'NF{print; exit}' || true)"

if [[ -z "$MOUNT_PATH" || ! -d "$MOUNT_PATH" ]]; then
  echo "[BLAD] Nie udalo sie zamontowac partycji $DEV." >&2
  exit 1
fi

echo "-> Zamontowano w: $MOUNT_PATH"

# 1. Update boot kernel in active slot and root while preserving existing A/B fallback slot
mkdir -p "$MOUNT_PATH/boot/slot_a"
mkdir -p "$MOUNT_PATH/boot/slot_b"

read_boot_cfg() {
  local key="$1"
  local fallback="$2"
  local cfg="$MOUNT_PATH/boot/boot_state.cfg"
  if [[ -f "$cfg" ]]; then
    local val
    val="$(awk -F= -v k="$key" '$1 ~ "^[[:space:]]*" k "[[:space:]]*$"{gsub(/^[[:space:]]+|[[:space:]\r]+$/,"",$2); print $2; exit}' "$cfg")"
    if [[ -n "$val" ]]; then
      printf '%s\n' "$val"
      return
    fi
  fi
  printf '%s\n' "$fallback"
}

ACTIVE_SLOT="$(printf '%s' "$(read_boot_cfg "ACTIVE_SLOT" "A")" | tr '[:lower:]' '[:upper:]')"
if [[ "$ACTIVE_SLOT" == "B" ]]; then
  ACTIVE_SLOT="B"
  FALLBACK_SLOT="A"
  ACTIVE_DIR="slot_b"
  FALLBACK_DIR="slot_a"
else
  ACTIVE_SLOT="A"
  FALLBACK_SLOT="B"
  ACTIVE_DIR="slot_a"
  FALLBACK_DIR="slot_b"
fi

echo "-> Kopiowanie nowego kernela do aktywnego Slotu $ACTIVE_SLOT ($MOUNT_PATH/boot/$ACTIVE_DIR/ZonderqOS.elf)..."
cp -f "$KERNEL_BIN" "$MOUNT_PATH/boot/$ACTIVE_DIR/ZonderqOS.elf"
echo "-> Kopiowanie nowego kernela do /boot/ZonderqOS.elf..."
cp -f "$KERNEL_BIN" "$MOUNT_PATH/boot/ZonderqOS.elf"

KERNEL_SHA="$(sha256sum "$KERNEL_BIN" | awk '{print $1}')"
echo "$KERNEL_SHA" > "$MOUNT_PATH/boot/$ACTIVE_DIR/kernel.sha256"

if [[ ! -s "$MOUNT_PATH/boot/$FALLBACK_DIR/ZonderqOS.elf" ]]; then
  echo "-> Inicjalizacja pustego slota zapasowego $FALLBACK_SLOT ($MOUNT_PATH/boot/$FALLBACK_DIR/ZonderqOS.elf)..."
  cp -f "$KERNEL_BIN" "$MOUNT_PATH/boot/$FALLBACK_DIR/ZonderqOS.elf"
  echo "$KERNEL_SHA" > "$MOUNT_PATH/boot/$FALLBACK_DIR/kernel.sha256"
  FALLBACK_STATUS="Healthy"
  FALLBACK_VERSION="1.0.2"
  FALLBACK_BUILD="3120"
else
  echo "-> Zachowano istniejacy slot zapasowy $FALLBACK_SLOT ($MOUNT_PATH/boot/$FALLBACK_DIR/ZonderqOS.elf) dla rollbacku A/B."
  FALLBACK_STATUS="$(read_boot_cfg "SLOT_${FALLBACK_SLOT}_STATUS" "Healthy")"
  FALLBACK_VERSION="$(read_boot_cfg "SLOT_${FALLBACK_SLOT}_VERSION" "1.0.1")"
  FALLBACK_BUILD="$(read_boot_cfg "SLOT_${FALLBACK_SLOT}_BUILD" "3100")"
fi

BOOTGUARD_PREBOOT="$(read_boot_cfg "BOOTGUARD_PREBOOT" "0")"
if [[ "$BOOTGUARD_PREBOOT" != "1" ]]; then
  BOOTGUARD_PREBOOT="0"
fi

# 2. Update /etc/os-release and /boot/boot_state.cfg preserving fallback slot rollback metadata
mkdir -p "$MOUNT_PATH/etc"
cat > "$MOUNT_PATH/etc/os-release" <<'EOF'
NAME="ZOnderqOS"
VERSION="1.0.2"
ID=zonderqos
ID_LIKE=cosmos
VERSION_ID=1.0.2
PRETTY_NAME="ZOnderqOS 1.0.2 (Cosmos 3.0.95)"
BUILD_ID=3120
VARIANT="Workstation"
VARIANT_ID=workstation
HOME_URL="https://github.com/zonderq/ZOnderqOS"
SUPPORT_URL="https://github.com/zonderq/ZOnderqOS/issues"
BUG_REPORT_URL="https://github.com/zonderq/ZOnderqOS/issues"
EOF

cat > "$MOUNT_PATH/etc/update.cfg" <<'EOF'
CHANNEL=stable
UPDATER_VERSION=3.0
INSTALLED_VERSION=1.0.2
INSTALLED_BUILD=3120
LAST_NOTIFIED=
EOF

if [[ "$ACTIVE_SLOT" == "A" ]]; then
  SLOT_A_STATUS="Healthy"
  SLOT_A_VERSION="1.0.2"
  SLOT_A_BUILD="3120"
  SLOT_B_STATUS="$FALLBACK_STATUS"
  SLOT_B_VERSION="$FALLBACK_VERSION"
  SLOT_B_BUILD="$FALLBACK_BUILD"
else
  SLOT_B_STATUS="Healthy"
  SLOT_B_VERSION="1.0.2"
  SLOT_B_BUILD="3120"
  SLOT_A_STATUS="$FALLBACK_STATUS"
  SLOT_A_VERSION="$FALLBACK_VERSION"
  SLOT_A_BUILD="$FALLBACK_BUILD"
fi

cat > "$MOUNT_PATH/boot/boot_state.cfg" <<EOF
# ZonderqOS A/B Bootloader State Configuration
ACTIVE_SLOT=$ACTIVE_SLOT
FALLBACK_SLOT=$FALLBACK_SLOT
PENDING_SLOT=
BOOT_ATTEMPTS=0
BOOTGUARD_PREBOOT=$BOOTGUARD_PREBOOT
BOOTGUARD_FALLBACK=0
MAX_BOOT_ATTEMPTS=3
SLOT_A_STATUS=$SLOT_A_STATUS
SLOT_A_VERSION=$SLOT_A_VERSION
SLOT_A_BUILD=$SLOT_A_BUILD
SLOT_B_STATUS=$SLOT_B_STATUS
SLOT_B_VERSION=$SLOT_B_VERSION
SLOT_B_BUILD=$SLOT_B_BUILD
LAST_MODIFIED=$(date +'%Y-%m-%d %H:%M:%S')
EOF

# 2.5. Write autonomous Limine configuration with fixed Slot A (1) and Slot B (2) entries
if [[ "$ACTIVE_SLOT" == "B" ]]; then
  DEFAULT_ENTRY=2
else
  DEFAULT_ENTRY=1
fi
mkdir -p "$MOUNT_PATH/boot/limine"
cat > "$MOUNT_PATH/boot/limine.conf" <<EOF
# Cosmos Gen3 / Limine v10 A/B dual-slot boot configuration.
timeout: 5
default_entry: $DEFAULT_ENTRY
remember_last_entry: no

/ZonderqOS Slot A
    protocol: limine
    resolution: 1366x768x32
    path: fslabel(ZONDERQDATA):/boot/slot_a/ZonderqOS.elf

/ZonderqOS Slot B
    protocol: limine
    resolution: 1366x768x32
    path: fslabel(ZONDERQDATA):/boot/slot_b/ZonderqOS.elf

/ZonderqOS Recovery ISO
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
cp -rf "$UPDATES_DIR"/. "$MOUNT_PATH/updates/"
cp -rf "$UPDATES_DIR"/. "$MOUNT_PATH/var/updates/incoming/"

# Weryfikacja obecnosci plikow aktualizacji
if [[ ! -f "$MOUNT_PATH/updates/update.manifest" || ! -f "$MOUNT_PATH/updates/ZonderqOS.elf" ]]; then
    echo "[BLAD] Pliki aktualizacji nie zostaly poprawnie skopiowane do /updates!" >&2
    exit 1
fi
DEPLOYED_VERSION="$(awk -F= '$1=="VERSION"{gsub(/^[[:space:]]+|[[:space:]\r]+$/,"",$2); print $2; exit}' "$MOUNT_PATH/updates/update.manifest" || true)"
echo "-> Zweryfikowano: pakiet aktualizacji (wersja ${DEPLOYED_VERSION:-unknown}) jest gotowy w /updates."

echo "-> Synchronizacja buforow USB (sync)..."
sync

echo "-> Bezpieczne odmontowywanie partycji USB..."
udisksctl unmount -b "$DEV" --no-user-interaction || true

echo "=== Pomyslnie zaktualizowano pendrive ZOnderqOS! ==="
echo "Mozesz bezpiecznie odpiac pendrive i uruchomic laptopa."
