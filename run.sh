#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUILD_ISO=""
cd "$ROOT_DIR"

print_header() {
    clear
    echo "========================================"
    echo "        ZonderqOS build launcher"
    echo "========================================"
    echo
}

fail() {
    echo "[BLAD] $*" >&2
    exit 1
}

require_command() {
    command -v "$1" >/dev/null 2>&1 || fail "Brak $1 w PATH."
}

sync_repo() {
    echo "[GIT] Aktualizacja main..."

    local current_branch
    current_branch="$(git branch --show-current)"
    [[ "$current_branch" == "main" ]] || \
        fail "Launcher musi byc uruchomiony z brancha main (aktualnie: ${current_branch:-detached HEAD})."

    if [[ -n "$(git status --porcelain --untracked-files=no)" ]]; then
        fail "Masz lokalne zmiany w sledzonych plikach. Commit/stash przed automatycznym sync."
    fi

    # Fetch one explicit remote branch. This avoids local branch.*.merge config
    # accidentally making a normal pull target multiple branches.
    git fetch --no-tags origin refs/heads/main:refs/remotes/origin/main
    git merge --ff-only refs/remotes/origin/main
    echo
}

clean_build_cache() {
    local arch="$1"
    local label
    label="$(printf '%s' "$arch" | tr '[:lower:]' '[:upper:]')"
    echo "[$label] Czyszczenie cache builda..."

    # obj/bin are shared MSBuild/NativeAOT intermediates. Removing them on an
    # architecture switch prevents x64 and ARM64 compile assets from mixing.
    rm -rf "$ROOT_DIR/obj" "$ROOT_DIR/bin" "$ROOT_DIR/output-$arch"
}

build_iso() {
    local arch="$1"
    local label
    label="$(printf '%s' "$arch" | tr '[:lower:]' '[:upper:]')"

    require_command cosmos
    clean_build_cache "$arch"

    echo "[$label] Budowanie ZonderqOS..."
    cosmos build -a "$arch"

    BUILD_ISO="$ROOT_DIR/output-$arch/ZonderqOS.iso"
    [[ -f "$BUILD_ISO" ]] || fail "Brak obrazu po buildzie: $BUILD_ISO"
}

run_x64() {
    require_command qemu-system-x86_64
    build_iso x64
    local iso="$BUILD_ISO"

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

    # Networking is intentionally disabled in the current ZonderqOS profile.
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
    require_command qemu-system-aarch64
    build_iso arm64
    local iso="$BUILD_ISO"

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

    # Reuse one persistent data disk across architectures, but expose it as NVMe
    # on QEMU virt where Cosmos Gen3 has a PCI/NVMe path.
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
