#!/usr/bin/env bash
set -euo pipefail
ISO="${ZONDERQ_ISO:-}"; [[ -n "$ISO" && -f "$ISO" ]] || exit 1
LOG=network-n5-qemu.log; PCAP=network-n5.pcap; rm -f "$LOG" "$PCAP"
timeout 35s qemu-system-x86_64 -m 512M -smp 2 -cdrom "$ISO" -boot d -display none -serial stdio -no-reboot -netdev socket,id=net0,udp=127.0.0.1:5576,localaddr=127.0.0.1:5575 -device e1000e,netdev=net0 -object filter-dump,id=n5dump,netdev=net0,file="$PCAP" >"$LOG" 2>&1 & q=$!
python3 - <<'PY' &
import socket,sys
def cs(x):
 s=sum((x[i]<<8)+(x[i+1] if i+1<len(x) else 0) for i in range(0,len(x),2))
 while s>>16:s=(s&65535)+(s>>16)
 return (~s)&65535
s=socket.socket(socket.AF_INET,socket.SOCK_DGRAM);s.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1);s.bind(('127.0.0.1',5576));s.settimeout(25);peer=bytes.fromhex('525400aabbcc')
try:
 d,_=s.recvfrom(2048)
 if len(d)<42 or d[12:14]!=b'\x08\x06' or d[20:22]!=b'\0\1':raise RuntimeError('ARP request absent')
 guest=d[6:12];r=guest+peer+b'\x08\x06'+b'\0\1\x08\0\6\4\0\2'+peer+bytes([10,0,2,2])+guest+bytes([10,0,2,15]);s.sendto(r+b'\0'*(64-len(r)),('127.0.0.1',5575))
 d,_=s.recvfrom(2048)
 if len(d)<42 or d[12:14]!=b'\x08\0':raise RuntimeError('IPv4/ICMP TX absent')
 ip=bytearray(d[14:]);ihl=(ip[0]&15)*4;total=int.from_bytes(ip[2:4],'big');ic=bytearray(ip[ihl:total])
 if ip[0]>>4!=4 or ip[9]!=1 or ip[12:16]!=bytes([10,0,2,15]) or ip[16:20]!=bytes([10,0,2,2]) or cs(ip[:ihl])!=0:raise RuntimeError('invalid IPv4 echo request')
 if len(ic)<8 or ic[0]!=8 or ic[1]!=0 or int.from_bytes(ic[4:6],'big')!=0x5a35 or int.from_bytes(ic[6:8],'big')!=1 or cs(ic)!=0 or bytes(ic[8:])!=b'ZONDERQ_N5_ICMP_ECHO':raise RuntimeError('invalid ICMP echo request')
 ic[0]=0;ic[2:4]=b'\0\0';ic[2:4]=cs(ic).to_bytes(2,'big');h=bytearray(20);h[0]=0x45;h[2:4]=(20+len(ic)).to_bytes(2,'big');h[4:6]=(0x4e35).to_bytes(2,'big');h[6:8]=(0x4000).to_bytes(2,'big');h[8]=64;h[9]=1;h[12:16]=bytes([10,0,2,2]);h[16:20]=bytes([10,0,2,15]);h[10:12]=cs(h).to_bytes(2,'big');f=guest+peer+b'\x08\0'+h+ic;s.sendto(f+b'\0'*max(0,64-len(f)),('127.0.0.1',5575))
except Exception as e:print('[NETWORK-N5][PEER-FAIL]',e,file=sys.stderr);sys.exit(1)
finally:s.close()
PY
p=$!;set +e;wait $p;pr=$?;wait $q;qr=$?;set -e;cat "$LOG";[[ $pr -eq 0 && ($qr -eq 0 || $qr -eq 124) && -s "$PCAP" ]]
python3 - "$PCAP" <<'PY'
import struct,sys
b=open(sys.argv[1],'rb').read();e='<' if b[:4] in (b'\xd4\xc3\xb2\xa1',b'M<\xb2\xa1') else '>';o=24;fs=[]
while o+16<=len(b): _,_,n,_=struct.unpack(e+'IIII',b[o:o+16]);o+=16;fs.append(b[o:o+n]);o+=n
req=any(len(f)>=42 and f[12:14]==b'\x08\0' and f[23]==1 and f[34]==8 and b'ZONDERQ_N5_ICMP_ECHO' in f for f in fs);rep=any(len(f)>=42 and f[12:14]==b'\x08\0' and f[23]==1 and f[34]==0 and b'ZONDERQ_N5_ICMP_ECHO' in f for f in fs);proof=any(len(f)>=14 and f[12:14]==b'\x88\xb8' and b'ZONDERQ_N5_ICMP_VALIDATED' in f for f in fs)
if not(req and rep and proof):raise SystemExit('[NETWORK-N5][FAIL] ICMP wire/runtime proof incomplete')
PY
echo '[NETWORK-N5][PASS] ICMP echo request/reply validated in guest and on wire'
