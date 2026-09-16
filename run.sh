#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUILD_ISO=""
cd "$ROOT_DIR"

HW_SOCKETS=1
HW_CORES=1
HW_THREADS=1
HW_RAM=""
HW_CPU_MODEL=""
HW_VCPUS=1

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

usage() {
    cat <<'EOF'
Uzycie:
  ./run.sh [x64|arm64] [opcje]

Opcje sprzetu QEMU:
  --sockets N       liczba socketow (domyslnie 1)
  --cores N         rdzenie na socket
  --threads N       watki na rdzen
  --ram SIZE        RAM, np. 256M, 1G, 4G (sama liczba = MB)
  --cpu MODEL       model CPU QEMU, np. max, qemu64, cortex-a72
  --preset NAME     tiny | dual | quad | smt | stress
  -h, --help        pomoc

Przyklady:
  ./run.sh x64 --cores 4 --threads 2 --ram 2G
  ./run.sh x64 --sockets 2 --cores 2 --threads 1 --ram 1G
  ./run.sh x64 --preset stress
  ./run.sh arm64 --cores 4 --ram 1G
  ./run.sh arm64 --cores 8 --ram 4G --cpu cortex-a72
EOF
}

check_untracked_build_inputs() {
    local untracked
    untracked="$(git ls-files --others --exclude-standard -- '*.cs' '*.csproj' '*.props' '*.targets')"
    if [[ -n "$untracked" ]]; then
        echo "[BLAD] W katalogu sa niecommitowane pliki zrodlowe, ktore MSBuild moze automatycznie kompilowac:" >&2
        while IFS= read -r path; do
            [[ -n "$path" ]] && echo "  $path" >&2
        done <<< "$untracked"
        echo >&2
        echo "Usun/stash/commit te pliki przed buildem. Sprawdz: git status --short" >&2
        echo "Stare pliki kompatybilnosci .cs potrafia powodowac bledy mimo komunikatu 'Already up to date'." >&2
        exit 1
    fi
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

    check_untracked_build_inputs

    # Fetch one explicit remote branch. This avoids local branch.*.merge config
    # accidentally making a normal pull target multiple branches.
    git fetch --no-tags origin refs/heads/main:refs/remotes/origin/main
    git merge --ff-only refs/remotes/origin/main

    # A merge may introduce/remove wildcard-compiled files, so check again.
    check_untracked_build_inputs
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

set_arch_defaults() {
    local arch="$1"
    HW_SOCKETS=1
    HW_CORES=1
    HW_THREADS=1

    if [[ "$arch" == "x64" ]]; then
        HW_RAM="2G"
        HW_CPU_MODEL="max"
    else
        HW_RAM="512M"
        HW_CPU_MODEL="cortex-a72"
    fi
}

apply_preset() {
    local preset="$1"
    case "$preset" in
        tiny)
            HW_SOCKETS=1; HW_CORES=1; HW_THREADS=1; HW_RAM="256M" ;;
        dual)
            HW_SOCKETS=1; HW_CORES=2; HW_THREADS=1; HW_RAM="512M" ;;
        quad)
            HW_SOCKETS=1; HW_CORES=4; HW_THREADS=1; HW_RAM="1G" ;;
        smt)
            HW_SOCKETS=1; HW_CORES=4; HW_THREADS=2; HW_RAM="2G" ;;
        stress)
            HW_SOCKETS=1; HW_CORES=8; HW_THREADS=2; HW_RAM="4G" ;;
        *)
            fail "Nieznany preset '$preset'. Dostepne: tiny, dual, quad, smt, stress." ;;
    esac
}

parse_hardware_args() {
    while [[ $# -gt 0 ]]; do
        case "$1" in
            --sockets)
                [[ $# -ge 2 ]] || fail "--sockets wymaga wartosci."
                HW_SOCKETS="$2"; shift 2 ;;
            --cores)
                [[ $# -ge 2 ]] || fail "--cores wymaga wartosci."
                HW_CORES="$2"; shift 2 ;;
            --threads)
                [[ $# -ge 2 ]] || fail "--threads wymaga wartosci."
                HW_THREADS="$2"; shift 2 ;;
            --ram)
                [[ $# -ge 2 ]] || fail "--ram wymaga wartosci."
                HW_RAM="$2"; shift 2 ;;
            --cpu)
                [[ $# -ge 2 ]] || fail "--cpu wymaga wartosci."
                HW_CPU_MODEL="$2"; shift 2 ;;
            --preset)
                [[ $# -ge 2 ]] || fail "--preset wymaga nazwy."
                apply_preset "$2"; shift 2 ;;
            -h|--help)
                usage
                exit 0 ;;
            *)
                fail "Nieznana opcja: $1. Uzyj --help." ;;
        esac
    done
}

validate_positive_int() {
    local name="$1"
    local value="$2"
    [[ "$value" =~ ^[1-9][0-9]*$ ]] || fail "$name musi byc dodatnia liczba calkowita (jest: '$value')."
}

validate_hardware() {
    validate_positive_int "sockets" "$HW_SOCKETS"
    validate_positive_int "cores" "$HW_CORES"
    validate_positive_int "threads" "$HW_THREADS"

    if [[ "$HW_RAM" =~ ^[1-9][0-9]*$ ]]; then
        HW_RAM="${HW_RAM}M"
    fi
    HW_RAM="${HW_RAM^^}"
    HW_RAM="${HW_RAM%B}"
    [[ "$HW_RAM" =~ ^[1-9][0-9]*[KMGTPE]$ ]] || \
        fail "Nieprawidlowy RAM '$HW_RAM'. Przyklady: 256M, 1G, 4G."

    HW_VCPUS=$((HW_SOCKETS * HW_CORES * HW_THREADS))
    (( HW_VCPUS >= 1 && HW_VCPUS <= 128 )) || \
        fail "Laczna liczba vCPU musi byc 1..128 (jest: $HW_VCPUS)."

    [[ -n "$HW_CPU_MODEL" ]] || fail "Model CPU nie moze byc pusty."
}

select_hardware_profile() {
    local arch="$1"
    echo
    echo "Profil sprzetu QEMU:"
    echo "1) Domyslny  - 1C/1T (${HW_RAM})"
    echo "2) Tiny      - 1C/1T, 256M"
    echo "3) Dual      - 2C/1T, 512M"
    echo "4) Quad      - 4C/1T, 1G"
    echo "5) SMT       - 4C/2T, 2G"
    echo "6) Stress    - 8C/2T, 4G"
    echo "7) Custom"
    echo

    local profile
    read -r -p "Wybierz [1-7, Enter=1]: " profile
    profile="${profile:-1}"

    case "$profile" in
        1) ;;
        2) apply_preset tiny ;;
        3) apply_preset dual ;;
        4) apply_preset quad ;;
        5) apply_preset smt ;;
        6) apply_preset stress ;;
        7)
            read -r -p "Sockety [1]: " HW_SOCKETS
            HW_SOCKETS="${HW_SOCKETS:-1}"
            read -r -p "Rdzenie na socket [1]: " HW_CORES
            HW_CORES="${HW_CORES:-1}"
            read -r -p "Watki na rdzen [1]: " HW_THREADS
            HW_THREADS="${HW_THREADS:-1}"
            read -r -p "RAM [${HW_RAM}]: " custom_ram
            HW_RAM="${custom_ram:-$HW_RAM}"
            read -r -p "Model CPU [${HW_CPU_MODEL}]: " custom_cpu
            HW_CPU_MODEL="${custom_cpu:-$HW_CPU_MODEL}"
            ;;
        *) fail "Nieprawidlowy profil: $profile" ;;
    esac

    validate_hardware
}

print_hardware_summary() {
    local arch="$1"
    echo "[QEMU] CPU model: $HW_CPU_MODEL"
    echo "[QEMU] Topologia: ${HW_SOCKETS} socket x ${HW_CORES} core x ${HW_THREADS} thread = ${HW_VCPUS} vCPU"
    echo "[QEMU] RAM: $HW_RAM"

    if [[ "$arch" == "arm64" && "$HW_VCPUS" -gt 1 ]]; then
        echo "[ARM64] UWAGA: QEMU wystawi ${HW_VCPUS} vCPU, ale obecny Cosmos ARM64 HAL zarzadza tylko CPU0." >&2
        echo "[ARM64] RAM i hardware enumeration testujemy realnie; pelne ARM SMP wymaga osobnego bring-up AP/secondary CPUs." >&2
    fi
}

run_x64() {
    require_command qemu-system-x86_64
    build_iso x64
    local iso="$BUILD_ISO"

    echo
    echo "[X64] Uruchamianie QEMU..."
    print_hardware_summary x64

    local qemu_data="$HOME/.cosmos/tools/share/qemu"
    local smp="cpus=${HW_VCPUS},sockets=${HW_SOCKETS},cores=${HW_CORES},threads=${HW_THREADS}"
    local -a qemu_args=(
        -L "$qemu_data"
        -M q35
        -cpu "$HW_CPU_MODEL"
        -smp "$smp"
        -m "$HW_RAM"
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
    echo "[ARM64] PCI address space: low ECAM/MMIO compatibility mode"
    print_hardware_summary arm64

    local smp="cpus=${HW_VCPUS},sockets=${HW_SOCKETS},cores=${HW_CORES},threads=${HW_THREADS}"
    local -a qemu_args=(
        -M virt,gic-version=3,highmem-ecam=off,highmem-mmio=off
        -cpu "$HW_CPU_MODEL"
        -smp "$smp"
        -m "$HW_RAM"
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
interactive_choice=0
if [[ -z "$choice" ]]; then
    interactive_choice=1
    echo "1) x86_64 - full ZonderqOS + QEMU"
    echo "2) ARM64  - full ZonderqOS + QEMU virt"
    echo
    read -r -p "Wybierz [1/2]: " choice
else
    shift
fi

case "$choice" in
    1|x64|x86|x86_64)
        arch="x64" ;;
    2|arm64|arm|qemu-arm64)
        arch="arm64" ;;
    -h|--help|help)
        usage
        exit 0 ;;
    *)
        fail "Nieprawidlowy wybor: $choice. Uzyj ./run.sh --help"
        ;;
esac

set_arch_defaults "$arch"

if (( interactive_choice )); then
    select_hardware_profile "$arch"
else
    parse_hardware_args "$@"
    validate_hardware
fi

case "$arch" in
    x64) run_x64 ;;
    arm64) run_arm64 ;;
esac