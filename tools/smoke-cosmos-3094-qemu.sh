#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ISO="${1:?usage: smoke-cosmos-3094-qemu.sh ISO SERIAL-LOG}"
LOG="${2:?usage: smoke-cosmos-3094-qemu.sh ISO SERIAL-LOG}"
CPUS="${SMP3094_QEMU_CPUS:-4}"
CORES="${SMP3094_QEMU_CORES:-$CPUS}"
THREADS="${SMP3094_QEMU_THREADS:-1}"
TIMEOUT="${SMP3094_QEMU_TIMEOUT:-180}"
STABLE="${SMP3094_QEMU_STABLE:-8}"

fail() { echo "[SMP3094-QEMU][FAIL] $*" >&2; exit 1; }
for value in "$CPUS" "$CORES" "$THREADS" "$TIMEOUT" "$STABLE"; do
    [[ "$value" =~ ^[1-9][0-9]*$ ]] || fail "Invalid CPU/timeout value: $value"
done
(( CPUS >= 1 && CPUS <= 256 )) || fail "CPU count out of range"
(( CORES * THREADS == CPUS )) || fail "cores * threads must equal vCPU count"
[[ -s "$ISO" ]] || fail "ISO missing or empty: $ISO"
command -v qemu-system-x86_64 >/dev/null || fail "Missing QEMU x86_64"

mkdir -p "$(dirname "$LOG")"
: > "$LOG"
# Avoid silently reusing a serial file from an earlier, successful boot.
qemu-system-x86_64 -machine q35 -cpu Nehalem \
    -smp "$CPUS,sockets=1,cores=$CORES,threads=$THREADS" \
    -m 1024M -boot d -cdrom "$ISO" \
    -vga std -display none -monitor none \
    -serial "file:$LOG" -no-reboot -no-shutdown \
    2> "$LOG.stderr" &
pid=$!
cleanup() {
    if kill -0 "$pid" 2>/dev/null; then
        kill "$pid" 2>/dev/null || true
    fi
    wait "$pid" 2>/dev/null || true
}
trap cleanup EXIT INT TERM

started=$SECONDS
confirmed=0
while (( SECONDS - started < TIMEOUT )); do
    if ! kill -0 "$pid" 2>/dev/null; then
        fail "QEMU terminated unexpectedly. See $LOG.stderr"
    fi
    # Fail as soon as the kernel explicitly panics, rather than burning
    # the full BOOT timeout while QEMU stays alive in its native panic loop.
    # Preserve a useful serial tail in the GitHub Actions job output.
    if grep -Eq '^KERNEL PANIC|^System halted\.|^\[SMP3094\].*(failed|timeout)' "$LOG"; then
        echo "[SMP3094-QEMU] Guest failure evidence:" >&2
        tail -n 80 "$LOG" >&2
        fail "Guest failed before BOOT; check serial evidence"
    fi
    if grep -Fq '[SMP3094-PROBE] BOOT' "$LOG"; then
        confirmed=1
        break
    fi
    sleep 1
done
(( confirmed )) || fail "Kernel BOOT proof did not arrive within $TIMEOUT seconds"
python3 "$ROOT/tests/Cosmos3094SmpBoot/verify_boot.py" "$LOG" --cpus "$CPUS"

# Verify the guest does not immediately reboot or panic after an initial PASS.
sleep "$STABLE"
python3 "$ROOT/tests/Cosmos3094SmpBoot/verify_boot.py" "$LOG" --cpus "$CPUS"
kill -0 "$pid" 2>/dev/null || fail "QEMU exited during stability window"
echo "[SMP3094-QEMU] Stable boot for $STABLE s after full AP proof"
