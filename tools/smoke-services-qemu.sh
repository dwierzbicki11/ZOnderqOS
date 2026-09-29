#!/usr/bin/env bash
set -euo pipefail

ISO="${1:-output-x64/ZonderqOS.iso}"
OUT="${2:-services-qemu-output}"
BOOT_SECONDS="${SERVICE_QEMU_BOOT_SECONDS:-100}"

fail() { echo "[SERVICES-QEMU][FAIL] $*" >&2; exit 1; }
for tool in qemu-system-x86_64 mkfs.vfat sfdisk mtype mcopy python3; do
  command -v "$tool" >/dev/null || fail "missing $tool"
done
[[ -s "$ISO" ]] || fail "missing ISO: $ISO"
[[ "$BOOT_SECONDS" =~ ^[1-9][0-9]*$ ]] || fail "invalid boot duration: $BOOT_SECONDS"
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
ISO="$(realpath "$ISO")"
DISK="$OUT/services-fat.img"
[[ ! -e "$DISK" ]] || fail "output disk already exists: $DISK"

# A real MBR partition matters here: ZonderqOS mounts VFS partition 0, not a
# FAT filesystem placed directly on an otherwise unpartitioned block device.
truncate -s 256M "$DISK"
printf 'label: dos\nunit: sectors\nstart=2048, type=c\n' | sfdisk "$DISK" > "$OUT/partition.log"
mkfs.vfat -I -F 32 -h 2048 --offset 2048 "$DISK" > "$OUT/mkfs.log"
MTOOLS_IMAGE="$DISK@@1048576"
QEMU_PID=""

cleanup() {
  if [[ -n "$QEMU_PID" ]]; then
    kill "$QEMU_PID" 2>/dev/null || true
    wait "$QEMU_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT

boot_guest() {
  local phase="$1"
  local socket="$OUT/$phase.qmp"
  local serial="$OUT/$phase.serial.log"
  local stderr="$OUT/$phase.stderr.log"
  rm -f "$socket"
  : > "$serial"
  : > "$stderr"
  qemu-system-x86_64 \
    -M q35 -accel tcg -cpu Nehalem -smp 1 -m 2G \
    -drive "file=$ISO,if=none,id=cosmoscd,format=raw,readonly=on" \
    -device ide-cd,drive=cosmoscd,bootindex=0 -boot d \
    -device ich9-ahci,id=ahci0 \
    -drive "file=$DISK,if=none,id=ahcidisk0,format=raw,cache=directsync" \
    -device ide-hd,drive=ahcidisk0,bus=ahci0.0 \
    -display none -monitor none -serial "file:$serial" \
    -qmp "unix:$socket,server=on,wait=off" \
    -no-reboot -no-shutdown 2> "$stderr" &
  QEMU_PID=$!

  local elapsed
  for ((elapsed=0;elapsed<BOOT_SECONDS;elapsed++)); do
    kill -0 "$QEMU_PID" 2>/dev/null || fail "$phase: QEMU exited early; see $stderr"
    sleep 1
  done

  # QMP quit closes the emulated AHCI disk and flushes its writes before mtools
  # reads the image. Never inspect or change the FAT filesystem while QEMU runs.
  python3 - "$socket" <<'PY'
import json
import socket
import sys

with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as connection:
    connection.settimeout(10)
    connection.connect(sys.argv[1])
    channel = connection.makefile('rwb', buffering=0)
    greeting = json.loads(channel.readline())
    if 'QMP' not in greeting:
        raise SystemExit('QMP greeting missing')
    channel.write(b'{"execute":"qmp_capabilities"}\r\n')
    while 'return' not in json.loads(channel.readline()):
        pass
    channel.write(b'{"execute":"quit"}\r\n')
PY
  wait "$QEMU_PID" || fail "$phase: QEMU quit failed"
  QEMU_PID=""
  echo "[SERVICES-QEMU] $phase: QEMU shut down after ${BOOT_SECONDS}s"
}

read_guest() {
  mtype -i "$MTOOLS_IMAGE" "::$1"
}

boot_guest first
read_guest /etc/zservices/heartbeat.conf > "$OUT/heartbeat-first.conf" || fail 'first boot did not persist heartbeat.conf'
read_guest /var/log/zservices/heartbeat.log > "$OUT/heartbeat-first.log" || fail 'first boot did not persist the heartbeat log'
read_guest /var/log/system.log > "$OUT/system-first.log" || fail 'first boot did not persist the system log'
grep -q '^enabled=true$' "$OUT/heartbeat-first.conf" || fail 'first boot did not create enabled heartbeat config'
grep -q 'heartbeat' "$OUT/heartbeat-first.log" || fail 'first boot did not run heartbeat'
grep -q 'ZonderqOS kernel successfully booted' "$OUT/system-first.log" || fail 'first boot did not finish kernel setup'

# Change the on-disk definition with QEMU stopped. The second boot must load it
# and append memory samples to the same persistent service log.
printf 'type=memory\nenabled=true\ninterval_seconds=5\n' > "$OUT/heartbeat-second.conf"
mcopy -o -i "$MTOOLS_IMAGE" "$OUT/heartbeat-second.conf" ::/etc/zservices/heartbeat.conf
boot_guest second
read_guest /etc/zservices/heartbeat.conf > "$OUT/heartbeat-after.conf" || fail 'second boot lost service configuration'
read_guest /var/log/zservices/heartbeat.log > "$OUT/heartbeat-after.log" || fail 'second boot lost the service log'
read_guest /var/log/system.log > "$OUT/system-after.log" || fail 'second boot lost the system log'
grep -q '^type=memory$' "$OUT/heartbeat-after.conf" || fail 'second boot replaced custom configuration'
grep -q 'heartbeat' "$OUT/heartbeat-after.log" || fail 'first boot log was not retained'
grep -q 'free_pages=' "$OUT/heartbeat-after.log" || fail 'second boot did not run the memory service'
first_boots="$(grep -c 'ZonderqOS kernel successfully booted' "$OUT/system-first.log")"
second_boots="$(grep -c 'ZonderqOS kernel successfully booted' "$OUT/system-after.log")"
(( second_boots > first_boots )) || fail 'second boot did not finish kernel setup'

echo '[SERVICES-QEMU][PASS] Two real boots reused one FAT partition; /etc config and /var/log persisted.'
