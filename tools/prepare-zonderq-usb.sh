#!/usr/bin/env bash
# ZonderqOS Gen 3 — USB Drive Preparation Script
# Prepares a dedicated SanDisk USB drive with A/B dual-slot layout, Limine bootloader, and persistent ZONDERQDATA.
#
# BEZPIECZENSTWO:
# Ten skrypt wymaga podania sciezki do urzadzenia oraz wpisania potwierdzenia "PRZYGOTUJ".
# Nie wykonuje operacji na dyskach wewnetrznych NVMe/SATA ani partycjach Kali Linux.
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ISO="$ROOT/output-3095/ZonderqOS-3095-disk.iso"
DEVICE="${1:-}"

fail() { printf '\n[STOP] %s\n' "$*" >&2; exit 1; }

[[ -n "$DEVICE" ]] || fail "Podaj sciezke do urzadzenia USB, np.: sudo ./tools/prepare-zonderq-usb.sh /dev/sdb"
[[ -b "$DEVICE" ]] || fail "Urzadzenie $DEVICE nie istnieje lub nie jest urzadzeniem blokowym."

for cmd in lsblk blockdev sfdisk mkfs.vfat blkid sync udevadm partprobe dd stat sha256sum findmnt umount date awk; do
    command -v "$cmd" >/dev/null 2>&1 || fail "Brakuje wymaganego narzedzia: $cmd"
done

# Validate that device is actually a removable USB device
TRAN="$(lsblk -dn -o TRAN "$DEVICE" | xargs || true)"
[[ "$TRAN" == "usb" ]] || fail "Urzadzenie $DEVICE NIE JEST urzadzeniem USB (wykryto magistrale: '$TRAN'). Odmowa operacji ze wzgledow bezpieczenstwa!"

DEV_TYPE="$(lsblk -dn -o TYPE "$DEVICE" | xargs || true)"
[[ "$DEV_TYPE" == "disk" ]] || fail "$DEVICE nie jest calym dyskiem (jest: $DEV_TYPE)."

DATA_START=65536 # LBA 65536 = 32 MiB
MAX_32MIB=$((DATA_START * 512))

# Verify ISO file exists and fits strictly before ZONDERQDATA (<= 32 MiB)
[[ -f "$ISO" && -s "$ISO" ]] || fail "Brak skompilowanego ISO: $ISO. Wykonaj najpierw: ./run.sh --build"
ISO_BYTES="$(stat -c%s "$ISO")"
(( ISO_BYTES > 512 && ISO_BYTES <= MAX_32MIB )) || fail "Obraz ISO ma rozmiar $ISO_BYTES bajtow (limit <= $MAX_32MIB B / 32 MiB). Przerwano, aby nie uszkodzic partycji ZONDERQDATA."

KERNEL_SRC="$ROOT/obj/Debug/net10.0/linux-x64/cosmos/iso_root/boot/ZonderqOS.elf"
if [[ ! -f "$KERNEL_SRC" && -f "$ROOT/output-x64/ZonderqOS.elf" ]]; then
    KERNEL_SRC="$ROOT/output-x64/ZonderqOS.elf"
elif [[ ! -f "$KERNEL_SRC" && -f "$ROOT/output-3095/updates-package/ZonderqOS.elf" ]]; then
    KERNEL_SRC="$ROOT/output-3095/updates-package/ZonderqOS.elf"
fi
[[ -f "$KERNEL_SRC" ]] || fail "Brak pliku kernela do zainicjalizowania Slotu A ($KERNEL_SRC)."
KERNEL_BYTES="$(stat -c%s "$KERNEL_SRC")"
(( KERNEL_BYTES > 0 && KERNEL_BYTES <= MAX_32MIB )) || fail "Obraz kernela $KERNEL_SRC ma niedozwolony rozmiar ($KERNEL_BYTES B; limit <= $MAX_32MIB B / 32 MiB)."

# Verify no foreign OS partitions exist on target device (Kali, Windows, Linux, BitLocker, ESP)
while IFS= read -r part; do
    [[ -n "$part" ]] || continue
    fstype="$(lsblk -no FSTYPE "$part" 2>/dev/null || true)"
    label="$(lsblk -no LABEL "$part" 2>/dev/null || true)"
    parttype="$(lsblk -no PARTTYPE "$part" 2>/dev/null || true)"
    if [[ "$fstype" == "ext4" || "$fstype" == "ntfs" || "$fstype" == "btrfs" || "$label" =~ (KALI|WINDOWS|EFI|RECOVERY) || "$parttype" == "c12a7328-f81f-11d2-ba4b-00a0c93ec93b" ]]; then
        fail "Wykryto partycje obcego systemu ($part: typ=$fstype, etykieta=$label, parttype=$parttype). Operacja zablokowana ze wzgledow bezpieczenstwa!"
    fi
done < <(lsblk -lnpo NAME "$DEVICE" | tail -n +2)

printf '\n============================================================\n'
printf '      PRZYGOTOWANIE PENDRIVE SANDISK DLA ZONDERQOS GEN 3    \n'
printf '============================================================\n'
lsblk -o NAME,SIZE,MODEL,TRAN,FSTYPE,LABEL,MOUNTPOINTS "$DEVICE"
printf '\nWybrany nosnik docelowy: %s\n' "$DEVICE"
printf 'Obraz ISO kernela:       %s\n' "$ISO"
printf '\nUKLAD ROZRUCHOWY A/B:\n'
printf '  * Sektory 0 - 65535:    Bootloader Limine v10 + hybrydowe ISO\n'
printf '  * Sektor 65536+:        Partycja ZONDERQDATA (FAT32)\n'
printf '    - /boot/slot_a/       Slot A (aktywny kernel startowy)\n'
printf '    - /boot/slot_b/       Slot B (rezerwa dla bezpiecznych aktualizacji USB)\n'
printf '    - /boot/boot_state.cfg Konfiguracja przelaczania i watchdog A/B\n'
printf '    - /updates/           Katalog pakietow instalacyjnych\n'
printf '\nUWAGA: Wszystkie dane rozruchowe na poczatku %s zostana przygotowane.\n' "$DEVICE"
read -r -p "Wpisz PRZYGOTUJ aby potwierdzic i kontynuowac: " CONFIRM
[[ "$CONFIRM" == "PRZYGOTUJ" ]] || fail "Anulowano przez uzytkownika."

# Unmount existing mounted partitions
while IFS= read -r PART; do
    [[ -n "$PART" ]] || continue
    if findmnt -rn --source "$PART" >/dev/null 2>&1; then
        printf 'Odmontowywanie %s...\n' "$PART"
        umount "$PART" || fail "Nie mozna odmontowac $PART"
    fi
done < <(lsblk -lnpo NAME "$DEVICE" | tail -n +2)

# Write hybrid ISO to the start of USB
printf '\nWgrywanie hybrydowego obrazu rozruchowego Limine na %s...\n' "$DEVICE"
dd if="$ISO" of="$DEVICE" bs=4M status=progress conv=fsync
sync

# Append partition 4 ZONDERQDATA
TOTAL_SECTORS="$(blockdev --getsz "$DEVICE")"
(( TOTAL_SECTORS > DATA_START )) || fail "Zbyt maly rozmiar nosnika USB"

printf 'start=%s, size=%s, type=c\n' "$DATA_START" "$((TOTAL_SECTORS - DATA_START))" \
   | sfdisk --append --wipe=never --wipe-partitions=never "$DEVICE" \
   || fail "Nie udalo sie dopisac partycji ZONDERQDATA (start=$DATA_START) do $DEVICE"

partprobe "$DEVICE" || true
udevadm settle
sync

PART4="${DEVICE}4"
[[ -b "$PART4" ]] || PART4="${DEVICE}p4"
[[ -b "$PART4" ]] || fail "Nie udalo sie zidentyfikowac partycji danych: $PART4"
PART4_START="$(lsblk -dn -o START "$PART4" | xargs || true)"
[[ "$PART4_START" == "$DATA_START" ]] || fail "Nieoczekiwany sektor startowy $PART4 (otrzymano: '$PART4_START', oczekiwano: $DATA_START)"

# Format ZONDERQDATA as FAT32 if not already formatted with that label
CURRENT_LABEL="$(blkid -p -s LABEL -o value "$PART4" || true)"
if [[ "$CURRENT_LABEL" != "ZONDERQDATA" ]]; then
    printf 'Formatowanie %s jako FAT32 z etykieta ZONDERQDATA...\n' "$PART4"
    mkfs.vfat -F 32 -n "ZONDERQDATA" "$PART4"
    sync
else
    printf 'Wykryto istniejaca partycje ZONDERQDATA na %s. Formatowanie pominiete, zachowywanie istniejacych danych uzytkownika.\n' "$PART4"
fi

# Mount ZONDERQDATA to seed Slot A and Limine A/B config
MOUNT_DIR="$(mktemp -d /tmp/zonderq-usb-mount.XXXXXX)"
cleanup() {
    sync
    umount "$MOUNT_DIR" 2>/dev/null || true
    rmdir "$MOUNT_DIR" 2>/dev/null || true
}
trap cleanup EXIT

printf 'Inicjalizacja struktury katalogow i slotu A na partycji ZONDERQDATA...\n'
mount -t vfat "$PART4" "$MOUNT_DIR"

mkdir -p "$MOUNT_DIR/boot/slot_a"
mkdir -p "$MOUNT_DIR/boot/slot_b"
mkdir -p "$MOUNT_DIR/boot/limine"
mkdir -p "$MOUNT_DIR/updates"
mkdir -p "$MOUNT_DIR/var/updates/installed"
mkdir -p "$MOUNT_DIR/var/updates/backup"
mkdir -p "$MOUNT_DIR/var/log"

# Clean up legacy fake update configurations and mock files
rm -f "$MOUNT_DIR/etc/update.cfg"
rm -f "$MOUNT_DIR/etc/os-release"
rm -f "$MOUNT_DIR/var/log/update.log"
mkdir -p "$MOUNT_DIR/etc"

cat > "$MOUNT_DIR/etc/update.cfg" <<EOF
CHANNEL=stable
LAST_CHECK=$(date +'%Y-%m-%d %H:%M')
UPDATER_VERSION=3.0
INSTALLED_VERSION=1.0.0-alpha
INSTALLED_BUILD=3100
LAST_NOTIFIED=
EOF

cat > "$MOUNT_DIR/etc/os-release" <<EOF
NAME="ZonderqOS"
ID=zonderqos
VERSION_ID="1.0.2"
BUILD_ID="3120"
PRETTY_NAME="ZonderqOS 1.0.2 (Cosmos 3.0.95 x64 Native Core)"
UPDATE_DATE="$(date +'%Y-%m-%d %H:%M:%S')"
EOF

# Copy running kernel into slot A
if [[ -f "$KERNEL_SRC" ]]; then
    cp -f "$KERNEL_SRC" "$MOUNT_DIR/boot/slot_a/ZonderqOS.elf"
    cp -f "$KERNEL_SRC" "$MOUNT_DIR/boot/ZonderqOS.elf"
    sha256sum "$MOUNT_DIR/boot/slot_a/ZonderqOS.elf" | awk '{print $1}' > "$MOUNT_DIR/boot/slot_a/kernel.sha256"
else
    fail "Brak pliku kernela do zainicjalizowania Slotu A ($KERNEL_SRC)."
fi

if [[ -s "$MOUNT_DIR/boot/slot_b/ZonderqOS.elf" ]]; then
    SLOT_B_STATUS="Healthy"
    SLOT_B_VERSION="1.0.2"
    SLOT_B_BUILD="3120"
else
    SLOT_B_STATUS="Empty"
    SLOT_B_VERSION=""
    SLOT_B_BUILD="0"
fi

# Write boot_state.cfg (aligned with BootSlotManager.cs and bootguard_efi.c)
cat > "$MOUNT_DIR/boot/boot_state.cfg" <<EOF
# ZonderqOS A/B Bootloader State Configuration
ACTIVE_SLOT=A
FALLBACK_SLOT=B
PENDING_SLOT=
BOOT_ATTEMPTS=0
BOOTGUARD_PREBOOT=0
BOOTGUARD_FALLBACK=0
MAX_BOOT_ATTEMPTS=3
SLOT_A_STATUS=Healthy
SLOT_A_VERSION=1.0.2
SLOT_A_BUILD=3120
SLOT_B_STATUS=$SLOT_B_STATUS
SLOT_B_VERSION=$SLOT_B_VERSION
SLOT_B_BUILD=$SLOT_B_BUILD
LAST_MODIFIED=$(date +'%Y-%m-%d %H:%M:%S')
EOF

# Write limine.conf on ZONDERQDATA (Slot A = entry 1, Slot B = entry 2, Recovery = entry 3)
cat > "$MOUNT_DIR/boot/limine.conf" <<EOF
# Cosmos Gen3 / Limine v10 A/B dual-slot boot configuration.
timeout: 5
default_entry: 1
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

cp -f "$MOUNT_DIR/boot/limine.conf" "$MOUNT_DIR/limine.conf"
cp -f "$MOUNT_DIR/boot/limine.conf" "$MOUNT_DIR/boot/limine/limine.conf"

sync
umount "$MOUNT_DIR"
trap - EXIT
rmdir "$MOUNT_DIR"

printf '\n[SUKCES] Pendrive SanDisk zostal pomyslnie przygotowany dla ZonderqOS Gen 3!\n'
printf 'Uklad rozruchowy A/B jest w pelni aktywny.\n'
printf 'Mozesz teraz bezpiecznie odpiac pendrive i przetestowac boot na fizycznym laptopie.\n\n'
