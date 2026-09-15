#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT_DIR"

MIN_USB_BYTES=28000000000
MAX_USB_BYTES=35000000000
USB_LABEL="ZONDERQ"

ISO_MOUNT=""
USB_PARTITION=""
USB_MOUNT=""
USB_TEMP_MOUNT=""

cleanup_mounts() {
    set +e

    if [[ -n "${ISO_MOUNT:-}" ]] && mountpoint -q "$ISO_MOUNT"; then
        sudo umount "$ISO_MOUNT"
    fi

    if [[ -n "${USB_PARTITION:-}" ]] && findmnt -nr -S "$USB_PARTITION" >/dev/null 2>&1; then
        sudo umount "$USB_PARTITION"
    fi

    if [[ -n "${USB_TEMP_MOUNT:-}" ]]; then
        rmdir "$USB_TEMP_MOUNT" 2>/dev/null || true
    fi

    if [[ -n "${ISO_MOUNT:-}" ]]; then
        rmdir "$ISO_MOUNT" 2>/dev/null || true
    fi

    ISO_MOUNT=""
    USB_PARTITION=""
    USB_MOUNT=""
    USB_TEMP_MOUNT=""
}

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
        echo "Odlacz pozostale nosniki, zeby nie bylo ryzyka wyboru zlego dysku." >&2
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
    USB_PARTITION="$(find_zonderq_partition "$usb_device")"

    echo "[USB] Znaleziono: $usb_device"
    lsblk -o NAME,RM,SIZE,MODEL,TRAN,FSTYPE,LABEL,MOUNTPOINTS "$usb_device"
    echo

    read -r -p "Wgrac build ARM64 na $USB_PARTITION? [t/N] " answer
    if [[ ! "$answer" =~ ^[TtYy]$ ]]; then
        echo "Anulowano."
        exit 0
    fi

    ISO_MOUNT="$(mktemp -d /tmp/zonderq-iso.XXXXXX)"
    USB_MOUNT="$(findmnt -nr -S "$USB_PARTITION" -o TARGET || true)"
    USB_TEMP_MOUNT=""

    trap cleanup_mounts EXIT INT TERM

    sudo mount -o loop,ro "$iso" "$ISO_MOUNT"

    if [[ -z "$USB_MOUNT" ]]; then
        USB_TEMP_MOUNT="$(mktemp -d /tmp/zonderq-usb.XXXXXX)"
        sudo mount "$USB_PARTITION" "$USB_TEMP_MOUNT"
        USB_MOUNT="$USB_TEMP_MOUNT"
    fi

    # Safety check: this must already be the prepared Raspberry Pi 4 card.
    if [[ ! -f "$USB_MOUNT/RPI_EFI.fd" || ! -f "$USB_MOUNT/config.txt" || ! -f "$USB_MOUNT/start4.elf" ]]; then
        echo "[BLAD] $USB_PARTITION nie wyglada jak przygotowana karta RPi4/PFTF." >&2
        echo "Brakuje RPI_EFI.fd, config.txt albo start4.elf. Niczego nie nadpisano." >&2
        exit 1
    fi

    if [[ ! -f "$ISO_MOUNT/EFI/BOOT/BOOTAA64.EFI" || ! -f "$ISO_MOUNT/boot/ZonderqOS.elf" ]]; then
        echo "[BLAD] Obraz ARM64 nie zawiera wymaganych plikow bootowania." >&2
        exit 1
    fi

    echo "[USB] Wgrywanie Limine + ZonderqOS na $USB_PARTITION..."
    sudo mkdir -p "$USB_MOUNT/EFI/BOOT"
    sudo cp -f "$ISO_MOUNT/EFI/BOOT/BOOTAA64.EFI" "$USB_MOUNT/EFI/BOOT/BOOTAA64.EFI"
    sudo rm -rf "$USB_MOUNT/boot"
    sudo mkdir -p "$USB_MOUNT/boot"
    sudo cp -R "$ISO_MOUNT/boot/." "$USB_MOUNT/boot/"
    sudo rm -f "$USB_MOUNT/EFI/BOOT/BOOTAA64.EFI.disabled"
    sync

    cleanup_mounts
    trap - EXIT INT TERM

    echo
    echo "[OK] ARM64 zostal wgrany na karte."
    echo "[OK] Firmware RPi/PFTF zostal zachowany."
    echo "[OK] Nosnik zostal odmontowany i jest gotowy do wyjecia."
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
