#!/usr/bin/env bash
# ZonderqOS Gen3: flash a prebuilt hybrid ISO, preserve existing FAT32 ZONDERQDATA.
set -Eeuo pipefail

DEVICE="/dev/sdb"
ISO="${1:-$HOME/Pulpit/ZOnderqOS/output-3095/ZonderqOS-3095-disk.iso}"
DATA_START=65536  # Existing ZONDERQDATA starts at LBA 65536 (32 MiB)

fail() { printf '\n[STOP] %s\n' "$*" >&2; exit 1; }

for cmd in lsblk sfdisk blockdev dd sync stat python3 blkid partprobe udevadm fdisk findmnt grep tail xargs; do
    command -v "$cmd" >/dev/null 2>&1 || fail "Brakuje programu: $cmd"
done
[[ -b "$DEVICE" ]] || fail "Nie ma dysku $DEVICE"
[[ "$(lsblk -dn -o TYPE "$DEVICE" | xargs)" == disk ]] || fail "$DEVICE nie jest dyskiem"
[[ "$(lsblk -dn -o TRAN "$DEVICE" | xargs)" == usb ]] || fail "$DEVICE nie jest urzadzeniem USB"
[[ -f "$ISO" && -s "$ISO" ]] || fail "Nie ma gotowego ISO: $ISO"
ISO_BYTES=$(stat -c%s "$ISO")
(( ISO_BYTES > 512 && ISO_BYTES < DATA_START * 512 )) || fail "ISO ma $ISO_BYTES bajtow: nie miesci sie przed ZONDERQDATA (32 MiB)"

# Check the actual hybrid ISO MBR before any write. The 4th entry must be free.
sfdisk --json "$ISO" | python3 -c '
import json,sys
j=json.load(sys.stdin)["partitiontable"]
p=j.get("partitions",[])
if j.get("label") != "dos" or len(p) != 3:
    sys.exit("[STOP] ISO musi miec MBR z trzema partycjami (jak obecny ZonderqOS)")
for e in p:
    if e["start"]+e["size"] > 65536:
        sys.exit("[STOP] Partycja ISO przekracza sektor 65535")
' || fail "Niepoprawny uklad ISO"

# Ensure we are preserving the exact existing data partition, not another disk.
[[ -b "${DEVICE}4" ]] || fail "Brakuje ${DEVICE}4"
[[ "$(lsblk -dn -o START "${DEVICE}4" | xargs)" == "$DATA_START" ]] || fail "Poczatek ${DEVICE}4 nie wynosi $DATA_START"
[[ "$(lsblk -dn -o LABEL "${DEVICE}4" | xargs)" == ZONDERQDATA ]] || fail "Nie ma etykiety ZONDERQDATA na ${DEVICE}4"
[[ "$(lsblk -dn -o FSTYPE "${DEVICE}4" | xargs)" == vfat ]] || fail "${DEVICE}4 nie jest FAT"

printf '\nPENDRIVE:\n'
lsblk -o NAME,SIZE,MODEL,TRAN,FSTYPE,LABEL,MOUNTPOINTS "$DEVICE"
printf '\nISO: %s (%s bajtow)\n' "$ISO" "$ISO_BYTES"
printf 'Partycja danych: %s4, FAT32, start LBA %s. Nie bedzie formatowana.\n' "$DEVICE" "$DATA_START"
printf '\nUWAGA: nadpisane zostana partycje rozruchowe z poczatku %s.\n' "$DEVICE"
printf 'Zrob kopie waznych plikow ZONDERQDATA przed aktualizacja.\n'
read -r -p "Wpisz WGRYWAM aby kontynuowac: " CONFIRM
[[ "$CONFIRM" == WGRYWAM ]] || fail "Anulowano"

# Acquire root only after validations, and revalidate on-disk details as root.
sudo -v || fail "Brak sudo"
[[ "$(sudo lsblk -dn -o TRAN "$DEVICE" | xargs)" == usb ]] || fail "Dysk nie jest USB"
[[ "$(sudo lsblk -dn -o START "${DEVICE}4" | xargs)" == "$DATA_START" ]] || fail "Zmienil sie poczatek partycji danych"
[[ "$(sudo blkid -p -s LABEL -o value "${DEVICE}4" || true)" == ZONDERQDATA ]] || fail "Zmienila sie etykieta partycji danych"

# Unmount all partitions of the target device, fail on busy mount.
while IFS= read -r PART; do
    if findmnt -rn --source "$PART" >/dev/null 2>&1; then
        sudo umount "$PART" || fail "Nie mozna odmontowac $PART"
    fi
done < <(lsblk -lnpo NAME "$DEVICE" | tail -n +2)
if lsblk -nr -o MOUNTPOINTS "$DEVICE" | grep -q '[^[:space:]]'; then
    fail "Nadal sa zamontowane partycje pendrive'a"
fi

BACKUP_DIR="$HOME/zonderq-usb-backups/$(date +%Y%m%d-%H%M%S)"
mkdir -p "$BACKUP_DIR"
sudo sfdisk --dump "$DEVICE" > "$BACKUP_DIR/partitions-before.sfdisk"
# Back up the boot sector as an additional recovery aid (not a backup of user files).
sudo dd if="$DEVICE" of="$BACKUP_DIR/first-1MiB.bin" bs=1M count=1 status=none
sudo chown "$(id -u):$(id -g)" "$BACKUP_DIR/first-1MiB.bin"
printf 'Zapisano kopie tablicy partycji i pierwszego MiB: %s\n' "$BACKUP_DIR"

sudo dd if="$ISO" of="$DEVICE" bs=4M status=progress conv=fsync
sync

# The flash overwrote MBR entry 4 but NOT the data sectors (ISO ends before 32 MiB).
# The 3-entry ISO MBR was pre-validated above.
TOTAL_SECTORS=$(sudo blockdev --getsz "$DEVICE")
(( TOTAL_SECTORS > DATA_START )) || fail "Niepoprawny rozmiar dysku po wgraniu ISO"
printf 'start=%s, size=%s, type=c\n' "$DATA_START" "$((TOTAL_SECTORS-DATA_START))" \
   | sudo sfdisk --append --wipe=never --wipe-partitions=never "$DEVICE" \
   || fail "Nie udalo sie przywrocic wpisu sdb4. NIE FORMATUJ! Kopia tablicy: $BACKUP_DIR"

sudo partprobe "$DEVICE" || fail "Blad partprobe: odłącz i podłącz USB przed weryfikacja"
sudo udevadm settle
sync
[[ "$(sudo blkid -p -s LABEL -o value "${DEVICE}4" || true)" == ZONDERQDATA ]] || fail "Weryfikacja ZONDERQDATA nieudana. NIE FORMATUJ"
[[ "$(sudo lsblk -dn -o START "${DEVICE}4" | xargs)" == "$DATA_START" ]] || fail "Zly sektor startowy partycji danych"
printf '\n[OK] Wgrano istniejacy obraz ISO, ZONDERQDATA zachowana bez formatowania.\n'
lsblk -o NAME,START,SIZE,FSTYPE,LABEL,PARTTYPE "$DEVICE"
printf '\nMozesz bezpiecznie odłączyć USB: sudo udisksctl power-off -b %s\n' "$DEVICE"
