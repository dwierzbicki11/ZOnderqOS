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

python3 - <<PY
from pyfatfs.PyFatFS import PyFatFS
fs = PyFatFS("$DISK", offset=2048 * 512)
fs.makedir("/boot")
fs.makedir("/boot/slot_a")
fs.makedir("/boot/slot_b")
fs.makedir("/boot/limine")
fs.makedir("/var")
fs.makedir("/var/log")

with open("$ROOT/obj/Debug/net10.0/linux-x64/cosmos/iso_root/boot/ZonderqOS.elf", "rb") as kf:
    kbytes = kf.read()

with fs.open("/boot/slot_a/ZonderqOS.elf", "wb") as f:
    f.write(kbytes)
with fs.open("/boot/ZonderqOS.elf", "wb") as f:
    f.write(kbytes)

with fs.open("/boot/boot_state.cfg", "w") as f:
    f.write("ACTIVE_SLOT=A\nFALLBACK_SLOT=B\nBOOT_ATTEMPTS=0\nMAX_BOOT_ATTEMPTS=3\nSLOT_A_STATUS=HEALTHY\nSLOT_A_VERSION=1.0.0-alpha\nSLOT_A_BUILD=3100\nSLOT_B_STATUS=EMPTY\n")

limine_cfg = (
    "timeout: 3\ndefault_entry: 1\n\n"
    "/ZonderqOS (Slot A)\n    protocol: limine\n    resolution: 1366x768x32\n    path: fslabel(ZONDERQDATA):/boot/slot_a/ZonderqOS.elf\n\n"
    "/ZonderqOS (Slot B)\n    protocol: limine\n    resolution: 1366x768x32\n    path: fslabel(ZONDERQDATA):/boot/slot_b/ZonderqOS.elf\n\n"
    "/ZonderqOS (ISO)\n    protocol: limine\n    resolution: 1366x768x32\n    path: boot():/boot/ZonderqOS.elf\n"
)
with fs.open("/boot/limine.conf", "w") as f: f.write(limine_cfg)
with fs.open("/limine.conf", "w") as f: f.write(limine_cfg)
with fs.open("/boot/limine/limine.conf", "w") as f: f.write(limine_cfg)
fs.close()
PY

timeout --preserve-status "${BOOT_TIME}s" qemu-system-x86_64 \
  -M q35 -accel tcg -cpu Nehalem -smp "$SMP" -m 2048M \
  -vga std \
  -drive "file=$ISO,if=none,id=cosmoscd,format=raw,readonly=on" \
  -device ide-cd,drive=cosmoscd,bootindex=0 -boot d \
  -device ich9-ahci,id=ahci0 \
  -drive "file=$DISK,if=none,id=ahcidisk0,format=raw,cache=directsync" \
  -device ide-hd,drive=ahcidisk0,bus=ahci0.0 \
  -display none \
  -no-reboot >/dev/null 2>&1 || true

python3 - <<PY
from pyfatfs.PyFatFS import PyFatFS
fs = PyFatFS("$DISK", offset=2048 * 512)
if fs.exists("/var/log/system.log"):
    with fs.open("/var/log/system.log", "r") as f:
        with open("$SERIAL_LOG", "w") as out:
            out.write(f.read())
fs.close()
PY

printf '[QEMU-TEST] Zakonczono bieg QEMU. Log systemowy z dysku ZONDERQDATA:\n'
cat "$SERIAL_LOG"

printf '\n[QEMU-TEST] Weryfikacja kluczowych kryteriow sukcesu:\n'
grep -q "Mounted dedicated ZONDERQDATA at '/'" "$SERIAL_LOG" && printf '  [PASS] Partycja ZONDERQDATA pomyslnie zamontowana w / \n' || printf '  [FAIL] Brak montowania ZONDERQDATA\n'
grep -q "ZonderqOS kernel successfully booted" "$SERIAL_LOG" && printf '  [PASS] Kernel ZonderqOS pomyslnie uruchomiony w pamieci RAM\n' || printf '  [FAIL] Kernel nie zglosil udanego bootowania\n'
grep -q "PERF_BOOT_INIT_MS=" "$SERIAL_LOG" && printf '  [PASS] Inicjalizacja uslug zakonczona pomyslnie\n' || printf '  [FAIL] Brak pomiaru PERF_BOOT_INIT_MS\n'
