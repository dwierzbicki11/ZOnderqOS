#!/usr/bin/env bash
set -euo pipefail
ISO="${ZONDERQ_ISO:-}"
[[ -n "$ISO" && -f "$ISO" ]] || { echo '[NETWORK-N2][FAIL] ISO missing' >&2; exit 1; }
LOG="${ZONDERQ_NETWORK_N2_LOG:-network-n2-qemu.log}"
PCAP="${ZONDERQ_NETWORK_N2_PCAP:-network-n2.pcap}"
rm -f "$LOG" "$PCAP"

# socket netdev lets the host inject an actual Ethernet frame into E1000E while
# filter-dump independently records what crossed the emulated NIC boundary.
set +e
timeout 35s qemu-system-x86_64 \
  -m 512M -smp 2 -cdrom "$ISO" -boot d -display none -serial stdio -no-reboot \
  -netdev socket,id=net0,udp=127.0.0.1:5556,localaddr=127.0.0.1:5555 \
  -device e1000e,netdev=net0 \
  -object filter-dump,id=n2dump,netdev=net0,file="$PCAP" >"$LOG" 2>&1 &
qemu_pid=$!
set -e

# Give the kernel enough time to initialize E1000E and install its raw RX hook.
sleep 10
python3 - <<'PY'
import socket
marker = b'ZONDERQ_N2_RX'
frame = b'\xff'*6 + bytes.fromhex('525400aabbcc') + bytes.fromhex('88b5') + marker
frame += b'\x00' * (64 - len(frame))
s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
s.sendto(frame, ('127.0.0.1', 5555))
s.close()
print('[NETWORK-N2] injected host->guest raw Ethernet probe')
PY

set +e
wait "$qemu_pid"
qemu_rc=$?
set -e
[[ $qemu_rc -eq 0 || $qemu_rc -eq 124 ]] || { cat "$LOG"; exit "$qemu_rc"; }
cat "$LOG"

grep -Fq '[E1000E] MAC Address:' "$LOG" || { echo '[NETWORK-N2][FAIL] E1000E MAC not observed' >&2; exit 1; }
grep -Fq '[E1000E] TX initialized' "$LOG" || { echo '[NETWORK-N2][FAIL] E1000E TX ring not initialized' >&2; exit 1; }
grep -Fq '[E1000E] RX initialized' "$LOG" || { echo '[NETWORK-N2][FAIL] E1000E RX ring not initialized' >&2; exit 1; }
grep -Fq '[E1000E] Link: UP' "$LOG" || { echo '[NETWORK-N2][FAIL] E1000E link not up' >&2; exit 1; }

[[ -s "$PCAP" ]] || { echo '[NETWORK-N2][FAIL] QEMU capture is empty' >&2; exit 1; }
grep -aFq 'ZONDERQ_N2_TX' "$PCAP" || { echo '[NETWORK-N2][FAIL] guest TX payload absent from QEMU pcap' >&2; exit 1; }
# The host injection itself is not enough. RX only passes when the guest raw
# callback validates ZONDERQ_N2_RX and transmits this distinct ACK marker.
grep -aFq 'ZONDERQ_N2_RX_ACK' "$PCAP" || { echo '[NETWORK-N2][FAIL] guest did not ACK injected RX frame' >&2; exit 1; }

echo '[NETWORK-N2][TX-PASS] guest Ethernet frame observed outside guest'
echo '[NETWORK-N2][RX-PASS] host frame delivered to guest callback and acknowledged'
