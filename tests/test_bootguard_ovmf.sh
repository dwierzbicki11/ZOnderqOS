#!/usr/bin/env bash
# Real EFI BootGuard in OVMF, with a test-only EFI chainload target.
# Four consecutive firmware boots; the fake target never executes the kernel.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GUARD="$ROOT/tools/bootguard/build/bootguard.efi"
STUB="$ROOT/tools/bootguard/build/limine_stub.efi"
test -s "$GUARD" && test -s "$STUB"

TMP="$(mktemp -d)"
QPID=""
cleanup() {
  if [ -n "$QPID" ]; then
    kill "$QPID" 2>/dev/null || true
    wait "$QPID" 2>/dev/null || true
  fi
  rm -rf "$TMP"
}
trap cleanup EXIT

CODE="/usr/share/OVMF/OVMF_CODE_4M.fd"
VARS="/usr/share/OVMF/OVMF_VARS_4M.fd"
test -f "$CODE" && test -f "$VARS" || { echo "No OVMF 4M firmware" >&2; exit 1; }

IMG="$TMP/zq-efi-fat.img"
truncate -s 96M "$IMG"
mkfs.vfat -F 32 -n ZONDERQDATA "$IMG" >/dev/null
mmd -i "$IMG" ::/EFI ::/EFI/BOOT ::/boot
mcopy -i "$IMG" "$GUARD" ::/EFI/BOOT/BOOTX64.EFI
mcopy -i "$IMG" "$STUB" ::/EFI/BOOT/LIMINE.EFI
cat > "$TMP/boot_state.cfg" <<'STATE'
# ZonderqOS A/B Bootloader State Configuration
ACTIVE_SLOT=B
FALLBACK_SLOT=A
PENDING_SLOT=B
BOOT_ATTEMPTS=0
BOOTGUARD_PREBOOT=1
BOOTGUARD_FALLBACK=0
MAX_BOOT_ATTEMPTS=3
STATE
mcopy -i "$IMG" "$TMP/boot_state.cfg" ::/boot/boot_state.cfg
cp "$VARS" "$TMP/OVMF_VARS.fd"
DEBUG="$TMP/serial-e9.log"

# BootGuard sets the EFI one-shot variable; the stub emits its value over
# QEMU debugcon then reboots without ever executing a ZonderqOS kernel.
qemu-system-x86_64 \
  -machine q35 -cpu qemu64 -m 512M \
  -drive if=pflash,format=raw,readonly=on,file="$CODE" \
  -drive if=pflash,format=raw,file="$TMP/OVMF_VARS.fd" \
  -device qemu-xhci,id=xhci \
  -drive if=none,id=stick,file="$IMG",format=raw \
  -device usb-storage,drive=stick,bus=xhci.0,bootindex=0 \
  -display none -serial none -monitor none \
  -debugcon "file:$DEBUG" -global isa-debugcon.iobase=0xe9 \
  >"$TMP/qemu.log" 2>&1 &
QPID=$!

FOUND=0
for i in $(seq 1 140); do
  if [ -f "$DEBUG" ]; then
    mapfile -t events < <(grep -o 'ZQBOOT:[AB]' "$DEBUG" | head -n 4 || true)
    if [ "${#events[@]}" -ge 4 ]; then
      FOUND=1
      break
    fi
  fi
  if ! kill -0 "$QPID" 2>/dev/null; then
    echo "QEMU exited early" >&2
    cat "$TMP/qemu.log" >&2
    exit 1
  fi
  sleep 0.5
done

if [ "$FOUND" -ne 1 ]; then
  echo "BootGuard OVMF timeout: no four EFI boot decisions" >&2
  test ! -f "$DEBUG" || cat "$DEBUG" >&2
  cat "$TMP/qemu.log" >&2
  exit 1
fi

EXPECTED=$'ZQBOOT:B\nZQBOOT:B\nZQBOOT:B\nZQBOOT:A'
ACTUAL="$(printf '%s\n' "${events[@]}")"
printf '%s\n' "$ACTUAL"
test "$ACTUAL" = "$EXPECTED" || {
  echo "FAIL: expected three pending boots then fallback before kernel" >&2
  exit 1
}

# The persistent FAT32 file must contain the exhausted counter and recovery
# flag written by BootGuard itself, not by any kernel code.
mtype -i "$IMG" ::/boot/boot_state.cfg > "$TMP/final_state"
grep -q 'BOOT_ATTEMPTS=3' "$TMP/final_state"
grep -q 'BOOTGUARD_FALLBACK=1' "$TMP/final_state"
echo "BOOTGUARD OVMF PASS: B B B A without running the ELF"
