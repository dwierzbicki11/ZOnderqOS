#!/usr/bin/env bash
set -euo pipefail
ISO="${ZONDERQ_ISO:-}"; [[ -n "$ISO" && -f "$ISO" ]] || exit 1
LOG="network-n4-qemu.log"; PCAP="network-n4.pcap"; rm -f "$LOG" "$PCAP"
timeout 35s qemu-system-x86_64 -m 512M -smp 2 -cdrom "$ISO" -boot d -display none -serial stdio -no-reboot -netdev socket,id=net0,udp=127.0.0.1:5566,localaddr=127.0.0.1:5565 -device e1000e,netdev=net0 -object filter-dump,id=n4dump,netdev=net0,file="$PCAP" >"$LOG" 2>&1 & q=$!
python3 - <<'PY' &
import socket,struct,sys
def csum(x):
 s=sum((x[i]<<8)+(x[i+1] if i+1<len(x) else 0) for i in range(0,len(x),2));
 while s>>16:s=(s&65535)+(s>>16)
 return (~s)&65535
s=socket.socket(socket.AF_INET,socket.SOCK_DGRAM);s.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1);s.bind(('127.0.0.1',5566));s.settimeout(25);peer=bytes.fromhex('525400aabbcc')
try:
 d,a=s.recvfrom(2048)
 if len(d)<42 or d[12:14]!=b'\x08\x06' or d[20:22]!=b'\0\1':raise RuntimeError('ARP request absent')
 guest=d[6:12];r=guest+peer+b'\x08\x06'+b'\0\1\x08\0\6\4\0\2'+peer+bytes([10,0,2,2])+guest+bytes([10,0,2,15]);s.sendto(r+b'\0'*(64-len(r)),('127.0.0.1',5565))
 d,a=s.recvfrom(2048)
 if len(d)<34 or d[:6]!=peer or d[12:14]!=b'\x08\0':raise RuntimeError('IPv4 TX absent')
 ip=bytearray(d[14:]);ihl=(ip[0]&15)*4;total=int.from_bytes(ip[2:4],'big')
 if ip[0]>>4!=4 or ip[9]!=253 or ip[12:16]!=bytes([10,0,2,15]) or ip[16:20]!=bytes([10,0,2,2]) or csum(ip[:ihl])!=0 or bytes(ip[ihl:total])!=b'ZONDERQ_N4_IPV4_TX':raise RuntimeError('invalid IPv4 TX')
 payload=b'ZONDERQ_N4_IPV4_RX';h=bytearray(20);h[0]=0x45;h[2:4]=(20+len(payload)).to_bytes(2,'big');h[4:6]=(0x4e34).to_bytes(2,'big');h[6:8]=(0x4000).to_bytes(2,'big');h[8]=64;h[9]=253;h[12:16]=bytes([10,0,2,2]);h[16:20]=bytes([10,0,2,15]);h[10:12]=csum(h).to_bytes(2,'big');f=guest+peer+b'\x08\0'+h+payload;s.sendto(f+b'\0'*max(0,64-len(f)),('127.0.0.1',5565))
except Exception as e:print('[NETWORK-N4][PEER-FAIL]',e,file=sys.stderr);sys.exit(1)
finally:s.close()
PY
p=$!;set +e;wait $p;pr=$?;wait $q;qr=$?;set -e;cat "$LOG";[[ $pr -eq 0 && ($qr -eq 0 || $qr -eq 124) && -s "$PCAP" ]]
python3 - "$PCAP" <<'PY'
import struct,sys
b=open(sys.argv[1],'rb').read();e='<' if b[:4] in (b'\xd4\xc3\xb2\xa1',b'M<\xb2\xa1') else '>';o=24;fs=[]
while o+16<=len(b):
 _,_,n,_=struct.unpack(e+'IIII',b[o:o+16]);o+=16;fs.append(b[o:o+n]);o+=n
peer=bytes.fromhex('525400aabbcc');tx=any(len(f)>=34 and f[:6]==peer and f[12:14]==b'\x08\0' and f[23]==253 and b'ZONDERQ_N4_IPV4_TX' in f for f in fs);rx=any(len(f)>=34 and f[6:12]==peer and f[12:14]==b'\x08\0' and f[23]==253 and b'ZONDERQ_N4_IPV4_RX' in f for f in fs);proof=any(f[12:14]==b'\x88\xb7' and b'ZONDERQ_N4_IPV4_VALIDATED' in f for f in fs if len(f)>=14)
if not(tx and rx and proof):raise SystemExit('[NETWORK-N4][FAIL] wire proof incomplete')
PY
echo '[NETWORK-N4][PASS] IPv4 TX/RX validated in guest and on wire'
