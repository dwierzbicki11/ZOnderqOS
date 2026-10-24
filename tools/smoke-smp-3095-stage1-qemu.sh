#!/usr/bin/env bash
# Diskless real-QEMU Cosmos 3.0.95 Limine MP discovery (Stage 1 only).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ISO="${1:?usage: smoke-smp-3095-stage1-qemu.sh ISO LOG CPUS}"
LOG="${2:?missing serial log path}"
CPUS="${3:?missing expected CPU count}"
case "$CPUS" in
  1) TOPOLOGY=1 ;;
  2) TOPOLOGY=sockets=1,cores=1,threads=2 ;;
  4) TOPOLOGY=sockets=1,cores=2,threads=2 ;;
  8) TOPOLOGY=sockets=1,cores=4,threads=2 ;;
  *) echo "[SMP3095-STAGE1][FAIL] supported CPUs: 1 2 4 8" >&2; exit 2 ;;
esac
[[ -s "$ISO" ]] || { echo "Missing probe ISO: $ISO" >&2; exit 2; }
gate_flags=()
if [[ "${ZONDERQ_3095_REQUIRE_DENSE:-false}" == true ]]; then
  gate_flags=(--require-dense)
fi
command -v qemu-system-x86_64 >/dev/null || { echo "QEMU not installed" >&2; exit 2; }
mkdir -p "$(dirname "$LOG")"
: > "$LOG"
qemu-system-x86_64 \
  -machine q35 -cpu Nehalem -smp "$TOPOLOGY" -m 2048M \
  -boot d -cdrom "$ISO" -nic none -vga std \
  -display none -monitor none -serial "file:$LOG" \
  -no-reboot -no-shutdown 2>"$LOG.stderr" &
pid=$!
cleanup() {
  kill "$pid" 2>/dev/null || true
  wait "$pid" 2>/dev/null || true
}
trap cleanup EXIT INT TERM

deadline=$((SECONDS + 150))
while (( SECONDS < deadline )); do
  if ! kill -0 "$pid" 2>/dev/null; then
    cat "$LOG.stderr" >&2 || true
    tail -n 80 "$LOG" >&2 || true
    echo "[SMP3095-STAGE1][FAIL] QEMU exited before stable proof" >&2
    exit 1
  fi
  if grep -Eqi 'kernel panic|fatal:[[:space:]]*exception|triple.fault|page fault' "$LOG"; then
    tail -n 80 "$LOG" >&2
    echo "[SMP3095-STAGE1][FAIL] kernel exception in guest serial" >&2
    exit 1
  fi
  if grep -Fq '[Kernel] Calling Run()...' "$LOG"; then
    python3 "$ROOT/tests/verify_3095_native_stage1.py" "$LOG" --cpus "$CPUS" "${gate_flags[@]}"
    sleep 5
    kill -0 "$pid" 2>/dev/null || { echo "[SMP3095-STAGE1][FAIL] guest exited" >&2; exit 1; }
    python3 "$ROOT/tests/verify_3095_native_stage1.py" "$LOG" --cpus "$CPUS" "${gate_flags[@]}"
    echo "[SMP3095-NATIVE-QEMU] PASS verified topology and 5s stability; APs NOT started"
    exit 0
  fi
  sleep 1
done
tail -n 100 "$LOG" >&2 || true
echo "[SMP3095-STAGE1][FAIL] no main loop in QEMU before timeout" >&2
exit 1
