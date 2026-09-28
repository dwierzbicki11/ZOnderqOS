#!/usr/bin/env bash
set -euo pipefail
ISO="${ZONDERQ_ISO:-}"; [[ -n "$ISO" && -f "$ISO" ]] || exit 1
LOG=network-n6-qemu.log; PCAP=network-n6.pcap; rm -f "$LOG" "$PCAP"
timeout 35s qemu-system-x86_64 -m 512M -smp 2 -cdrom "$ISO" -boot d -display none -serial stdio -no-reboot -netdev socket,id=net0,udp=127.0.0.1:5586,localaddr=127.0.0.1:5585 -device e1000e,netdev=net0 -object filter-dump,id=n6dump,netdev=net0,file="$PCAP" >"$LOG" 2>&1 & q=$!
python3 - <<'PY' &
import socket,sys
def words(x): return sum((x[i]<<8)+(x[i+1] if i+1<len(x) else 0) for i in range(0,len(x),2))
def fold(s):
 while s>>16:s=(s&65535)+(s>>16)
 return (~s)&65535
def cs(x): return fold(words(x))
def ucs(src,dst,u): return fold(words(src)+words(dst)+17+len(u)+words(u))
s=socket.socket(socket.AF_INET,socket.SOCK_DGRAM);s.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1);s.bind(('127.0.0.1',5586));s.settimeout(25);peer=bytes.fromhex('525400aabbcc');lip=bytes([10,0,2,15]);pip=bytes([10,0,2,2])
try:
 d,_=s.recvfrom(2048)
 if len(d)<42 or d[12:14]!=b'\x08\x06' or d[20:22]!=b'\0\1':raise RuntimeError('ARP request absent')
 guest=d[6:12];r=guest+peer+b'\x08\x06'+b'\0\1\x08\0\6\4\0\2'+peer+pip+guest+lip;s.sendto(r+b'\0'*(64-len(r)),('127.0.0.1',5585))
 d,_=s.recvfrom(2048)
 if len(d)<42 or d[12:14]!=b'\x08\0':raise RuntimeError('IPv4/UDP TX absent')
 ip=bytearray(d[14:]);ihl=(ip[0]&15)*4;total=int.from_bytes(ip[2:4],'big');u=bytearray(ip[ihl:total]);ul=int.from_bytes(u[4:6],'big')
 if ip[0]>>4!=4 or ip[9]!=17 or ip[12:16]!=lip or ip[16:20]!=pip or cs(ip[:ihl])!=0:raise RuntimeError('invalid IPv4 UDP request')
 if ul!=len(u) or int.from_bytes(u[0:2],'big')!=46306 or int.from_bytes(u[2:4],'big')!=46307 or int.from_bytes(u[6:8],'big')==0 or ucs(lip,pip,u)!=0 or bytes(u[8:])!=b'ZONDERQ_N6_UDP_DATAGRAM':raise RuntimeError('invalid UDP datagram')
 payload=b'ZONDERQ_N6_UDP_REPLY';ru=bytearray(8+len(payload));ru[0:2]=(46307).to_bytes(2,'big');ru[2:4]=(46306).to_bytes(2,'big');ru[4:6]=len(ru).to_bytes(2,'big');ru[8:]=payload;ru[6:8]=ucs(pip,lip,ru).to_bytes(2,'big');h=bytearray(20);h[0]=0x45;h[2:4]=(20+len(ru)).to_bytes(2,'big');h[4:6]=(0x4e36).to_bytes(2,'big');h[6:8]=(0x4000).to_bytes(2,'big');h[8]=64;h[9]=17;h[12:16]=pip;h[16:20]=lip;h[10:12]=cs(h).to_bytes(2,'big');f=guest+peer+b'\x08\0'+h+ru;s.sendto(f+b'\0'*max(0,64-len(f)),('127.0.0.1',5585))
except Exception as e:print('[NETWORK-N6][PEER-FAIL]',e,file=sys.stderr);sys.exit(1)
finally:s.close()
PY
p=$!;set +e;wait $p;pr=$?;wait $q;qr=$?;set -e;cat "$LOG";[[ $pr -eq 0 && ($qr -eq 0 || $qr -eq 124) && -s "$PCAP" ]]
python3 - "$PCAP" <<'PY'
import struct,sys
b=open(sys.argv[1],'rb').read();e='<' if b[:4] in (b'\xd4\xc3\xb2\xa1',b'M<\xb2\xa1') else '>';o=24;fs=[]
while o+16<=len(b): _,_,n,_=struct.unpack(e+'IIII',b[o:o+16]);o+=16;fs.append(b[o:o+n]);o+=n
req=any(len(f)>=42 and f[12:14]==b'\x08\0' and f[23]==17 and b'ZONDERQ_N6_UDP_DATAGRAM' in f for f in fs);rep=any(len(f)>=42 and f[12:14]==b'\x08\0' and f[23]==17 and b'ZONDERQ_N6_UDP_REPLY' in f for f in fs);proof=any(len(f)>=14 and f[12:14]==b'\x88\xb9' and b'ZONDERQ_N6_UDP_VALIDATED' in f for f in fs)
if not(req and rep and proof):raise SystemExit('[NETWORK-N6][FAIL] UDP wire/runtime proof incomplete')
PY
echo '[NETWORK-N6][PASS] UDP TX/RX validated in guest and on wire'
