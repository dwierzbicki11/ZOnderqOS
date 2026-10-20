#!/usr/bin/env bash
# Real persisted FAT32 volume smoke-test for Cosmos 3.0.94.
# All storage is QEMU image files: NO writes to a runner's physical disks.
set -euo pipefail

root="${1:?usage: bash tests/qemu-3094-persistent-disk.sh <ISO path>}"
iso="$(realpath "$root")"
test -s "$iso"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
efi="$work/efi-spoof.img"
data="$work/zonderqdata.img"
serial="$work/serial.log"
const_offset=$((2048 * 512))
export MTOOLS_SKIP_CHECK=1

# First drive: a hostile, spoof-labelled MBR EFI SYSTEM PARTITION (0xEF).
truncate -s 128M "$efi"
printf 'label: dos\nunit: sectors\n\nstart=2048, type=ef\n' | sfdisk "$efi"
mkfs.fat -F 32 -n ZONDERQDATA --offset=2048 "$efi"
sha256sum "$efi" > "$work/efi.sha256"

# Second drive: the *only* allowed data partition, FAT32-LBA type 0x0C.
truncate -s 512M "$data"
printf 'label: dos\nunit: sectors\n\nstart=2048, type=c\n' | sfdisk "$data"
mkfs.fat -F 32 -n ZONDERQDATA --offset=2048 "$data"
printf 'persistent-root-3094-check\n' > "$work/PERSIST.TXT"
mcopy -i "$data@@$const_offset" "$work/PERSIST.TXT" ::/PERSIST.TXT
mtype -i "$data@@$const_offset" ::/PERSIST.TXT | cmp - "$work/PERSIST.TXT"

run_guest() {
    local seconds="$1" pass="$2" pid
    : > "$serial"
    qemu-system-x86_64 \
        -machine q35 -cpu Nehalem -smp 1 -m 2048M \
        -cdrom "$iso" -boot d \
        -drive "file=$efi,format=raw,if=ide" \
        -drive "file=$data,format=raw,if=ide" \
        -vga std -display none -monitor none \
        -device qemu-xhci,id=xhci \
        -device usb-mouse,bus=xhci.0 \
        -serial "file:$serial" -no-reboot -no-shutdown \
        2> "$work/qemu.stderr" &
    pid=$!
    for ((i=0; i<seconds; i++)); do
        if ! kill -0 "$pid" 2>/dev/null; then
            tail -n 80 "$serial" >&2
            cat "$work/qemu.stderr" >&2
            wait "$pid" || true
            echo "[3094-persist] QEMU exited unexpectedly on boot $pass" >&2
            return 1
        fi
        if grep -Eqi 'kernel panic|fatal: exception|page fault' "$serial"; then
            tail -n 80 "$serial" >&2
            echo "[3094-persist] Kernel panic during boot $pass" >&2
            kill "$pid" 2>/dev/null || true
            wait "$pid" 2>/dev/null || true
            return 1
        fi
        sleep 1
    done
    kill "$pid" 2>/dev/null || true
    wait "$pid" 2>/dev/null || true
    sha256sum --check "$work/efi.sha256"
    mtype -i "$data@@$const_offset" ::/PERSIST.TXT | cmp - "$work/PERSIST.TXT"
    # The normal ZonderqOS initialization creates these on its approved root.
    mdir -i "$data@@$const_offset" ::/etc >/dev/null
    mdir -i "$data@@$const_offset" ::/root >/dev/null
    mdir -i "$data@@$const_offset" ::/var >/dev/null
    echo "[3094-persist] boot $pass: FHS on dedicated data disk, seed persisted, EFI unchanged."
}

run_guest 55 1
run_guest 30 2

echo "[3094-persist] PASS: FAT32 physical-disk-equivalent root persists over two boots without writing EFI."
