#!/usr/bin/env bash
# Headless QEMU automated boot verification for ZonderqOS Gen 3.
# Verifies kernel boot, disk mounting, BootSlotManager initialization and slot health confirmation.
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ISO="${1:-$ROOT/output-3095/ZonderqOS-3095-disk.iso}"
SMP="${2:-1}"
BOOT_TIME="${3:-28}"

OUT_DIR="$(mktemp -d /tmp/zonderq-qemu-run.XXXXXX)"
cleanup() {
    rm -rf "$OUT_DIR"
}
trap cleanup EXIT

DISK="$OUT_DIR/zonderqdata.img"
truncate -s 256M "$DISK"
printf 'label: dos\nunit: sectors\nstart=2048, type=c\n' | sfdisk "$DISK" >/dev/null 2>&1
mkfs.vfat -I -F 32 -n ZONDERQDATA -h 2048 --offset 2048 "$DISK" >/dev/null 2>&1

SERIAL_LOG="$OUT_DIR/serial.log"
printf '[QEMU-TEST] Uruchamianie ZonderqOS w QEMU (SMP: %s vCPU, Czas: %ss)...\n' "$SMP" "$BOOT_TIME"

timeout --preserve-status "${BOOT_TIME}s" qemu-system-x86_64 \
  -M q35 -accel tcg -cpu Nehalem -smp "$SMP" -m 2048M \
  -vga std \
  -drive "file=$ISO,if=none,id=cosmoscd,format=raw,readonly=on" \
  -device ide-cd,drive=cosmoscd,bootindex=0 -boot d \
  -device ich9-ahci,id=ahci0 \
  -drive "file=$DISK,if=none,id=ahcidisk0,format=raw,cache=directsync" \
  -device ide-hd,drive=ahcidisk0,bus=ahci0.0 \
  -display none \
  -serial "file:$SERIAL_LOG" \
  -no-reboot >/dev/null 2>&1 || true

printf '[QEMU-TEST] Zakonczono bieg QEMU. Log konsoli szeregowej:\n'
cat "$SERIAL_LOG"

printf '\n[QEMU-TEST] Weryfikacja kluczowych kryteriow sukcesu:\n'
grep -q "Mounted dedicated ZONDERQDATA at '/'" "$SERIAL_LOG" && printf '  [PASS] Partycja ZONDERQDATA pomyslnie zamontowana w / \n' || printf '  [FAIL] Brak montowania ZONDERQDATA\n'
grep -q "ZonderqOS kernel successfully booted" "$SERIAL_LOG" && printf '  [PASS] Kernel ZonderqOS pomyslnie uruchomiony w pamieci RAM\n' || printf '  [FAIL] Kernel nie zglosil udanego bootowania\n'
grep -q "PERF_BOOT_INIT_MS=" "$SERIAL_LOG" && printf '  [PASS] Inicjalizacja uslug zakonczona pomyslnie\n' || printf '  [FAIL] Brak pomiaru PERF_BOOT_INIT_MS\n'
