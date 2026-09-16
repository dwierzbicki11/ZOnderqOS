#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT_DIR"

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

    if ! command -v qemu-system-x86_64 >/dev/null 2>&1; then
        echo "[BLAD] Brak qemu-system-x86_64 w PATH." >&2
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

    # Networking remains intentionally disabled in the current ZonderqOS profile.
    qemu-system-x86_64 "${qemu_args[@]}"
}

find_arm64_firmware() {
    local -a candidates=(
        "$HOME/.cosmos/tools/qemu/share/qemu/edk2-aarch64-code.fd"
        "$HOME/.cosmos/tools/share/qemu/edk2-aarch64-code.fd"
        "/usr/share/qemu-efi-aarch64/QEMU_EFI.fd"
        "/usr/share/AAVMF/AAVMF_CODE.fd"
        "/usr/share/edk2/aarch64/QEMU_EFI.fd"
    )

    local candidate
    for candidate in "${candidates[@]}"; do
        if [[ -f "$candidate" ]]; then
            printf '%s\n' "$candidate"
            return 0
        fi
    done

    echo "[BLAD] Nie znaleziono firmware UEFI AArch64 dla QEMU." >&2
    echo "Sprawdzone lokalizacje:" >&2
    printf '  %s\n' "${candidates[@]}" >&2
    return 1
}

run_arm64() {
    echo "[ARM64] Budowanie ZonderqOS dla QEMU virt..."
    cosmos build -a arm64

    local iso="$ROOT_DIR/output-arm64/ZonderqOS.iso"
    if [[ ! -f "$iso" ]]; then
        echo "[BLAD] Brak obrazu: $iso" >&2
        exit 1
    fi

    if ! command -v qemu-system-aarch64 >/dev/null 2>&1; then
        echo "[BLAD] Brak qemu-system-aarch64 w PATH." >&2
        exit 1
    fi

    local firmware
    firmware="$(find_arm64_firmware)"

    echo
    echo "[ARM64] Uruchamianie QEMU virt + VirtIO keyboard..."
    echo "[ARM64] UEFI: $firmware"
    echo "[ARM64] QEMU: $(qemu-system-aarch64 --version | head -n 1)"

    # Modern-QEMU-compatible ARM64 profile. Do not force highmem=off here:
    # on newer QEMU releases that constrains the whole virt machine to a
    # 32-bit physical address space and can make the machine fail before UEFI
    # starts. Cosmos' regular ARM64 profile also uses highmem enabled with
    # cortex-a72 and 512 MiB RAM. VirtIO keyboard still lives in the low MMIO
    # window scanned by the Cosmos ARM64 HAL.
    local -a qemu_args=(
        -M virt,gic-version=3
        -cpu cortex-a72
        -m 512M
        -bios "$firmware"
        -drive "if=none,id=cd,file=$iso,format=raw,readonly=on"
        -device virtio-scsi-pci
        -device scsi-cd,drive=cd,bootindex=0
        -device virtio-keyboard-device
        -device ramfb
        -display gtk,zoom-to-fit=on
        -serial stdio
        -no-reboot
        -no-shutdown
    )

    qemu-system-aarch64 "${qemu_args[@]}"
}

print_header
sync_repo

choice="${1:-}"
if [[ -z "$choice" ]]; then
    echo "1) x86_64 - build + QEMU"
    echo "2) ARM64  - build + QEMU virt + VirtIO keyboard"
    echo
    read -r -p "Wybierz [1/2]: " choice
fi

case "$choice" in
    1|x64|x86|x86_64)
        run_x64
        ;;
    2|arm64|arm|qemu-arm64)
        run_arm64
        ;;
    *)
        echo "Nieprawidlowy wybor: $choice" >&2
        echo "Uzycie: ./run.sh [x64|arm64]" >&2
        exit 2
        ;;
esac
