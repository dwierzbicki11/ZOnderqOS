#!/usr/bin/env bash
set -euo pipefail
ISO="${ZONDERQ_ISO:-}"; [[ -n "$ISO" && -f "$ISO" ]] || exit 1
LOG=network-n9-qemu.log; PCAP=network-n9.pcap; rm -f "$LOG" "$PCAP"
timeout 40s qemu-system-x86_64 -m 512M -smp 2 -cdrom "$ISO" -boot d -display none -serial stdio -no-reboot -netdev socket,id=net0,udp=127.0.0.1:5600,localaddr=127.0.0.1:5599 -device e1000e,netdev=net0 -object filter-dump,id=n9dump,netdev=net0,file="$PCAP" >"$LOG" 2>&1 & q=$!
python3 - <<'PY' &
import socket,sys
peer=bytes.fromhex('525400aabbcc'); pip=bytes([10,0,2,2]); gip=bytes([10,0,2,15]); pisn=0x12345678; cisn=0x5a390001; cp=b'ZONDERQ_N9_CLIENT'; sp=b'ZONDERQ_N9_SERVER'
def words(x):return sum((x[i]<<8)+(x[i+1] if i+1<len(x) else 0) for i in range(0,len(x),2))
def cs(x):
 s=words(x)
 while s>>16:s=(s&65535)+(s>>16)
 return (~s)&65535
def eth(dst,src,t,p):return dst+src+t.to_bytes(2,'big')+p
def arp(g):
 a=bytearray(28);a[:8]=bytes.fromhex('0001080006040002');a[8:14]=peer;a[14:18]=pip;a[18:24]=g;a[24:28]=gip;return eth(g,peer,0x806,a)
def tcp(g,seq,ack,flags,payload=b''):
 t=bytearray(20+len(payload));t[:2]=(8080).to_bytes(2,'big');t[2:4]=(46309).to_bytes(2,'big');t[4:8]=seq.to_bytes(4,'big');t[8:12]=ack.to_bytes(4,'big');t[12]=0x50;t[13]=flags;t[14:16]=(4096).to_bytes(2,'big');t[20:]=payload;t[16:18]=cs(pip+gip+b'\0\x06'+len(t).to_bytes(2,'big')+t).to_bytes(2,'big')
 h=bytearray(20);h[0]=0x45;h[2:4]=(20+len(t)).to_bytes(2,'big');h[8]=64;h[9]=6;h[12:16]=pip;h[16:20]=gip;h[10:12]=cs(h).to_bytes(2,'big');return eth(g,peer,0x800,h+t)
def parse(d,flags=None):
 if len(d)<54 or d[12:14]!=b'\x08\0' or d[23]!=6:raise RuntimeError('TCP frame absent')
 ih=(d[14]&15)*4;t=14+ih;th=(d[t+12]>>4)*4;seq=int.from_bytes(d[t+4:t+8],'big');ack=int.from_bytes(d[t+8:t+12],'big');fl=d[t+13];pl=d[t+th:14+int.from_bytes(d[16:18],'big')]
 if d[t:t+2]!=(46309).to_bytes(2,'big') or d[t+2:t+4]!=(8080).to_bytes(2,'big'):raise RuntimeError('TCP ports invalid')
 if cs(gip+pip+b'\0\x06'+(len(d)-t).to_bytes(2,'big')+d[t:])!=0:raise RuntimeError('TCP checksum invalid')
 if flags is not None and fl&flags!=flags:raise RuntimeError('TCP flags invalid')
 return seq,ack,fl,pl
s=socket.socket(socket.AF_INET,socket.SOCK_DGRAM);s.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1);s.bind(('127.0.0.1',5600));s.settimeout(30)
try:
 d,_=s.recvfrom(4096);g=d[6:12]
 if len(d)<42 or d[12:14]!=b'\x08\x06' or d[38:42]!=pip:raise RuntimeError('ARP absent')
 s.sendto(arp(g),('127.0.0.1',5599));d,_=s.recvfrom(4096);seq,ack,fl,pl=parse(d,0x02)
 if seq!=cisn or ack!=0 or fl&0x10:raise RuntimeError('SYN invalid')
 s.sendto(tcp(g,pisn,cisn+1,0x12),('127.0.0.1',5599));d,_=s.recvfrom(4096);seq,ack,fl,pl=parse(d,0x18)
 if seq!=cisn+1 or ack!=pisn+1 or pl!=cp:raise RuntimeError('client payload/sequence invalid')
 s.sendto(tcp(g,pisn+1,cisn+1+len(cp),0x18,sp),('127.0.0.1',5599));d,_=s.recvfrom(4096);seq,ack,fl,pl=parse(d,0x11)
 if seq!=cisn+1+len(cp) or ack!=pisn+1+len(sp):raise RuntimeError('client FIN invalid')
 s.sendto(tcp(g,pisn+1+len(sp),cisn+2+len(cp),0x11),('127.0.0.1',5599));d,_=s.recvfrom(4096);seq,ack,fl,pl=parse(d,0x10)
 if seq!=cisn+2+len(cp) or ack!=pisn+2+len(sp):raise RuntimeError('final ACK invalid')
except Exception as e:print('[NETWORK-N9][PEER-FAIL]',e,file=sys.stderr);sys.exit(1)
finally:s.close()
PY
p=$!;set +e;wait $p;pr=$?;wait $q;qr=$?;set -e;cat "$LOG";[[ $pr -eq 0 && ($qr -eq 0 || $qr -eq 124) && -s "$PCAP" ]]
python3 - "$PCAP" <<'PY'
import struct,sys
b=open(sys.argv[1],'rb').read();e='<' if b[:4] in (b'\xd4\xc3\xb2\xa1',b'M<\xb2\xa1') else '>';o=24;ok=False
while o+16<=len(b):
 _,_,n,_=struct.unpack(e+'IIII',b[o:o+16]);o+=16;f=b[o:o+n];o+=n
 if len(f)>=14 and f[12:14]==b'\x88\xbc' and b'ZONDERQ_N9_TCP_VALIDATED' in f:ok=True
if not ok:raise SystemExit('[NETWORK-N9][FAIL] TCP guest validation proof absent')
PY
echo '[NETWORK-N9][PASS] TCP handshake, bidirectional payload and orderly close proven'
