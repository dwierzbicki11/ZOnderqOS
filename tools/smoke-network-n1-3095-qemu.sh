#!/usr/bin/env bash
# Real E1000E link smoke: does not claim DHCP, TCP, internet or SMP proof.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ISO="${1:?usage: smoke-network-n1-3095-qemu.sh ISO SERIAL-LOG}"
LOG="${2:?usage: smoke-network-n1-3095-qemu.sh ISO SERIAL-LOG}"
TIMEOUT="${NET3095_N1_TIMEOUT:-150}"
PCAP="${NET3095_N1_PCAP:-$LOG.pcap}"
[[ "$TIMEOUT" =~ ^[1-9][0-9]*$ ]] && (( TIMEOUT <= 600 )) || {
  echo "[NET3095-N1][FAIL] Invalid timeout" >&2; exit 1;
}
[[ -s "$ISO" ]] || { echo "[NET3095-N1][FAIL] Missing ISO" >&2; exit 1; }
command -v qemu-system-x86_64 >/dev/null || exit 1
mkdir -p "$(dirname "$LOG")" "$(dirname "$PCAP")"
: > "$LOG"
rm -f -- "$PCAP"
qemu-system-x86_64 \
  -machine q35 -cpu Nehalem -smp 1 -m 2048M \
  -boot d -cdrom "$ISO" \
  -nic none -netdev user,id=net0 -device e1000e,netdev=net0 \
  -object "filter-dump,id=netdump,netdev=net0,file=$PCAP" \
  -vga std -display none -monitor none \
  -serial "file:$LOG" -no-reboot -no-shutdown \
  2> "$LOG.stderr" &
pid=$!
cleanup() {
  kill "$pid" 2>/dev/null || true
  wait "$pid" 2>/dev/null || true
}
trap cleanup EXIT INT TERM

started=$SECONDS
while (( SECONDS - started < TIMEOUT )); do
  if ! kill -0 "$pid" 2>/dev/null; then
    cat "$LOG.stderr" >&2 || true
    echo "[NET3095-N1][FAIL] Guest process exited before valid NIC proof" >&2
    exit 1
  fi
  if grep -Eqi 'kernel panic|fatal: exception|page fault|\[NET3095-N1\]\[FAIL\]' "$LOG"; then
    tail -n 90 "$LOG" >&2
    echo "[NET3095-N1][FAIL] Kernel or NIC failure before proof" >&2
    exit 1
  fi
  if grep -Fq '[NET3095-N1] DHCP=NOT_ATTEMPTED IP=UNASSIGNED' "$LOG"; then
    python3 "$ROOT/tests/verify_3095_network_n1.py" "$LOG" "$PCAP"
    sleep 8
    kill -0 "$pid" 2>/dev/null || {
      echo "[NET3095-N1][FAIL] Guest exited after initial link proof" >&2; exit 1;
    }
    python3 "$ROOT/tests/verify_3095_network_n1.py" "$LOG" "$PCAP"
    echo "[NET3095-N1-QEMU] PASS real e1000e link + QEMU PCAP raw TX + 8s stability; RX/DHCP unproven"
    exit 0
  fi
  sleep 1
done
tail -n 100 "$LOG" >&2
echo "[NET3095-N1][FAIL] QEMU did not produce verified NIC/link proof in $TIMEOUT seconds" >&2
exit 1
