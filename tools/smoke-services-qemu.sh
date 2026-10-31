#!/usr/bin/env bash
set -euo pipefail

ISO="${1:-output-x64/ZonderqOS.iso}"
OUT="${2:-services-qemu-output}"
BOOT_SECONDS="${SERVICE_QEMU_BOOT_SECONDS:-100}"
CAPTURE_SCREEN="${SERVICE_QEMU_CAPTURE_SCREEN:-0}"
AUTOMATE_GUI_LOGIN="${SERVICE_QEMU_AUTOMATE_GUI_LOGIN:-0}"
PROBE_FILE_MANAGER="${SERVICE_QEMU_PROBE_FILE_MANAGER:-0}"
PROBE_TASK_MANAGER="${SERVICE_QEMU_PROBE_TASK_MANAGER:-0}"
EXPECTED_FRAMEBUFFER="${SERVICE_QEMU_EXPECT_FRAMEBUFFER:-}"

fail() { echo "[SERVICES-QEMU][FAIL] $*" >&2; exit 1; }
for tool in qemu-system-x86_64 mkfs.vfat sfdisk mtype mcopy python3; do
  command -v "$tool" >/dev/null || fail "missing $tool"
done
[[ -s "$ISO" ]] || fail "missing ISO: $ISO"
[[ "$BOOT_SECONDS" =~ ^[1-9][0-9]*$ ]] || fail "invalid boot duration: $BOOT_SECONDS"
[[ "$CAPTURE_SCREEN" == 0 || "$CAPTURE_SCREEN" == 1 ]] || fail "invalid capture flag: $CAPTURE_SCREEN"
[[ "$AUTOMATE_GUI_LOGIN" == 0 || "$AUTOMATE_GUI_LOGIN" == 1 ]] || fail "invalid GUI login flag"
[[ "$AUTOMATE_GUI_LOGIN" == 0 || "$CAPTURE_SCREEN" == 1 ]] || fail "GUI login probe requires framebuffer capture"
[[ "$PROBE_FILE_MANAGER" == 0 || "$PROBE_FILE_MANAGER" == 1 ]] || fail "invalid File Manager probe flag"
[[ "$PROBE_FILE_MANAGER" == 0 || "$AUTOMATE_GUI_LOGIN" == 1 ]] || fail "File Manager probe requires synthetic GUI login"
[[ "$PROBE_TASK_MANAGER" == 0 || "$PROBE_TASK_MANAGER" == 1 ]] || fail "invalid Task Manager probe flag"
[[ "$PROBE_TASK_MANAGER" == 0 || "$PROBE_FILE_MANAGER" == 1 ]] || fail "Task Manager probe requires completed File Manager probe"
[[ "$PROBE_TASK_MANAGER" == 0 || "$BOOT_SECONDS" -ge 84 ]] || fail "Task Manager probe requires at least 84 boot seconds"
[[ -z "$EXPECTED_FRAMEBUFFER" || "$EXPECTED_FRAMEBUFFER" =~ ^[1-9][0-9]*x[1-9][0-9]*$ ]] || fail "expected framebuffer must be WIDTHxHEIGHT"
[[ -z "$EXPECTED_FRAMEBUFFER" || "$CAPTURE_SCREEN" == 1 ]] || fail "framebuffer expectation requires screenshot capture"
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
ISO="$(realpath "$ISO")"
DISK="$OUT/services-fat.img"
[[ ! -e "$DISK" ]] || fail "output disk already exists: $DISK"

# A real MBR partition matters here: ZonderqOS mounts VFS partition 0, not a
# FAT filesystem placed directly on an otherwise unpartitioned block device.
truncate -s 256M "$DISK"
printf 'label: dos\nunit: sectors\nstart=2048, type=c\n' | sfdisk "$DISK" > "$OUT/partition.log"
# Exactly the dedicated FAT32 volume label required by DataVolumePolicy.
mkfs.vfat -I -F 32 -n ZONDERQDATA -h 2048 --offset 2048 "$DISK" > "$OUT/mkfs.log"
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
    if [[ "$AUTOMATE_GUI_LOGIN" == 1 && "$elapsed" == 25 ]]; then
      # Only the fresh synthetic FAT32 image is attached; never use a host disk.
      python3 "$(dirname "$0")/qmp-gui-login.py" "$socket" "$phase" \
        > "$OUT/$phase.login.log" 2>&1 || fail "$phase: GUI QMP input failed"
    fi
    if [[ "$PROBE_FILE_MANAGER" == 1 && "$elapsed" == 65 ]]; then
      # The guest uses only the synthetic test image. Capture real framebuffer
      # changes caused by opening File Manager via Alt+Space -> search -> Enter.
      python3 "$(dirname "$0")/qmp-gui-filemanager.py" "$socket" "$OUT/$phase" \
        > "$OUT/$phase.filemanager-probe.log" 2>&1 || fail "$phase: GUI File Manager interaction probe failed"
      # Publish measured framebuffer changes in CI logs, not just artifacts.
      cat "$OUT/$phase.filemanager-probe.log"
    fi
    if [[ "$PROBE_TASK_MANAGER" == 1 && "$elapsed" == 83 ]]; then
      # Read-only Task Manager interactions follow the File Manager close.
      # Both probes run against the same disposable FAT32 guest image.
      python3 "$(dirname "$0")/qmp-gui-taskmanager.py" "$socket" "$OUT/$phase" \
        > "$OUT/$phase.taskmanager-probe.log" 2>&1 || fail "$phase: Task Manager QMP interaction probe failed"
      cat "$OUT/$phase.taskmanager-probe.log"
    fi
    sleep 1
  done

  # QMP quit closes the emulated AHCI disk and flushes its writes before mtools
  # reads the image. Never inspect or change the FAT filesystem while QEMU runs.
  python3 - "$socket" "$OUT/$phase.ppm" "$CAPTURE_SCREEN" <<'PY'
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
    if sys.argv[3] == '1':
        command = {"execute": "screendump", "arguments": {"filename": sys.argv[2]}}
        channel.write((json.dumps(command) + "\r\n").encode())
        while True:
            reply = json.loads(channel.readline())
            if 'error' in reply:
                # Still issue QMP quit so the synthetic FAT32 disk is flushed.
                # The missing screenshot is rejected after QEMU has exited.
                print("QMP screendump failed: " + str(reply['error']), file=sys.stderr)
                break
            if 'return' in reply:
                break
    channel.write(b'{"execute":"quit"}\r\n')
PY
  wait "$QEMU_PID" || fail "$phase: QEMU quit failed"
  QEMU_PID=""
  if [[ "$CAPTURE_SCREEN" == 1 ]]; then
    [[ -s "$OUT/$phase.ppm" ]] || fail "$phase: QMP screendump missing"
    python3 - "$OUT/$phase.ppm" "$EXPECTED_FRAMEBUFFER" <<'PY'
import sys
with open(sys.argv[1], 'rb') as screenshot:
    magic = screenshot.readline().strip()
    if magic != b'P6':
        raise SystemExit('QMP screendump is not a P6 PPM')
    dimensions = screenshot.readline()
    while dimensions.startswith(b'#'):
        dimensions = screenshot.readline()
    width, height = map(int, dimensions.split())
    max_value = int(screenshot.readline().strip())
    if width <= 0 or height <= 0 or max_value != 255:
        raise SystemExit('Invalid QMP framebuffer dimensions')
    pixels = screenshot.read()
    if len(pixels) != width * height * 3:
        raise SystemExit('Incomplete QMP framebuffer dump')
    if sys.argv[2] and f'{width}x{height}' != sys.argv[2]:
        raise SystemExit(f'Framebuffer mode mismatch: requested {sys.argv[2]}, actual {width}x{height}')
    if sys.argv[2]:
        print(f'[SERVICES-QEMU] verified actual framebuffer {width}x{height}')
    # This proves a framebuffer was captured, NOT that GUI interactions work.
    samples = {pixels[i:i+3] for i in range(0, len(pixels), max(3, len(pixels) // 4096 // 3 * 3))}
    print(f'[SERVICES-QEMU] framebuffer capture {width}x{height}, sampled colors={len(samples)}')
PY
  fi
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
if [[ "$PROBE_FILE_MANAGER" == 1 ]]; then
  # Framebuffer changes alone do not prove the intended app launched.
  # The palette records the actual launch in the persistent guest system log.
  first_filemanager_launches="$(grep -cF '[PALETTE] Launched application: File Manager' "$OUT/system-first.log" || true)"
  (( first_filemanager_launches >= 1 )) || fail 'first boot did not log File Manager launch'
fi
if [[ "$PROBE_TASK_MANAGER" == 1 ]]; then
  first_taskmanager_launches="$(grep -cF '[PALETTE] Launched application: Manager zadan' "$OUT/system-first.log" || true)"
  (( first_taskmanager_launches >= 1 )) || fail 'first boot did not log Task Manager launch'
fi

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
if [[ "$PROBE_FILE_MANAGER" == 1 ]]; then
  second_filemanager_launches="$(grep -cF '[PALETTE] Launched application: File Manager' "$OUT/system-after.log" || true)"
  (( second_filemanager_launches > first_filemanager_launches )) ||
    fail 'second boot did not log a new File Manager launch'
  echo '[SERVICES-QEMU] QMP keyboard probe launched File Manager in both real guest sessions.'
fi
if [[ "$PROBE_TASK_MANAGER" == 1 ]]; then
  second_taskmanager_launches="$(grep -cF '[PALETTE] Launched application: Manager zadan' "$OUT/system-after.log" || true)"
  (( second_taskmanager_launches > first_taskmanager_launches )) ||
    fail 'second boot did not log a new Task Manager launch'
  echo '[SERVICES-QEMU] QMP Task Manager launch and tab interactions verified in both guest sessions.'
fi

echo '[SERVICES-QEMU][PASS] Two real boots reused one FAT partition; /etc config and /var/log persisted.'
