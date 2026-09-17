#!/usr/bin/env bash
set -euo pipefail

ISO="${1:-output-x64/ZonderqOS.iso}"
LOG="${2:-stage02-qemu-serial.log}"
CPUS="${STAGE02_QEMU_CPUS:-8}"
CORES="${STAGE02_QEMU_CORES:-4}"
THREADS="${STAGE02_QEMU_THREADS:-2}"
TIMEOUT="${STAGE02_QEMU_TIMEOUT_SECONDS:-75}"

command -v qemu-system-x86_64 >/dev/null || { echo '[SMT-QEMU][FAIL] qemu-system-x86_64 not found' >&2; exit 1; }
[[ -f "$ISO" ]] || { echo "[SMT-QEMU][FAIL] ISO not found: $ISO" >&2; exit 1; }
[[ "$TIMEOUT" =~ ^[0-9]+$ ]] || { echo '[SMT-QEMU][FAIL] invalid timeout' >&2; exit 1; }
: > "$LOG"

qemu-system-x86_64 \
  -M q35 -cpu qemu64 \
  -smp "cpus=$CPUS,sockets=1,cores=$CORES,threads=$THREADS" \
  -m 2G \
  -drive "file=$ISO,media=cdrom,if=ide,readonly=on" -boot d \
  -display none -monitor none -serial "file:$LOG" \
  -no-reboot -no-shutdown &
QEMU_PID=$!
trap 'kill "$QEMU_PID" 2>/dev/null || true; wait "$QEMU_PID" 2>/dev/null || true' EXIT

for ((second=0; second<TIMEOUT; second++)); do
  if grep -Eq 'CPU EXCEPTION|System halted|Kernel Panic|\bPANIC\b|Unhandled Exception|Triple fault' "$LOG"; then
    echo '[SMT-QEMU][FAIL] kernel failure detected' >&2
    cat "$LOG" >&2
    exit 1
  fi
  if grep -Fq '[SMP] WARNING:' "$LOG"; then
    echo '[SMT-QEMU][FAIL] SMP warning detected' >&2
    cat "$LOG" >&2
    exit 1
  fi
  if grep -Fq '[SMP] Stage 2 complete: dense CPU map ready; APs remain parked.' "$LOG"; then
    grep -Fq "[SMP] Dense CPU map: $CPUS logical CPU(s)" "$LOG"
    map_count="$(grep -Fc '[SMP] CpuId[' "$LOG" || true)"
    [[ "$map_count" -eq "$CPUS" ]] || { echo "[SMT-QEMU][FAIL] expected $CPUS dense map entries, got $map_count" >&2; cat "$LOG" >&2; exit 1; }
    for ((id=0; id<CPUS; id++)); do
      grep -Fq "[SMP] CpuId[$id]" "$LOG" || { echo "[SMT-QEMU][FAIL] missing dense CpuId $id" >&2; cat "$LOG" >&2; exit 1; }
    done
    grep -Fq '[SMP] CpuId[0]' "$LOG"
    grep -F '[SMP] CpuId[0]' "$LOG" | grep -Fq ' BSP'
    echo "[SMT-QEMU][OK] dense CpuId 0..$((CPUS-1)) verified for ${CORES}C/${CPUS}T."
    exit 0
  fi
  if ! kill -0 "$QEMU_PID" 2>/dev/null; then
    echo '[SMT-QEMU][FAIL] QEMU exited before Stage 2 marker' >&2
    cat "$LOG" >&2
    exit 1
  fi
  sleep 1
done

echo "[SMT-QEMU][FAIL] timeout after ${TIMEOUT}s" >&2
cat "$LOG" >&2
exit 1
