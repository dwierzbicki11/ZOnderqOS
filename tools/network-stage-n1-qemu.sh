#!/usr/bin/env bash
set -euo pipefail

ISO="${ZONDERQ_ISO:-}"
if [[ -z "$ISO" ]]; then
  ISO="$(find bin -type f -name 'ZonderqOS.iso' -o -name '*.iso' | head -n 1 || true)"
fi
if [[ -z "$ISO" || ! -f "$ISO" ]]; then
  echo '[NETWORK-N1][FAIL] x64 ISO not found.' >&2
  exit 1
fi

LOG="${ZONDERQ_NETWORK_N1_LOG:-network-n1-qemu.log}"
rm -f "$LOG"

# rtl8139 is intentionally explicit: N1 is not complete until this exact QEMU
# NIC is both supported by the kernel and produces runtime MAC/link evidence.
# User-mode networking avoids requiring privileged TAP configuration in CI.
set +e
timeout 35s qemu-system-x86_64 \
  -m 512M \
  -smp 2 \
  -cdrom "$ISO" \
  -boot d \
  -display none \
  -serial stdio \
  -no-reboot \
  -netdev user,id=net0 \
  -device rtl8139,netdev=net0 2>&1 | tee "$LOG"
qemu_rc=${PIPESTATUS[0]}
set -e

if [[ $qemu_rc -ne 0 && $qemu_rc -ne 124 ]]; then
  echo "[NETWORK-N1][FAIL] QEMU exited unexpectedly: $qemu_rc" >&2
  exit "$qemu_rc"
fi

# These are runtime evidence requirements, not injected PASS markers. Until
# the kernel emits both facts from the actual NIC path this stage MUST fail.
grep -Eiq '(rtl8139|realtek.*8139|network.*device|nic.*(found|detected|enumerat))' "$LOG" || {
  echo '[NETWORK-N1][FAIL] no supported NIC enumeration evidence in QEMU log.' >&2
  exit 1
}
grep -Eiq '([[:xdigit:]]{2}:){5}[[:xdigit:]]{2}' "$LOG" || {
  echo '[NETWORK-N1][FAIL] no runtime MAC-address evidence in QEMU log.' >&2
  exit 1
}
grep -Eiq '(link[ =:-]*(up|ready|connected)|network.*(ready|online))' "$LOG" || {
  echo '[NETWORK-N1][FAIL] no runtime link-up evidence in QEMU log.' >&2
  exit 1
}

echo '[NETWORK-N1] NIC enumeration, MAC and link evidence observed.'
