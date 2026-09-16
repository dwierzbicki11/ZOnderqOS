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
    echo "[ARM64] Czyszczenie starego cache NativeAOT/multi-arch..."
    # Cosmos 3.0.84 could leave architecture-specific System assemblies in the
    # patcher/ILC intermediate tree. A clean ARM64 publish is cheap compared to
    # debugging an ABI mix where Roslyn and ILC see different graphics APIs.
    rm -rf "$ROOT_DIR/obj" "$ROOT_DIR/bin" "$ROOT_DIR/output-arm64"

    echo "[ARM64] Budowanie pelnego ZonderqOS desktop dla QEMU virt..."
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
    echo "[ARM64] Uruchamianie pelnego profilu QEMU virt..."
    echo "[ARM64] UEFI: $firmware"
    echo "[ARM64] QEMU: $(qemu-system-aarch64 --version | head -n 1)"
    echo "[ARM64] Input: VirtIO MMIO keyboard + mouse"
    echo "[ARM64] Display: UEFI GOP / ramfb"
    echo "[ARM64] Scheduler: ON"
    echo "[ARM64] PCI/storage: ON (NVMe when an image is present)"

    # ARM64 is intended to expose the same ZonderqOS userspace as x86_64.
    # The devices differ underneath: GICv3 + VirtIO-MMIO input + PCIe/NVMe.
    # Do not force highmem=off; modern QEMU can otherwise reject the machine
    # before UEFI starts because the virt platform no longer fits below 4 GiB.
    local -a qemu_args=(
        -M virt,gic-version=3
        -cpu cortex-a72
        -m 512M
        -bios "$firmware"
        -drive "if=none,id=cd,file=$iso,format=raw,readonly=on"
        -device virtio-scsi-pci
        -device scsi-cd,drive=cd,bootindex=0
        -device virtio-keyboard-device
        -device virtio-mouse-device
        -device ramfb
        -display gtk,zoom-to-fit=on
        -serial stdio
        -no-reboot
        -no-shutdown
    )

    # Reuse the same persistent ZonderqOS disk image on both architectures,
    # but expose it as NVMe on ARM64. Cosmos Gen3 has a PCI/NVMe path on QEMU
    # virt, while the keyboard and mouse remain VirtIO-MMIO devices.
    if [[ -f "$ROOT_DIR/zonder_disk.img" ]]; then
        qemu_args+=(
            -drive "file=$ROOT_DIR/zonder_disk.img,if=none,id=armroot,format=raw"
            -device nvme,drive=armroot,serial=zonderq-arm-root
        )
        echo "[ARM64] Root/data disk: zonder_disk.img -> NVMe"
    elif [[ -f "$ROOT_DIR/disk_nvme_2G.img" ]]; then
        qemu_args+=(
            -drive "file=$ROOT_DIR/disk_nvme_2G.img,if=none,id=armroot,format=raw"
            -device nvme,drive=armroot,serial=zonderq-arm-root
        )
        echo "[ARM64] Root/data disk: disk_nvme_2G.img -> NVMe"
    else
        echo "[ARM64] UWAGA: brak persistent disk image; VFS nie bedzie mial partycji root." >&2
        echo "[ARM64] Utworz/wykorzystaj zonder_disk.img, aby login, pliki i ustawienia byly trwale." >&2
    fi

    qemu-system-aarch64 "${qemu_args[@]}"
}

print_header
sync_repo

choice="${1:-}"
if [[ -z "$choice" ]]; then
    echo "1) x86_64 - full ZonderqOS + QEMU"
    echo "2) ARM64  - full ZonderqOS + QEMU virt"
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
