#!/usr/bin/env bash
set -euo pipefail
ISO="${ZONDERQ_ISO:-}"; [[ -n "$ISO" && -f "$ISO" ]] || exit 1
LOG=network-n8-qemu.log; PCAP=network-n8.pcap; rm -f "$LOG" "$PCAP"
timeout 35s qemu-system-x86_64 -m 512M -smp 2 -cdrom "$ISO" -boot d -display none -serial stdio -no-reboot -netdev socket,id=net0,udp=127.0.0.1:5598,localaddr=127.0.0.1:5597 -device e1000e,netdev=net0 -object filter-dump,id=n8dump,netdev=net0,file="$PCAP" >"$LOG" 2>&1 & q=$!
python3 - <<'PY' &
import socket,sys
peer=bytes.fromhex('525400aabbcc'); pip=bytes([10,0,2,2]); gip=bytes([10,0,2,15]); answer=bytes([10,0,2,42])
def words(x):return sum((x[i]<<8)+(x[i+1] if i+1<len(x) else 0) for i in range(0,len(x),2))
def cs(x):
 s=words(x)
 while s>>16:s=(s&65535)+(s>>16)
 return (~s)&65535
def udp_cs(u,s,d):return cs(s+d+b'\0\x11'+len(u).to_bytes(2,'big')+u)
def eth(dst,src,t,p):return dst+src+t.to_bytes(2,'big')+p
def arp_reply(g):
 a=bytearray(28);a[:8]=bytes.fromhex('0001080006040002');a[8:14]=peer;a[14:18]=pip;a[18:24]=g;a[24:28]=gip;return eth(g,peer,0x806,a)
def dns_reply(g,d):
 ip=d[14:34];u=d[34:];query=u[8:];
 if len(query)<29 or query[:2]!=b'Z8' or query[2:4]!=b'\x01\0' or query[4:6]!=b'\0\x01' or query[12:29]!=b'\x07zonderq\x04test\0\0\x01\0\x01':raise RuntimeError('invalid DNS A query')
 r=query[:2]+b'\x81\x80\0\x01\0\x01\0\0\0\0'+query[12:29]+b'\xc0\x0c\0\x01\0\x01\0\0\0\x3c\0\x04'+answer
 ru=bytearray(8+len(r));ru[:2]=(53).to_bytes(2,'big');ru[2:4]=u[:2];ru[4:6]=len(ru).to_bytes(2,'big');ru[8:]=r;c=udp_cs(ru,pip,gip);ru[6:8]=(c or 0xffff).to_bytes(2,'big')
 h=bytearray(20);h[0]=0x45;h[2:4]=(20+len(ru)).to_bytes(2,'big');h[8]=64;h[9]=17;h[12:16]=pip;h[16:20]=gip;h[10:12]=cs(h).to_bytes(2,'big');return eth(g,peer,0x800,h+ru)
s=socket.socket(socket.AF_INET,socket.SOCK_DGRAM);s.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1);s.bind(('127.0.0.1',5598));s.settimeout(25)
try:
 d,_=s.recvfrom(2048);g=d[6:12]
 if len(d)<42 or d[12:14]!=b'\x08\x06' or d[38:42]!=pip:raise RuntimeError('ARP for DNS peer absent')
 s.sendto(arp_reply(g),('127.0.0.1',5597));d,_=s.recvfrom(2048)
 if len(d)<63 or d[12:14]!=b'\x08\0' or d[23]!=17 or d[36:38]!=b'\0\x35':raise RuntimeError('DNS UDP query absent')
 s.sendto(dns_reply(g,d),('127.0.0.1',5597))
except Exception as e:print('[NETWORK-N8][PEER-FAIL]',e,file=sys.stderr);sys.exit(1)
finally:s.close()
PY
p=$!;set +e;wait $p;pr=$?;wait $q;qr=$?;set -e;cat "$LOG";[[ $pr -eq 0 && ($qr -eq 0 || $qr -eq 124) && -s "$PCAP" ]]
python3 - "$PCAP" <<'PY'
import struct,sys
b=open(sys.argv[1],'rb').read();e='<' if b[:4] in (b'\xd4\xc3\xb2\xa1',b'M<\xb2\xa1') else '>';o=24;ok=False
while o+16<=len(b):
 _,_,n,_=struct.unpack(e+'IIII',b[o:o+16]);o+=16;f=b[o:o+n];o+=n
 if len(f)>=14 and f[12:14]==b'\x88\xbb' and b'ZONDERQ_N8_DNS_VALIDATED' in f and bytes([10,0,2,42]) in f:ok=True
if not ok:raise SystemExit('[NETWORK-N8][FAIL] DNS guest validation proof absent')
PY
echo '[NETWORK-N8][PASS] DNS A query/response and guest validation proven'
