#!/usr/bin/env bash
set -euo pipefail

ISO="${1:-output-x64/ZonderqOS.iso}"
LOG="${2:-stage03-qemu-serial.log}"
CPU_MODEL="${STAGE03_QEMU_CPU_MODEL:-Nehalem}"
SOCKETS="${STAGE03_QEMU_SOCKETS:-1}"
CPUS="${STAGE03_QEMU_CPUS:-8}"
CORES="${STAGE03_QEMU_CORES:-4}"
THREADS="${STAGE03_QEMU_THREADS:-2}"
TIMEOUT="${STAGE03_QEMU_TIMEOUT_SECONDS:-90}"

fail() {
  echo "[SMT3-QEMU][FAIL] $*" >&2
  exit 1
}

command -v qemu-system-x86_64 >/dev/null || fail 'qemu-system-x86_64 not found'
[[ -f "$ISO" ]] || fail "ISO not found: $ISO"

for value_name in SOCKETS CPUS CORES THREADS TIMEOUT; do
  value="${!value_name}"
  [[ "$value" =~ ^[1-9][0-9]*$ ]] || fail "$value_name must be a positive integer (got: $value)"
done

EXPECTED_CPUS=$((SOCKETS * CORES * THREADS))
[[ "$CPUS" -eq "$EXPECTED_CPUS" ]] || \
  fail "invalid topology: cpus=$CPUS but sockets*cores*threads=${SOCKETS}*${CORES}*${THREADS}=$EXPECTED_CPUS"

if ! qemu-system-x86_64 -cpu help 2>/dev/null | grep -Eq "(^|[[:space:]])${CPU_MODEL}([[:space:]]|$)"; then
  fail "QEMU CPU model '$CPU_MODEL' is not available on this host"
fi

: > "$LOG"

echo "[SMT3-QEMU] CPU model: $CPU_MODEL"
echo "[SMT3-QEMU] QEMU topology: ${SOCKETS} socket(s) x ${CORES} core(s) x ${THREADS} thread(s)/core = ${CPUS} logical CPU(s)"
echo "[SMT3-QEMU] Waiting for Stage 3 GS storage, dense map and first LAPIC timer tick..."

qemu-system-x86_64 \
  -M q35 \
  -cpu "$CPU_MODEL" \
  -smp "cpus=$CPUS,sockets=$SOCKETS,cores=$CORES,threads=$THREADS" \
  -m 2G \
  -drive "file=$ISO,media=cdrom,if=ide,readonly=on" -boot d \
  -display none -monitor none -serial "file:$LOG" \
  -no-reboot -no-shutdown &
QEMU_PID=$!
trap 'kill "$QEMU_PID" 2>/dev/null || true; wait "$QEMU_PID" 2>/dev/null || true' EXIT

for ((second=0; second<TIMEOUT; second++)); do
  if grep -Eq 'CPU EXCEPTION|System halted|Kernel Panic|\bPANIC\b|Unhandled Exception|Triple fault|Invalid RSP' "$LOG"; then
    echo '[SMT3-QEMU][FAIL] kernel failure detected' >&2
    cat "$LOG" >&2
    exit 1
  fi

  if grep -Fq '[SMP] WARNING:' "$LOG"; then
    echo '[SMT3-QEMU][FAIL] SMP warning detected' >&2
    cat "$LOG" >&2
    exit 1
  fi

  stage3=0
  stage2=0
  timer=0
  grep -Fq '[SMP] Stage 3 complete: BSP CpuId=0, GS per-CPU storage active; APs remain parked.' "$LOG" && stage3=1
  grep -Fq '[SMP] Stage 2 complete: dense CPU map ready; APs remain parked.' "$LOG" && stage2=1
  grep -Fq '[LAPIC] Timer tick 1 ' "$LOG" && timer=1

  if (( stage3 && stage2 && timer )); then
    grep -Fq "[SMP] Dense CPU map: $CPUS logical CPU(s)" "$LOG" || \
      { echo "[SMT3-QEMU][FAIL] kernel did not report $CPUS logical CPUs" >&2; cat "$LOG" >&2; exit 1; }
    grep -Fq '[SMP] Dense CPU map verified: BSP=0, CpuIds are unique and contiguous.' "$LOG" || \
      { echo '[SMT3-QEMU][FAIL] kernel did not verify dense CpuIds' >&2; cat "$LOG" >&2; exit 1; }

    map_count="$(grep -Fc '[SMP] CpuId[' "$LOG" || true)"
    [[ "$map_count" -eq "$CPUS" ]] || {
      echo "[SMT3-QEMU][FAIL] expected $CPUS dense map entries, got $map_count" >&2
      cat "$LOG" >&2
      exit 1
    }

    for ((id=0; id<CPUS; id++)); do
      grep -Fq "[SMP] CpuId[$id]" "$LOG" || {
        echo "[SMT3-QEMU][FAIL] missing dense CpuId $id" >&2
        cat "$LOG" >&2
        exit 1
      }
    done

    if (( THREADS > 1 )); then
      echo "[SMT3-QEMU][OK] QEMU exposes SMT topology: ${SOCKETS}S/${CORES}C/${THREADS}T = ${CPUS} logical CPUs."
    else
      echo "[SMT3-QEMU][OK] QEMU exposes SMP topology: ${SOCKETS}S/${CORES}C = ${CPUS} logical CPUs."
    fi
    echo '[SMT3-QEMU][OK] Stage 3 GS-local IRQ/context-switch storage is active on BSP.'
    echo '[SMT3-QEMU][OK] First LAPIC scheduler timer IRQ returned successfully through the GS-based path.'
    echo '[SMT3-QEMU][OK] APs remain intentionally parked until Stage 4.'
    exit 0
  fi

  if ! kill -0 "$QEMU_PID" 2>/dev/null; then
    echo '[SMT3-QEMU][FAIL] QEMU exited before Stage 3 runtime proof' >&2
    cat "$LOG" >&2
    exit 1
  fi

  sleep 1
done

echo "[SMT3-QEMU][FAIL] timeout after ${TIMEOUT}s" >&2
cat "$LOG" >&2
exit 1
