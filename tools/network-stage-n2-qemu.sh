#!/usr/bin/env bash
set -euo pipefail
ISO="${ZONDERQ_ISO:-}"
[[ -n "$ISO" && -f "$ISO" ]] || { echo '[NETWORK-N2][FAIL] ISO missing' >&2; exit 1; }
LOG="${ZONDERQ_NETWORK_N2_LOG:-network-n2-qemu.log}"
PCAP="${ZONDERQ_NETWORK_N2_PCAP:-network-n2.pcap}"
rm -f "$LOG" "$PCAP"

set +e
timeout 35s qemu-system-x86_64 \
  -m 512M -smp 2 -cdrom "$ISO" -boot d -display none -serial stdio -no-reboot \
  -netdev user,id=net0 -device e1000e,netdev=net0 \
  -object filter-dump,id=n2dump,netdev=net0,file="$PCAP" 2>&1 | tee "$LOG"
qemu_rc=${PIPESTATUS[0]}
set -e
[[ $qemu_rc -eq 0 || $qemu_rc -eq 124 ]] || exit "$qemu_rc"

grep -Fq '[NETWORK-N2][TX-QUEUED]' "$LOG" || { echo '[NETWORK-N2][FAIL] guest did not queue raw frame' >&2; exit 1; }
[[ -s "$PCAP" ]] || { echo '[NETWORK-N2][FAIL] QEMU capture is empty' >&2; exit 1; }
# Proof that bytes crossed the emulated NIC boundary, not merely that Send() returned true.
grep -aFq 'ZONDERQ_N2_TX' "$PCAP" || { echo '[NETWORK-N2][FAIL] probe payload absent from QEMU pcap' >&2; exit 1; }
echo '[NETWORK-N2][TX-PASS] raw Ethernet frame observed outside guest in QEMU pcap'

echo '[NETWORK-N2] RX proof is intentionally not claimed by this script; N2 remains incomplete until host->guest frame reception is independently verified.'
