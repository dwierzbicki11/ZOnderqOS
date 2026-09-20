#!/usr/bin/env bash
set -euo pipefail
ISO="${ZONDERQ_ISO:-}"; [[ -n "$ISO" && -f "$ISO" ]] || { echo '[NETWORK-N3][FAIL] ISO missing' >&2; exit 1; }
LOG="${ZONDERQ_NETWORK_N3_LOG:-network-n3-qemu.log}"; PCAP="${ZONDERQ_NETWORK_N3_PCAP:-network-n3.pcap}"; rm -f "$LOG" "$PCAP"
set +e
timeout 35s qemu-system-x86_64 -m 512M -smp 2 -cdrom "$ISO" -boot d -display none -serial stdio -no-reboot \
 -netdev socket,id=net0,udp=127.0.0.1:5556,localaddr=127.0.0.1:5555 -device e1000e,netdev=net0 \
 -object filter-dump,id=n3dump,netdev=net0,file="$PCAP" >"$LOG" 2>&1 &
qemu_pid=$!; set -e
# Controlled L2 peer: accept the guest's real ARP request and derive the reply from it.
python3 - <<'PY' &
import socket,sys,time
s=socket.socket(socket.AF_INET,socket.SOCK_DGRAM); s.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1); s.bind(('127.0.0.1',5556)); s.settimeout(25)
try:
 d,a=s.recvfrom(2048)
 if len(d)<42 or d[12:14]!=b'\x08\x06' or d[20:22]!=b'\x00\x01' or d[38:42]!=bytes([10,0,2,2]): raise RuntimeError('not expected ARP request')
 guest=d[6:12]; peer=bytes.fromhex('525400123456')
 r=guest+peer+b'\x08\x06'+b'\x00\x01\x08\x00\x06\x04\x00\x02'+peer+bytes([10,0,2,2])+guest+bytes([10,0,2,15])
 r+=b'\x00'*(64-len(r)); s.sendto(r,('127.0.0.1',5555)); print('[NETWORK-N3] controlled peer replied to guest ARP request')
except Exception as e:
 print('[NETWORK-N3][PEER-FAIL]',e,file=sys.stderr); sys.exit(1)
finally: s.close()
PY
peer_pid=$!
set +e; wait "$peer_pid"; peer_rc=$?; wait "$qemu_pid"; qemu_rc=$?; set -e
[[ $peer_rc -eq 0 ]] || { cat "$LOG"; exit "$peer_rc"; }; [[ $qemu_rc -eq 0 || $qemu_rc -eq 124 ]] || { cat "$LOG"; exit "$qemu_rc"; }
cat "$LOG"
grep -Fq '[NETWORK-N3][ARP-REQUEST-QUEUED]' "$LOG" || { echo '[NETWORK-N3][FAIL] guest ARP request not queued' >&2; exit 1; }
grep -Fq '[NETWORK-N3][ARP-REPLY-VALID]' "$LOG" || { echo '[NETWORK-N3][FAIL] guest did not validate ARP reply' >&2; exit 1; }
grep -Fq '[NETWORK-N3][CACHE-RESOLVED] 52:54:00:12:34:56' "$LOG" || { echo '[NETWORK-N3][FAIL] ARP resolution cache proof absent' >&2; exit 1; }
[[ -s "$PCAP" ]] || { echo '[NETWORK-N3][FAIL] pcap empty' >&2; exit 1; }
python3 - "$PCAP" <<'PY'
import sys
b=open(sys.argv[1],'rb').read()
if b'\x08\x06' not in b or bytes.fromhex('525400123456') not in b: raise SystemExit('[NETWORK-N3][FAIL] ARP evidence absent from pcap')
PY
echo '[NETWORK-N3][PASS] real ARP request/reply crossed E1000E and peer MAC was resolved'
