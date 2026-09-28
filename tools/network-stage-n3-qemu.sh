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
# Use a MAC distinct from QEMU's guest E1000E MAC; frames whose source equals the
# receiver's own MAC can be rejected by NIC filtering and are not a valid peer model.
python3 - <<'PY' &
import socket,sys
s=socket.socket(socket.AF_INET,socket.SOCK_DGRAM); s.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1); s.bind(('127.0.0.1',5556)); s.settimeout(25)
try:
 d,a=s.recvfrom(2048)
 if len(d)<42 or d[12:14]!=b'\x08\x06' or d[20:22]!=b'\x00\x01' or d[38:42]!=bytes([10,0,2,2]): raise RuntimeError('not expected ARP request')
 guest=d[6:12]; peer=bytes.fromhex('525400aabbcc')
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
[[ -s "$PCAP" ]] || { echo '[NETWORK-N3][FAIL] pcap empty' >&2; exit 1; }
# Parse actual PCAP records. Success requires all three independent wire events:
# guest ARP request, controlled peer ARP reply, and a proof frame that the guest can
# only emit after its RX callback validates that reply and stores the resolved MAC.
python3 - "$PCAP" <<'PY'
import struct,sys
b=open(sys.argv[1],'rb').read(); peer=bytes.fromhex('525400aabbcc'); guest=bytes.fromhex('525400123456'); marker=b'ZONDERQ_N3_ARP_RESOLVED'
if len(b)<24: raise SystemExit('[NETWORK-N3][FAIL] invalid pcap')
magic=b[:4]; endian='<' if magic in (b'\xd4\xc3\xb2\xa1',b'M<\xb2\xa1') else '>'
o=24; frames=[]
while o+16<=len(b):
 _,_,n,_=struct.unpack(endian+'IIII',b[o:o+16]); o+=16
 if o+n>len(b): break
 frames.append(b[o:o+n]); o+=n
req=any(len(f)>=42 and f[:6]==b'\xff'*6 and f[6:12]==guest and f[12:14]==b'\x08\x06' and f[20:22]==b'\x00\x01' and f[38:42]==bytes([10,0,2,2]) for f in frames)
rep=any(len(f)>=42 and f[:6]==guest and f[6:12]==peer and f[12:14]==b'\x08\x06' and f[20:22]==b'\x00\x02' and f[28:32]==bytes([10,0,2,2]) and f[38:42]==bytes([10,0,2,15]) for f in frames)
proof=any(len(f)>=14+len(marker)+6 and f[:6]==peer and f[12:14]==b'\x88\xb6' and f[14:14+len(marker)]==marker and f[14+len(marker):14+len(marker)+6]==peer for f in frames)
if not req: raise SystemExit('[NETWORK-N3][FAIL] real guest ARP request absent')
if not rep: raise SystemExit('[NETWORK-N3][FAIL] controlled ARP reply absent')
if not proof: raise SystemExit('[NETWORK-N3][FAIL] guest RX/cache resolution proof absent')
PY
echo '[NETWORK-N3][PASS] real ARP request/reply crossed E1000E and peer MAC was resolved'
