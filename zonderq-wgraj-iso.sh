#!/usr/bin/env bash
# ZonderqOS Gen3: flash a prebuilt hybrid ISO, preserve existing FAT32 ZONDERQDATA.
set -Eeuo pipefail

DEVICE="/dev/sdb"
ISO="$HOME/Pulpit/ZOnderqOS/output-x64/ZonderqOS.iso"
if [[ ! -f "$ISO" && -f "$HOME/Pulpit/ZOnderqOS/output-3095/ZonderqOS-3095-disk.iso" ]]; then
    ISO="$HOME/Pulpit/ZOnderqOS/output-3095/ZonderqOS-3095-disk.iso"
fi

for arg in "$@"; do
    if [[ "$arg" == /dev/* ]]; then
        DEVICE="$arg"
    elif [[ -f "$arg" || "$arg" == *.iso ]]; then
        ISO="$arg"
    fi
done

DATA_START=65536  # Existing ZONDERQDATA starts at LBA 65536 (32 MiB)

fail() { printf '\n[STOP] %s\n' "$*" >&2; exit 1; }

for cmd in lsblk sfdisk blockdev dd sync stat python3 blkid partprobe udevadm fdisk findmnt grep tail xargs; do
    command -v "$cmd" >/dev/null 2>&1 || fail "Missing required program: $cmd"
done
[[ -b "$DEVICE" ]] || fail "Block device not found: $DEVICE"
[[ "$(lsblk -dn -o TYPE "$DEVICE" | xargs)" == disk ]] || fail "$DEVICE is not a disk"
[[ "$(lsblk -dn -o TRAN "$DEVICE" | xargs)" == usb ]] || fail "$DEVICE is not a USB device"
[[ -f "$ISO" && -s "$ISO" ]] || fail "Prebuilt ISO not found: $ISO"
ISO_BYTES=$(stat -c%s "$ISO")
(( ISO_BYTES > 512 && ISO_BYTES < DATA_START * 512 )) || fail "ISO is $ISO_BYTES bytes: does not fit before ZONDERQDATA (32 MiB)"

# Check the actual hybrid ISO MBR before any write. The 4th entry must be free.
sfdisk --json "$ISO" | python3 -c '
import json,sys
j=json.load(sys.stdin)["partitiontable"]
p=j.get("partitions",[])
if j.get("label") != "dos" or len(p) != 3:
    sys.exit("[STOP] ISO must have an MBR with three partitions (as in current ZonderqOS)")
for e in p:
    if e["start"]+e["size"] > 65536:
        sys.exit("[STOP] ISO partition extends beyond sector 65535")
' || fail "Invalid ISO partition layout"

# Ensure we are preserving the exact existing data partition, not another disk.
[[ -b "${DEVICE}4" ]] || fail "Missing ${DEVICE}4"
[[ "$(lsblk -dn -o START "${DEVICE}4" | xargs)" == "$DATA_START" ]] || fail "Start sector of ${DEVICE}4 is not $DATA_START"
[[ "$(lsblk -dn -o LABEL "${DEVICE}4" | xargs)" == ZONDERQDATA ]] || fail "ZONDERQDATA label not found on ${DEVICE}4"
[[ "$(lsblk -dn -o FSTYPE "${DEVICE}4" | xargs)" == vfat ]] || fail "${DEVICE}4 is not FAT"

printf '\nUSB DRIVE:\n'
lsblk -o NAME,SIZE,MODEL,TRAN,FSTYPE,LABEL,MOUNTPOINTS "$DEVICE"
printf '\nISO: %s (%s bytes)\n' "$ISO" "$ISO_BYTES"
printf 'Data partition: %s4, FAT32, start LBA %s. It will not be formatted.\n' "$DEVICE" "$DATA_START"
printf '\nWARNING: boot partitions at the beginning of %s.\n' "$DEVICE"
printf 'Back up important ZONDERQDATA files before updating.\n'
read -r -p "Type WGRYWAM to continue: " CONFIRM
[[ "$CONFIRM" == WGRYWAM ]] || fail "Cancelled"

# Acquire root only after validations, and revalidate on-disk details as root.
sudo -v || fail "sudo authorization failed"
[[ "$(sudo lsblk -dn -o TRAN "$DEVICE" | xargs)" == usb ]] || fail "Device is not USB"
[[ "$(sudo lsblk -dn -o START "${DEVICE}4" | xargs)" == "$DATA_START" ]] || fail "Data partition start sector has changed"
[[ "$(sudo blkid -p -s LABEL -o value "${DEVICE}4" || true)" == ZONDERQDATA ]] || fail "Data partition label has changed"

# Unmount all partitions of the target device, fail on busy mount.
while IFS= read -r PART; do
    if findmnt -rn --source "$PART" >/dev/null 2>&1; then
        sudo umount "$PART" || fail "Cannot unmount $PART"
    fi
done < <(lsblk -lnpo NAME "$DEVICE" | tail -n +2)
if lsblk -nr -o MOUNTPOINTS "$DEVICE" | grep -q '[^[:space:]]'; then
    fail "USB drive partitions are still mounted"
fi

BACKUP_DIR="$HOME/zonderq-usb-backups/$(date +%Y%m%d-%H%M%S)"
mkdir -p "$BACKUP_DIR"
sudo sfdisk --dump "$DEVICE" > "$BACKUP_DIR/partitions-before.sfdisk"
# Back up the boot sector as an additional recovery aid (not a backup of user files).
sudo dd if="$DEVICE" of="$BACKUP_DIR/first-1MiB.bin" bs=1M count=1 status=none
sudo chown "$(id -u):$(id -g)" "$BACKUP_DIR/first-1MiB.bin"
printf 'Saved partition table and first MiB backup: %s\n' "$BACKUP_DIR"

sudo dd if="$ISO" of="$DEVICE" bs=4M status=progress conv=fsync
sync

# The flash overwrote MBR entry 4 but NOT the data sectors (ISO ends before 32 MiB).
# The 3-entry ISO MBR was pre-validated above.
TOTAL_SECTORS=$(sudo blockdev --getsz "$DEVICE")
(( TOTAL_SECTORS > DATA_START )) || fail "Invalid disk size after flashing ISO"
printf 'start=%s, size=%s, type=c\n' "$DATA_START" "$((TOTAL_SECTORS-DATA_START))" \
   | sudo sfdisk --append --wipe=never --wipe-partitions=never "$DEVICE" \
   || fail "Failed to restore sdb4 entry. DO NOT FORMAT! Partition table backup: $BACKUP_DIR"

sudo partprobe "$DEVICE" || fail "partprobe error: unplug and reconnect USB before verification"
sudo udevadm settle
sync
[[ "$(sudo blkid -p -s LABEL -o value "${DEVICE}4" || true)" == ZONDERQDATA ]] || fail "ZONDERQDATA verification failed. DO NOT FORMAT"
[[ "$(sudo lsblk -dn -o START "${DEVICE}4" | xargs)" == "$DATA_START" ]] || fail "Incorrect data partition start sector"

# Synchronize the active boot slot on ZONDERQDATA with the newly flashed kernel ELF
# so Limine boots the exact kernel matching the flashed ISO, while preserving any
# existing non-empty fallback slot for A/B rollback.
KERNEL_ELF="$(dirname "$ISO")/ZonderqOS.elf"
if [[ ! -f "$KERNEL_ELF" && -f "$HOME/Pulpit/ZOnderqOS/output-x64/ZonderqOS.elf" ]]; then
    KERNEL_ELF="$HOME/Pulpit/ZOnderqOS/output-x64/ZonderqOS.elf"
fi
if [[ -f "$KERNEL_ELF" && -s "$KERNEL_ELF" ]]; then
    MOUNT_DATA="$(mktemp -d /tmp/zonderq-data-sync.XXXXXX)"
    UPDATED_DESC=""
    if sudo mount -t vfat "${DEVICE}4" "$MOUNT_DATA"; then
        sudo mkdir -p "$MOUNT_DATA/boot/slot_a" "$MOUNT_DATA/boot/slot_b"
        ACTIVE_SLOT="A"
        if sudo test -f "$MOUNT_DATA/boot/boot_state.cfg"; then
            CFG_SLOT="$(sudo awk -F= '/^ACTIVE_SLOT=/{gsub(/[[:space:]\r]/,"",$2); print toupper($2); exit}' "$MOUNT_DATA/boot/boot_state.cfg")"
            if [[ "$CFG_SLOT" == "B" ]]; then
                ACTIVE_SLOT="B"
            fi
        fi
        if [[ "$ACTIVE_SLOT" == "B" ]]; then
            ACTIVE_SLOT_ELF="/boot/slot_b/ZonderqOS.elf"
            ACTIVE_SLOT_SHA="/boot/slot_b/kernel.sha256"
            FALLBACK_SLOT_ELF="/boot/slot_a/ZonderqOS.elf"
            FALLBACK_SLOT_SHA="/boot/slot_a/kernel.sha256"
        else
            ACTIVE_SLOT_ELF="/boot/slot_a/ZonderqOS.elf"
            ACTIVE_SLOT_SHA="/boot/slot_a/kernel.sha256"
            FALLBACK_SLOT_ELF="/boot/slot_b/ZonderqOS.elf"
            FALLBACK_SLOT_SHA="/boot/slot_b/kernel.sha256"
        fi

        sudo cp -f "$KERNEL_ELF" "$MOUNT_DATA$ACTIVE_SLOT_ELF"
        sudo cp -f "$KERNEL_ELF" "$MOUNT_DATA/boot/ZonderqOS.elf"
        KERNEL_SHA=""
        if command -v sha256sum >/dev/null 2>&1; then
            KERNEL_SHA="$(sha256sum "$KERNEL_ELF" | awk '{print $1}')"
            printf '%s\n' "$KERNEL_SHA" | sudo tee "$MOUNT_DATA$ACTIVE_SLOT_SHA" >/dev/null
        fi

        if ! sudo test -s "$MOUNT_DATA$FALLBACK_SLOT_ELF"; then
            sudo cp -f "$KERNEL_ELF" "$MOUNT_DATA$FALLBACK_SLOT_ELF"
            if [[ -n "$KERNEL_SHA" ]]; then
                printf '%s\n' "$KERNEL_SHA" | sudo tee "$MOUNT_DATA$FALLBACK_SLOT_SHA" >/dev/null
            fi
            UPDATED_DESC="$ACTIVE_SLOT_ELF and initialized empty fallback slot $FALLBACK_SLOT_ELF"
        else
            UPDATED_DESC="$ACTIVE_SLOT_ELF (preserved existing fallback slot $FALLBACK_SLOT_ELF for A/B rollback)"
        fi
        sync
        sudo umount "$MOUNT_DATA" || fail "Cannot unmount $MOUNT_DATA"
    fi
    rmdir "$MOUNT_DATA" 2>/dev/null || true
    if [[ -n "$UPDATED_DESC" ]]; then
        printf '[OK] Synchronized %s z: %s\n' "$UPDATED_DESC" "$KERNEL_ELF"
    fi
fi

printf '\n[OK] Flashed existing ISO image; ZONDERQDATA preserved without formatting.\n'
lsblk -o NAME,START,SIZE,FSTYPE,LABEL,PARTTYPE "$DEVICE"
printf '\nYou may safely disconnect the USB drive: sudo udisksctl power-off -b %s\n' "$DEVICE"
