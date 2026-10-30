#!/usr/bin/env bash
# Real E1000E link smoke: does not claim DHCP, TCP, internet or SMP proof.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ISO="${1:?usage: smoke-network-n1-3095-qemu.sh ISO SERIAL-LOG}"
LOG="${2:?usage: smoke-network-n1-3095-qemu.sh ISO SERIAL-LOG}"
TIMEOUT="${NET3095_N1_TIMEOUT:-150}"
PCAP="${NET3095_N1_PCAP:-$LOG.pcap}"
MODE="${3:-n1}"
[[ "$MODE" == n1 || "$MODE" == n2 || "$MODE" == n3 ]] || {
  echo "[NET3095-N1][FAIL] Expected n1, n2 or n3 mode" >&2; exit 2;
}
[[ "$TIMEOUT" =~ ^[1-9][0-9]*$ ]] && (( TIMEOUT <= 600 )) || {
  echo "[NET3095-N1][FAIL] Invalid timeout" >&2; exit 1;
}
[[ -s "$ISO" ]] || { echo "[NET3095-N1][FAIL] Missing ISO" >&2; exit 1; }
command -v qemu-system-x86_64 >/dev/null || exit 1
# Fail closed: do not follow symlinks or erase an existing capture.
[[ ! -L "$LOG" && ! -L "$LOG.stderr" ]] || {
  echo "[NET3095-N1][FAIL] Refusing symlinked serial output" >&2; exit 1;
}
[[ ! -e "$PCAP" && ! -L "$PCAP" ]] || {
  echo "[NET3095-N1][FAIL] Refusing to overwrite existing PCAP" >&2; exit 1;
}
mkdir -p "$(dirname "$LOG")" "$(dirname "$PCAP")"
: > "$LOG"
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
qemu_alive() {
  kill -0 "$pid" 2>/dev/null || return 1
  local state
  state="$(ps -o stat= -p "$pid" 2>/dev/null)" || return 1
  [[ -n "$state" && "$state" != Z* ]]
}

started=$SECONDS
while (( SECONDS - started < TIMEOUT )); do
  if ! qemu_alive; then
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
    # TX may leave the descriptor ring after the guest has logged QUEUED.
    # Wait for actual QEMU PCAP evidence, never count QUEUED as transmitted.
    capture_start=$SECONDS
    until python3 "$ROOT/tests/verify_3095_network_n1.py" "$LOG" "$PCAP" >/dev/null 2>&1; do
      qemu_alive || {
        echo "[NET3095-N1][FAIL] Guest exited before TX capture" >&2; exit 1;
      }
      if (( SECONDS - capture_start >= 12 )); then
        python3 "$ROOT/tests/verify_3095_network_n1.py" "$LOG" "$PCAP"
        echo "[NET3095-N1][FAIL] TX capture missing or malformed" >&2
        exit 1
      fi
      sleep 1
    done
    python3 "$ROOT/tests/verify_3095_network_n1.py" "$LOG" "$PCAP"
    if [[ "$MODE" == n2 || "$MODE" == n3 ]]; then
      # N2 requires genuine inbound DHCP OFFER/ACK and a guest-applied lease.
      dhcp_start=$SECONDS
      until python3 "$ROOT/tests/verify_3095_network_n2.py" "$LOG" "$PCAP" >/dev/null 2>&1; do
        qemu_alive || {
          echo "[NET3095-N2][FAIL] Guest exited before DHCP RX proof" >&2; exit 1;
        }
        if grep -Fq '[NET3095-N2][FAIL]' "$LOG"; then
          tail -n 90 "$LOG" >&2
          echo "[NET3095-N2][FAIL] Guest reported DHCP failure" >&2
          exit 1
        fi
        if (( SECONDS - dhcp_start >= 20 )); then
          python3 "$ROOT/tests/verify_3095_network_n2.py" "$LOG" "$PCAP"
          echo "[NET3095-N2][FAIL] No correlated DHCP RX/TX capture" >&2
          exit 1
        fi
        sleep 1
      done
      python3 "$ROOT/tests/verify_3095_network_n2.py" "$LOG" "$PCAP"
    fi
    if [[ "$MODE" == n3 ]]; then
      icmp_start=$SECONDS
      until python3 "$ROOT/tests/verify_3095_network_n3.py" "$LOG" "$PCAP" >/dev/null 2>&1; do
        qemu_alive || {
          echo "[NET3095-N3][FAIL] Guest exited before ICMP RX proof" >&2; exit 1;
        }
        if grep -Fq '[NET3095-N3][FAIL]' "$LOG"; then
          tail -n 90 "$LOG" >&2
          echo "[NET3095-N3][FAIL] Guest reported ICMP failure" >&2
          exit 1
        fi
        if (( SECONDS - icmp_start >= 15 )); then
          python3 "$ROOT/tests/verify_3095_network_n3.py" "$LOG" "$PCAP"
          echo "[NET3095-N3][FAIL] No correlated ICMP echo RX/TX capture" >&2
          exit 1
        fi
        sleep 1
      done
      python3 "$ROOT/tests/verify_3095_network_n3.py" "$LOG" "$PCAP"
    fi
    sleep 8
    qemu_alive || {
      echo "[NET3095-N1][FAIL] Guest exited after TX proof" >&2; exit 1;
    }
    python3 "$ROOT/tests/verify_3095_network_n1.py" "$LOG" "$PCAP"
    if [[ "$MODE" == n3 ]]; then
      python3 "$ROOT/tests/verify_3095_network_n3.py" "$LOG" "$PCAP"
      echo "[NET3095-N3-QEMU] PASS real e1000e DHCP RX + ICMP echo RX/TX + 8s stability; production networking disabled"
    elif [[ "$MODE" == n2 ]]; then
      python3 "$ROOT/tests/verify_3095_network_n2.py" "$LOG" "$PCAP"
      echo "[NET3095-N2-QEMU] PASS real e1000e RX via DHCP DORA + QEMU PCAP + 8s stability; production networking disabled"
    else
      echo "[NET3095-N1-QEMU] PASS real e1000e link + QEMU PCAP raw TX + 8s stability; RX/DHCP unproven"
    fi
    exit 0
  fi
  sleep 1
done
tail -n 100 "$LOG" >&2
echo "[NET3095-N1][FAIL] QEMU did not produce verified NIC/link proof in $TIMEOUT seconds" >&2
exit 1
