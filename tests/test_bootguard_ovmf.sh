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
mmd -i "$IMG" ::/boot/slot_a ::/boot/slot_b
# Test fixtures use structurally valid ELF64 headers; they are NEVER executed.
# A real kernel boot through Limine has a separate release gate.
python3 - "$TMP" <<'PY'
from pathlib import Path
import sys
root = Path(sys.argv[1])
elf = bytearray(128)
elf[:7] = b'\x7fELF\x02\x01\x01'
elf[16:18] = (2).to_bytes(2, 'little')
elf[18:20] = (62).to_bytes(2, 'little')
elf[32:40] = (64).to_bytes(8, 'little')
elf[54:56] = (56).to_bytes(2, 'little')
elf[56:58] = (1).to_bytes(2, 'little')
(root / "valid-elf.bin").write_bytes(elf)
(root / "broken-elf.bin").write_bytes(b"not an ELF")
PY
mcopy -i "$IMG" "$TMP/valid-elf.bin" ::/boot/slot_a/ZonderqOS.elf
mcopy -i "$IMG" "$TMP/valid-elf.bin" ::/boot/slot_b/ZonderqOS.elf
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

# Now corrupt slot B on a FRESH four-attempt state and reboot OVMF.
# Firmware must avoid executing B on the FIRST attempt and choose A.
kill "$QPID" 2>/dev/null || true
wait "$QPID" 2>/dev/null || true
QPID=""
mcopy -o -i "$IMG" "$TMP/boot_state.cfg" ::/boot/boot_state.cfg
mcopy -o -i "$IMG" "$TMP/broken-elf.bin" ::/boot/slot_b/ZonderqOS.elf
cp "$VARS" "$TMP/OVMF_VARS_BAD.fd"
BADDEBUG="$TMP/invalid-elf-e9.log"

qemu-system-x86_64 \
  -machine q35 -cpu qemu64 -m 512M \
  -drive if=pflash,format=raw,readonly=on,file="$CODE" \
  -drive if=pflash,format=raw,file="$TMP/OVMF_VARS_BAD.fd" \
  -device qemu-xhci,id=xhci \
  -drive if=none,id=stick,file="$IMG",format=raw \
  -device usb-storage,drive=stick,bus=xhci.0,bootindex=0 \
  -display none -serial none -monitor none \
  -debugcon "file:$BADDEBUG" -global isa-debugcon.iobase=0xe9 \
  >"$TMP/qemu-bad.log" 2>&1 &
QPID=$!

BAD_FOUND=0
for i in $(seq 1 140); do
  if [ -f "$BADDEBUG" ] && grep -q 'ZQBOOT:[AB]' "$BADDEBUG"; then
    BAD_FOUND=1
    break
  fi
  if ! kill -0 "$QPID" 2>/dev/null; then
    echo "Corrupted-ELF EFI boot exited early" >&2
    cat "$TMP/qemu-bad.log" >&2
    exit 1
  fi
  sleep 0.5
done
if [ "$BAD_FOUND" -ne 1 ]; then
  echo "No EFI boot selection for corrupted ELF" >&2
  test ! -f "$BADDEBUG" || cat "$BADDEBUG" >&2
  cat "$TMP/qemu-bad.log" >&2
  exit 1
fi
FIRST="$(grep -o 'ZQBOOT:[AB]' "$BADDEBUG" | head -n 1)"
test "$FIRST" = 'ZQBOOT:A' || {
  echo "FAIL: corrupt slot B selected instead of fallback A: $FIRST" >&2
  exit 1
}
kill "$QPID" 2>/dev/null || true
wait "$QPID" 2>/dev/null || true
QPID=""
mtype -i "$IMG" ::/boot/boot_state.cfg > "$TMP/corrupted-final-state"
grep -q 'BOOTGUARD_FALLBACK=1' "$TMP/corrupted-final-state"
grep -q 'BOOT_ATTEMPTS=0' "$TMP/corrupted-final-state"
echo "BOOTGUARD OVMF PASS: malformed candidate ELF rejected before executing it"

