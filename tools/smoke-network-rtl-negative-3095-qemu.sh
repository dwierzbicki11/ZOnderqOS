#!/usr/bin/env bash
# Negative QEMU gate: with -nic none the isolated RTL probe MUST decline.
# This is NOT physical Realtek proof and does not validate text framebuffer.
set -euo pipefail
ISO="${1:?usage: smoke-network-rtl-negative-3095-qemu.sh ISO SERIAL-LOG}"
LOG="${2:?usage: smoke-network-rtl-negative-3095-qemu.sh ISO SERIAL-LOG}"
[[ -s "$ISO" ]] || { echo "[NET3095-RTL-NEG][FAIL] Missing ISO" >&2; exit 1; }
command -v qemu-system-x86_64 >/dev/null || exit 1
[[ ! -e "$LOG" && ! -L "$LOG" && ! -e "$LOG.stderr" && ! -L "$LOG.stderr" ]] || {
  echo "[NET3095-RTL-NEG][FAIL] Refusing existing/symlinked output" >&2; exit 1;
}
mkdir -p "$(dirname "$LOG")"
: > "$LOG"
pid=""
cleanup() {
  if [[ -n "$pid" ]]; then
    kill "$pid" 2>/dev/null || true
    wait "$pid" 2>/dev/null || true
  fi
}
trap cleanup EXIT INT TERM
qemu-system-x86_64 \
  -machine q35 -cpu Nehalem -smp 1 -m 2048M \
  -boot d -cdrom "$ISO" -nic none \
  -vga std -display none -monitor none \
  -serial "file:$LOG" -no-reboot -no-shutdown \
  2> "$LOG.stderr" &
pid=$!
ready=0
for ((attempt=0; attempt<150; attempt++)); do
  if grep -Fq '[NET3095-RTL810XE] HEARTBEAT=2' "$LOG"; then
    ready=1
    break
  fi
  if ! kill -0 "$pid" 2>/dev/null; then
    echo "[NET3095-RTL-NEG][FAIL] QEMU exited before second heartbeat" >&2
    cat "$LOG.stderr" >&2
    exit 1
  fi
  sleep 1
done
[[ "$ready" == 1 ]] || {
  echo "[NET3095-RTL-NEG][FAIL] Timed out waiting for guest heartbeat" >&2
  exit 1
}
grep -Fq '[NET3095-RTL810XE] BOOT read-only physical probe' "$LOG"
grep -Fq '[NET3095-RTL810XE][FAIL] no unique valid MMIO snapshot; offers=0 samples=0 reason=no matching PCI probe offer' "$LOG"
if grep -Eq 'MMIO_SNAPSHOT=READ|SNAPSHOT=PASS|kernel panic|Exception 14|CR2=|#PF' "$LOG"; then
  echo "[NET3095-RTL-NEG][FAIL] False NIC proof or guest fault" >&2
  exit 1
fi
echo "[NET3095-RTL-NEG] PASS real QEMU no-NIC fail-closed boot with heartbeat; NOT physical RTL or screen proof"
