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

# Require the real E1000E path to initialize before accepting packet evidence.
grep -Fq '[E1000E] MAC Address:' "$LOG" || { echo '[NETWORK-N2][FAIL] E1000E MAC not observed' >&2; exit 1; }
grep -Fq '[E1000E] TX initialized' "$LOG" || { echo '[NETWORK-N2][FAIL] E1000E TX ring not initialized' >&2; exit 1; }
grep -Fq '[E1000E] Link: UP' "$LOG" || { echo '[NETWORK-N2][FAIL] E1000E link not up' >&2; exit 1; }

[[ -s "$PCAP" ]] || { echo '[NETWORK-N2][FAIL] QEMU capture is empty' >&2; exit 1; }
# Runtime proof is deliberately taken at the emulated NIC boundary. Console.WriteLine
# is VGA-only in the isolated probe kernel, so requiring its TX-QUEUED text on the
# serial stream would reject a frame that QEMU has already captured outside the guest.
grep -aFq 'ZONDERQ_N2_TX' "$PCAP" || { echo '[NETWORK-N2][FAIL] probe payload absent from QEMU pcap' >&2; exit 1; }
echo '[NETWORK-N2][TX-PASS] raw Ethernet frame observed outside guest in QEMU pcap'

echo '[NETWORK-N2] RX proof is intentionally not claimed by this script; N2 remains incomplete until host->guest frame reception is independently verified.'
