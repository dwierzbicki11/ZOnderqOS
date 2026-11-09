#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ISO="$ROOT/output-3095/ZonderqOS-3095-disk.iso"
DEVICE="/dev/sdb"
PART4="${DEVICE}4"
PART2="${DEVICE}2"

echo "=== ZOnderqOS Clean USB Flasher ==="
echo "Target device: $DEVICE"
echo "Source ISO:    $ISO"

# Safety verification: ensure DEVICE is strictly the SanDisk USB
MODEL="$(lsblk -dno MODEL "$DEVICE" || true)"
if [[ "$MODEL" != *"SanDisk"* ]]; then
  echo "[BLAD] Urzadzenie $DEVICE nie jest pendrivem SanDisk (wykryto: $MODEL)!"
  exit 1
fi

echo "-> Unmounting any mounted partitions on $DEVICE..."
sync
umount "${DEVICE}"* 2>/dev/null || true

echo "-> Flashing new hybrid bootloader ISO to beginning of $DEVICE..."
dd if="$ISO" of="$DEVICE" bs=4M status=progress conv=fsync
sync

echo "-> Restoring partition 4 (ZONDERQDATA) entry in MBR table..."
printf 'start=65536, size=240263168, type=c\n' | sfdisk --append --wipe=never --wipe-partitions=never "$DEVICE"
sync

echo "-> Refreshing partition table (partprobe)..."
partprobe "$DEVICE" || true
udevadm settle
sleep 1

# Verify PART4 exists and has ZONDERQDATA label
LABEL4="$(blkid -p -s LABEL -o value "$PART4" || true)"
if [[ "$LABEL4" != "ZONDERQDATA" ]]; then
  echo "[BLAD] Partycja $PART4 nie ma etykiety ZONDERQDATA (otrzymano: $LABEL4)!"
  exit 1
fi
echo "-> Partycja ZONDERQDATA ($PART4) zweryfikowana pomyslnie."

# Mount EFI partition PART2 and seed UEFI Limine configuration
MOUNT_EFI="$(mktemp -d /tmp/zonderq-efi-mount.XXXXXX)"
mount -t vfat "$PART2" "$MOUNT_EFI"
mkdir -p "$MOUNT_EFI/EFI/BOOT" "$MOUNT_EFI/boot/limine" "$MOUNT_EFI/EFI/limine"

cat > "$MOUNT_EFI/EFI/BOOT/limine.conf" <<'EOF'
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

/ZOnderqOS (Kopia zapasowa / Ratunkowa)
    protocol: limine
    resolution: 1366x768x32
    path: fslabel(ZONDERQDATA):/boot/ZonderqOS.elf
EOF

cp -f "$MOUNT_EFI/EFI/BOOT/limine.conf" "$MOUNT_EFI/limine.conf"
cp -f "$MOUNT_EFI/EFI/BOOT/limine.conf" "$MOUNT_EFI/boot/limine.conf"
cp -f "$MOUNT_EFI/EFI/BOOT/limine.conf" "$MOUNT_EFI/boot/limine/limine.conf"
cp -f "$MOUNT_EFI/EFI/BOOT/limine.conf" "$MOUNT_EFI/EFI/limine/limine.conf"

sync
umount "$MOUNT_EFI"
rmdir "$MOUNT_EFI"
echo "-> Konfiguracja rozruchowa UEFI Limine na partycji EFI zaktualizowana pomyslnie."

# Mount data partition PART4 and ensure latest kernel and configs are in place
MOUNT_DATA="$(mktemp -d /tmp/zonderq-data-mount.XXXXXX)"
mount -t vfat "$PART4" "$MOUNT_DATA"

KERNEL_BIN="$ROOT/output-3095/updates-package/ZonderqOS.elf"
mkdir -p "$MOUNT_DATA/boot/slot_a" "$MOUNT_DATA/boot/slot_b" "$MOUNT_DATA/boot/limine" "$MOUNT_DATA/etc"

echo "-> Instalowanie nowego kernela 1.0.2 do slotow i glownego pliku boot..."
cp -f "$KERNEL_BIN" "$MOUNT_DATA/boot/slot_a/ZonderqOS.elf"
cp -f "$KERNEL_BIN" "$MOUNT_DATA/boot/slot_b/ZonderqOS.elf"
cp -f "$KERNEL_BIN" "$MOUNT_DATA/boot/ZonderqOS.elf"

KERNEL_SHA="$(sha256sum "$KERNEL_BIN" | awk '{print $1}')"
echo "$KERNEL_SHA" > "$MOUNT_DATA/boot/slot_a/kernel.sha256"
echo "$KERNEL_SHA" > "$MOUNT_DATA/boot/slot_b/kernel.sha256"

cat > "$MOUNT_DATA/etc/os-release" <<'EOF'
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

cat > "$MOUNT_DATA/etc/update.cfg" <<'EOF'
CHANNEL=stable
UPDATER_VERSION=3.0
INSTALLED_VERSION=1.0.2
INSTALLED_BUILD=3120
LAST_NOTIFIED=
EOF

cat > "$MOUNT_DATA/boot/boot_state.cfg" <<'EOF'
ACTIVE_SLOT=A
FALLBACK_SLOT=B
PENDING_SLOT=
BOOT_ATTEMPTS=0
MAX_BOOT_ATTEMPTS=3
SLOT_A_STATUS=Healthy
SLOT_B_STATUS=Healthy
SLOT_A_VERSION=1.0.2
SLOT_A_BUILD=3120
SLOT_B_VERSION=1.0.2
SLOT_B_BUILD=3120
EOF

cat > "$MOUNT_DATA/boot/limine.conf" <<'EOF'
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

/ZOnderqOS (Kopia zapasowa / Ratunkowa)
    protocol: limine
    resolution: 1366x768x32
    path: fslabel(ZONDERQDATA):/boot/ZonderqOS.elf
EOF

cp -f "$MOUNT_DATA/boot/limine.conf" "$MOUNT_DATA/limine.conf"
cp -f "$MOUNT_DATA/boot/limine.conf" "$MOUNT_DATA/boot/limine/limine.conf"

mkdir -p "$MOUNT_DATA/updates" "$MOUNT_DATA/var/updates/incoming"
cp -rf "$ROOT/output-3095/updates-package"/* "$MOUNT_DATA/updates/"
cp -rf "$ROOT/output-3095/updates-package"/* "$MOUNT_DATA/var/updates/incoming/"

sync
umount "$MOUNT_DATA"
rmdir "$MOUNT_DATA"

echo "=== Wdrozenie zakonczone pelnym sukcesem! ==="
echo "Pendrive zostal w pelni i na czysto przygotowany."
