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
HW_CPU_LABEL=""
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
    cat <<'EOF_USAGE'
Uzycie:
  ./run.sh
  ./run.sh [x64|arm64]
  ./run.sh [x64|arm64] [opcje zaawansowane]

Tryb interaktywny:
  1. wybierasz architekture,
  2. wybierasz 1 z 10 realnych profili CPU,
  3. wpisujesz ilosc RAM dla systemu.

Opcje zaawansowane:
  --cpu MODEL       model CPU QEMU, np. Skylake-Client, EPYC-Milan, cortex-a72
  --cores N         liczba fizycznych rdzeni na socket
  --threads N       liczba watkow SMT na rdzen (1 = bez SMT)
  --sockets N       liczba socketow
  --ram SIZE        RAM, np. 512M, 2G, 8G (sama liczba = MB)
  -h, --help        pomoc

Przyklady:
  ./run.sh
  ./run.sh x64
  ./run.sh arm64
  ./run.sh x64 --cpu EPYC-Milan --cores 24 --threads 2 --ram 8G
  ./run.sh arm64 --cpu cortex-a72 --cores 4 --threads 1 --ram 2G
EOF_USAGE
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
    git fetch --no-tags origin refs/heads/main:refs/remotes/origin/main
    git merge --ff-only refs/remotes/origin/main
    check_untracked_build_inputs
    echo
}

clean_build_cache() {
    local arch="$1"
    local label
    label="$(printf '%s' "$arch" | tr '[:lower:]' '[:upper:]')"
    echo "[$label] Czyszczenie cache builda..."
    rm -rf "$ROOT_DIR/obj" "$ROOT_DIR/bin" "$ROOT_DIR/output-$arch"
}

build_iso() {
    local arch="$1"
    local label
    label="$(printf '%s' "$arch" | tr '[:lower:]' '[:upper:]')"

    clean_build_cache "$arch"

    echo "[$label] Budowanie ZonderqOS..."
    if [[ "$arch" == "x64" ]]; then
        bash "$ROOT_DIR/tools/build-x64-smt.sh"
    else
        require_command cosmos
        cosmos build -a "$arch"
    fi

    BUILD_ISO="$ROOT_DIR/output-$arch/ZonderqOS.iso"
    [[ -f "$BUILD_ISO" ]] || fail "Brak obrazu po buildzie: $BUILD_ISO"
}

apply_cpu_profile() {
    local arch="$1"
    local profile="$2"

    HW_SOCKETS=1

    if [[ "$arch" == "x64" ]]; then
        case "$profile" in
            1)
                HW_CPU_MODEL="Conroe"
                HW_CPU_LABEL="Intel Core 2 Duo E6600 (Conroe)"
                HW_CORES=2; HW_THREADS=1 ;;
            2)
                HW_CPU_MODEL="Nehalem"
                HW_CPU_LABEL="Intel Core i7-920 (Nehalem)"
                HW_CORES=4; HW_THREADS=2 ;;
            3)
                HW_CPU_MODEL="SandyBridge"
                HW_CPU_LABEL="Intel Core i7-2600 (Sandy Bridge)"
                HW_CORES=4; HW_THREADS=2 ;;
            4)
                HW_CPU_MODEL="IvyBridge"
                HW_CPU_LABEL="Intel Core i7-3770 (Ivy Bridge)"
                HW_CORES=4; HW_THREADS=2 ;;
            5)
                HW_CPU_MODEL="Haswell"
                HW_CPU_LABEL="Intel Core i7-4770 (Haswell)"
                HW_CORES=4; HW_THREADS=2 ;;
            6)
                HW_CPU_MODEL="Broadwell"
                HW_CPU_LABEL="Intel Core i7-5775C (Broadwell)"
                HW_CORES=4; HW_THREADS=2 ;;
            7)
                HW_CPU_MODEL="Skylake-Client"
                HW_CPU_LABEL="Intel Core i7-6700K (Skylake)"
                HW_CORES=4; HW_THREADS=2 ;;
            8)
                HW_CPU_MODEL="Cascadelake-Server"
                HW_CPU_LABEL="Intel Xeon Platinum 8280 (Cascade Lake)"
                HW_CORES=28; HW_THREADS=2 ;;
            9)
                HW_CPU_MODEL="EPYC-Rome"
                HW_CPU_LABEL="AMD EPYC 7302 (Rome)"
                HW_CORES=16; HW_THREADS=2 ;;
            10)
                HW_CPU_MODEL="EPYC-Milan"
                HW_CPU_LABEL="AMD EPYC 7443 (Milan)"
                HW_CORES=24; HW_THREADS=2 ;;
            *)
                fail "Nieprawidlowy profil x64: $profile" ;;
        esac
    else
        case "$profile" in
            1)
                HW_CPU_MODEL="cortex-a35"
                HW_CPU_LABEL="NXP i.MX 8QuadXPlus / Cortex-A35"
                HW_CORES=4; HW_THREADS=1 ;;
            2)
                HW_CPU_MODEL="cortex-a53"
                HW_CPU_LABEL="Raspberry Pi 3 BCM2837 / Cortex-A53"
                HW_CORES=4; HW_THREADS=1 ;;
            3)
                HW_CPU_MODEL="cortex-a55"
                HW_CPU_LABEL="Rockchip RK3568 / Cortex-A55"
                HW_CORES=4; HW_THREADS=1 ;;
            4)
                HW_CPU_MODEL="cortex-a57"
                HW_CPU_LABEL="NVIDIA Tegra X1 A57 cluster / Cortex-A57"
                HW_CORES=4; HW_THREADS=1 ;;
            5)
                HW_CPU_MODEL="cortex-a72"
                HW_CPU_LABEL="Raspberry Pi 4 BCM2711 / Cortex-A72"
                HW_CORES=4; HW_THREADS=1 ;;
            6)
                HW_CPU_MODEL="cortex-a76"
                HW_CPU_LABEL="Raspberry Pi 5 BCM2712 / Cortex-A76"
                HW_CORES=4; HW_THREADS=1 ;;
            7)
                HW_CPU_MODEL="cortex-a710"
                HW_CPU_LABEL="Snapdragon 8 Gen 1 A710 cluster / Cortex-A710"
                HW_CORES=3; HW_THREADS=1 ;;
            8)
                HW_CPU_MODEL="neoverse-n1"
                HW_CPU_LABEL="AWS Graviton2 / Neoverse-N1"
                HW_CORES=64; HW_THREADS=1 ;;
            9)
                HW_CPU_MODEL="neoverse-v1"
                HW_CPU_LABEL="AWS Graviton3 / Neoverse-V1"
                HW_CORES=64; HW_THREADS=1 ;;
            10)
                HW_CPU_MODEL="a64fx"
                HW_CPU_LABEL="Fujitsu A64FX"
                HW_CORES=48; HW_THREADS=1 ;;
            *)
                fail "Nieprawidlowy profil ARM64: $profile" ;;
        esac
    fi
}

print_cpu_menu() {
    local arch="$1"
    echo
    echo "Wybierz model procesora:"
    echo

    if [[ "$arch" == "x64" ]]; then
        echo " 1) Intel Core 2 Duo E6600       Conroe             2C / 2T"
        echo " 2) Intel Core i7-920            Nehalem            4C / 8T"
        echo " 3) Intel Core i7-2600           Sandy Bridge       4C / 8T"
        echo " 4) Intel Core i7-3770           Ivy Bridge         4C / 8T"
        echo " 5) Intel Core i7-4770           Haswell            4C / 8T"
        echo " 6) Intel Core i7-5775C          Broadwell          4C / 8T"
        echo " 7) Intel Core i7-6700K          Skylake            4C / 8T"
        echo " 8) Intel Xeon Platinum 8280     Cascade Lake      28C / 56T"
        echo " 9) AMD EPYC 7302                Rome              16C / 32T"
        echo "10) AMD EPYC 7443                Milan             24C / 48T"
        echo
        echo "QEMU emuluje rodzine CPU; nazwa po lewej jest realnym SKU, z ktorego bierzemy topologie."
    else
        echo " 1) NXP i.MX 8QuadXPlus          Cortex-A35         4C / 4T"
        echo " 2) Raspberry Pi 3 BCM2837       Cortex-A53         4C / 4T"
        echo " 3) Rockchip RK3568              Cortex-A55         4C / 4T"
        echo " 4) NVIDIA Tegra X1 A57 cluster  Cortex-A57         4C / 4T"
        echo " 5) Raspberry Pi 4 BCM2711       Cortex-A72         4C / 4T"
        echo " 6) Raspberry Pi 5 BCM2712       Cortex-A76         4C / 4T"
        echo " 7) Snapdragon 8 Gen 1 A710      Cortex-A710        3C / 3T"
        echo " 8) AWS Graviton2                Neoverse-N1       64C / 64T"
        echo " 9) AWS Graviton3                Neoverse-V1       64C / 64T"
        echo "10) Fujitsu A64FX                A64FX             48C / 48T"
        echo
        echo "ARM nie ma SMT w tych profilach. A710 odwzorowuje 3-rdzeniowy klaster A710 z heterogenicznego SoC."
    fi
}

select_cpu_profile() {
    local arch="$1"
    local default_profile
    local choice

    if [[ "$arch" == "x64" ]]; then
        default_profile=7
    else
        default_profile=5
    fi

    print_cpu_menu "$arch"
    read -r -p "Wybierz [1-10, Enter=${default_profile}]: " choice
    choice="${choice:-$default_profile}"
    [[ "$choice" =~ ^([1-9]|10)$ ]] || fail "Wybierz numer 1-10."
    apply_cpu_profile "$arch" "$choice"
}

prompt_ram() {
    local arch="$1"
    local default_ram
    local entered

    if [[ "$arch" == "x64" ]]; then
        default_ram="2G"
    else
        default_ram="512M"
    fi

    echo
    read -r -p "Ile RAM przydzielic systemowi? [${default_ram}]: " entered
    HW_RAM="${entered:-$default_ram}"
}

set_advanced_defaults() {
    local arch="$1"
    if [[ "$arch" == "x64" ]]; then
        apply_cpu_profile x64 7
        HW_RAM="2G"
    else
        apply_cpu_profile arm64 5
        HW_RAM="512M"
    fi
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
                HW_CPU_MODEL="$2"
                HW_CPU_LABEL="Custom: $2"
                shift 2 ;;
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
        fail "Nieprawidlowy RAM '$HW_RAM'. Przyklady: 512M, 2G, 8G."

    HW_VCPUS=$((HW_SOCKETS * HW_CORES * HW_THREADS))
    (( HW_VCPUS >= 1 && HW_VCPUS <= 128 )) || \
        fail "Laczna liczba watkow/vCPU musi byc 1..128 (jest: $HW_VCPUS)."

    [[ -n "$HW_CPU_MODEL" ]] || fail "Model CPU nie moze byc pusty."
}

verify_cpu_model() {
    local arch="$1"
    local emulator

    if [[ "$arch" == "x64" ]]; then
        emulator="qemu-system-x86_64"
    else
        emulator="qemu-system-aarch64"
    fi

    require_command "$emulator"
    if ! "$emulator" -cpu help 2>/dev/null | grep -Fq "$HW_CPU_MODEL"; then
        fail "QEMU nie zna modelu CPU '$HW_CPU_MODEL' dla $arch. Sprawdz: $emulator -cpu help"
    fi
}

print_hardware_summary() {
    local arch="$1"
    local total_threads="$HW_VCPUS"

    echo "[QEMU] CPU: $HW_CPU_LABEL"
    echo "[QEMU] Model: $HW_CPU_MODEL"
    echo "[QEMU] Topologia: ${HW_SOCKETS} socket x ${HW_CORES} core x ${HW_THREADS} thread/core = ${total_threads} logicznych CPU"
    echo "[QEMU] RAM: $HW_RAM"

    if [[ "$arch" == "arm64" && "$HW_VCPUS" -gt 1 ]]; then
        echo "[ARM64] UWAGA: QEMU wystawi ${HW_VCPUS} vCPU zgodnie z realnym profilem, ale obecny Cosmos ARM64 HAL zarzadza tylko CPU0." >&2
        echo "[ARM64] Pelne wykorzystanie pozostalych rdzeni wymaga osobnego bring-up SMP w HAL-u." >&2
    fi
}

run_x64() {
    verify_cpu_model x64
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
    verify_cpu_model arm64
    build_iso arm64
    local iso="$BUILD_ISO"

    local firmware
    firmware="$(find_arm64_firmware)"

    echo
    echo "[ARM64] Uruchamianie pelnego profilu QEMU virt..."
    echo "[ARM64] UEFI: $firmware"
    echo "[ARM64] QEMU: $(qemu-system-aarch64 --version | head -n 1)"
    echo "[ARM64] Input: VirtIO MMIO keyboard + mouse"
    echo "[ARM64] Display: UEFI GOP / virtio-gpu-pci 1920x1080; Limine requests 1920x1080x32"
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
        -device virtio-gpu-pci,xres=1920,yres=1080
        -display gtk,zoom-to-fit=on
        -serial stdio
        -no-reboot
        -no-shutdown
    )

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

if [[ $# -eq 0 ]]; then
    select_cpu_profile "$arch"
    prompt_ram "$arch"
else
    set_advanced_defaults "$arch"
    parse_hardware_args "$@"
fi

validate_hardware

echo
echo "Wybrana konfiguracja:"
print_hardware_summary "$arch"
echo

case "$arch" in
    x64) run_x64 ;;
    arm64) run_arm64 ;;
esac
