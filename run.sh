#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT_DIR"

MIN_USB_BYTES=28000000000
MAX_USB_BYTES=35000000000
USB_LABEL="ZONDERQ"

print_header() {
    clear
    echo "========================================"
    echo "        ZonderqOS build launcher"
    echo "========================================"
    echo
}

sync_repo() {
    echo "[GIT] Aktualizacja main..."
    git pull --ff-only origin main
    echo
}

run_x64() {
    echo "[X64] Budowanie ZonderqOS..."
    cosmos build -a x64

    local iso="$ROOT_DIR/output-x64/ZonderqOS.iso"
    if [[ ! -f "$iso" ]]; then
        echo "[BLAD] Brak obrazu: $iso" >&2
        exit 1
    fi

    echo
    echo "[X64] Uruchamianie QEMU..."

    local qemu_data="$HOME/.cosmos/tools/share/qemu"
    local -a qemu_args=(
        -L "$qemu_data"
        -M q35
        -cpu max
        -m 2G
        -drive "file=$iso,if=none,id=cosmoscd,format=raw,readonly=on"
        -device ide-cd,drive=cosmoscd,bootindex=0
        -boot d
        -display gtk,zoom-to-fit=on
        -full-screen
        -serial stdio
    )

    if [[ -f "$ROOT_DIR/zonder_disk.img" || -f "$ROOT_DIR/disk_sata_1G.img" ]]; then
        qemu_args+=( -device ich9-ahci,id=ahci0 )
    fi

    if [[ -f "$ROOT_DIR/zonder_disk.img" ]]; then
        qemu_args+=(
            -drive "file=$ROOT_DIR/zonder_disk.img,if=none,id=ahcidisk0,format=raw"
            -device ide-hd,drive=ahcidisk0,bus=ahci0.0
        )
    fi

    if [[ -f "$ROOT_DIR/disk_sata_1G.img" ]]; then
        qemu_args+=(
            -drive "file=$ROOT_DIR/disk_sata_1G.img,if=none,id=ahcidisk1,format=raw"
            -device ide-hd,drive=ahcidisk1,bus=ahci0.1
        )
    fi

    if [[ -f "$ROOT_DIR/disk_nvme_2G.img" ]]; then
        qemu_args+=(
            -drive "file=$ROOT_DIR/disk_nvme_2G.img,if=none,id=nvmedisk0,format=raw"
            -device nvme,drive=nvmedisk0,serial=nvme-1
        )
    fi

    # Networking is intentionally not attached: ZonderqOS currently builds
    # with the network stack disabled.
    qemu-system-x86_64 "${qemu_args[@]}"
}

find_32gb_usb() {
    local -a devices=()
    local name size removable type transport

    while read -r name size removable type transport; do
        [[ "$removable" == "1" ]] || continue
        [[ "$type" == "disk" ]] || continue
        (( size >= MIN_USB_BYTES && size <= MAX_USB_BYTES )) || continue

        # Typical SD-card readers show up as USB. MMC is accepted too.
        if [[ "$transport" == "usb" || "$transport" == "mmc" || -z "$transport" ]]; then
            devices+=("/dev/$name")
        fi
    done < <(lsblk -b -dn -o NAME,SIZE,RM,TYPE,TRAN)

    if (( ${#devices[@]} == 0 )); then
        echo "[BLAD] Nie znaleziono wymiennego nosnika ~32 GB." >&2
        echo "Podlacz karte/czytnik i sprawdz: lsblk -o NAME,RM,SIZE,MODEL,TRAN,FSTYPE,LABEL" >&2
        exit 1
    fi

    if (( ${#devices[@]} > 1 )); then
        echo "[BLAD] Znaleziono wiecej niz jeden wymienny nosnik ~32 GB:" >&2
        printf '  %s\n' "${devices[@]}" >&2
        echo "Odłącz pozostale nosniki, zeby nie bylo ryzyka wyboru zlego dysku." >&2
        exit 1
    fi

    printf '%s\n' "${devices[0]}"
}

find_zonderq_partition() {
    local device="$1"
    local -a partitions=()
    local part fstype label

    while read -r part fstype label; do
        [[ "$fstype" == "vfat" ]] || continue
        [[ "$label" == "$USB_LABEL" ]] || continue
        partitions+=("$part")
    done < <(lsblk -nrpo NAME,FSTYPE,LABEL "$device")

    if (( ${#partitions[@]} != 1 )); then
        echo "[BLAD] $device nie ma dokladnie jednej partycji FAT z etykieta $USB_LABEL." >&2
        echo "Nie zapisuje niczego na przypadkowy dysk." >&2
        exit 1
    fi

    printf '%s\n' "${partitions[0]}"
}

install_arm64_to_usb() {
    echo "[ARM64] Budowanie ZonderqOS..."
    cosmos build -a arm64

    local iso="$ROOT_DIR/output-arm64/ZonderqOS.iso"
    if [[ ! -f "$iso" ]]; then
        echo "[BLAD] Brak obrazu: $iso" >&2
        exit 1
    fi

    echo
    echo "[USB] Szukanie wymiennego nosnika ~32 GB..."
    local usb_device
    usb_device="$(find_32gb_usb)"
    local usb_partition
    usb_partition="$(find_zonderq_partition "$usb_device")"

    echo "[USB] Znaleziono: $usb_device"
    lsblk -o NAME,RM,SIZE,MODEL,TRAN,FSTYPE,LABEL,MOUNTPOINTS "$usb_device"
    echo

    read -r -p "Wgrac build ARM64 na $usb_partition? [t/N] " answer
    if [[ ! "$answer" =~ ^[TtYy]$ ]]; then
        echo "Anulowano."
        exit 0
    fi

    local iso_mount usb_mount
    iso_mount="$(mktemp -d /tmp/zonderq-iso.XXXXXX)"
    usb_mount="$(findmnt -nr -S "$usb_partition" -o TARGET || true)"
    local usb_temp_mount=""

    cleanup() {
        set +e
        mountpoint -q "$iso_mount" && sudo umount "$iso_mount"
        if findmnt -nr -S "$usb_partition" >/dev/null 2>&1; then
            sudo umount "$usb_partition"
        fi
        [[ -n "$usb_temp_mount" ]] && rmdir "$usb_temp_mount" 2>/dev/null || true
        rmdir "$iso_mount" 2>/dev/null || true
    }
    trap cleanup EXIT

    sudo mount -o loop,ro "$iso" "$iso_mount"

    if [[ -z "$usb_mount" ]]; then
        usb_temp_mount="$(mktemp -d /tmp/zonderq-usb.XXXXXX)"
        sudo mount "$usb_partition" "$usb_temp_mount"
        usb_mount="$usb_temp_mount"
    fi

    # Safety check: this must already be the prepared Raspberry Pi 4 card.
    if [[ ! -f "$usb_mount/RPI_EFI.fd" || ! -f "$usb_mount/config.txt" || ! -f "$usb_mount/start4.elf" ]]; then
        echo "[BLAD] $usb_partition nie wyglada jak przygotowana karta RPi4/PFTF." >&2
        echo "Brakuje RPI_EFI.fd, config.txt albo start4.elf. Niczego nie nadpisano." >&2
        exit 1
    fi

    if [[ ! -f "$iso_mount/EFI/BOOT/BOOTAA64.EFI" || ! -f "$iso_mount/boot/ZonderqOS.elf" ]]; then
        echo "[BLAD] Obraz ARM64 nie zawiera wymaganych plikow bootowania." >&2
        exit 1
    fi

    echo "[USB] Wgrywanie Limine + ZonderqOS na $usb_partition..."
    sudo mkdir -p "$usb_mount/EFI/BOOT"
    sudo cp -f "$iso_mount/EFI/BOOT/BOOTAA64.EFI" "$usb_mount/EFI/BOOT/BOOTAA64.EFI"
    sudo rm -rf "$usb_mount/boot"
    sudo cp -a "$iso_mount/boot" "$usb_mount/boot"
    sudo rm -f "$usb_mount/EFI/BOOT/BOOTAA64.EFI.disabled"
    sync

    echo
    echo "[OK] ARM64 zostal wgrany na $usb_partition."
    echo "[OK] Firmware RPi/PFTF zostal zachowany."
    echo "[OK] Nosnik zostanie odmontowany i bedzie gotowy do wyjecia."
}

print_header
sync_repo

choice="${1:-}"
if [[ -z "$choice" ]]; then
    echo "1) x86_64 - build + QEMU"
    echo "2) ARM64  - build + znajdz karte USB ~32 GB + wgraj RPi4"
    echo
    read -r -p "Wybierz [1/2]: " choice
fi

case "$choice" in
    1|x64|x86|x86_64)
        run_x64
        ;;
    2|arm64|rpi|rpi4)
        install_arm64_to_usb
        ;;
    *)
        echo "Nieprawidlowy wybor: $choice" >&2
        echo "Uzycie: ./run.sh [x64|arm64]" >&2
        exit 2
        ;;
esac
