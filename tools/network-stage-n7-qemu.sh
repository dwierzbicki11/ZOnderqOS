#!/usr/bin/env bash
set -euo pipefail
ISO="${ZONDERQ_ISO:-}"; [[ -n "$ISO" && -f "$ISO" ]] || exit 1
LOG=network-n7-qemu.log; PCAP=network-n7.pcap; rm -f "$LOG" "$PCAP"
timeout 35s qemu-system-x86_64 -m 512M -smp 2 -cdrom "$ISO" -boot d -display none -serial stdio -no-reboot -netdev socket,id=net0,udp=127.0.0.1:5596,localaddr=127.0.0.1:5595 -device e1000e,netdev=net0 -object filter-dump,id=n7dump,netdev=net0,file="$PCAP" >"$LOG" 2>&1 & q=$!
python3 - <<'PY' &
import socket,sys
X=0x5a4e3701; server=bytes([10,0,2,2]); yi=bytes([10,0,2,15]); peer=bytes.fromhex('525400aabbcc')
def words(x):return sum((x[i]<<8)+(x[i+1] if i+1<len(x) else 0) for i in range(0,len(x),2))
def cs(x):
 s=words(x)
 while s>>16:s=(s&65535)+(s>>16)
 return (~s)&65535
def msgtype(d):
 b=14+20+8
 if len(d)<b+240 or int.from_bytes(d[b+4:b+8],'big')!=X:return 0
 p=b+240
 while p<len(d):
  c=d[p];p+=1
  if c==255:break
  if c==0:continue
  l=d[p];p+=1
  if c==53 and l==1:return d[p]
  p+=l
 return 0
def reply(guest,mt):
 opts=bytes([53,1,mt,54,4])+server
 if mt==5:opts+=bytes([1,4,255,255,255,0,3,4])+server+bytes([6,4,1,1,1,1])
 opts+=b'\xff';boot=bytearray(240+len(opts));boot[0]=2;boot[1]=1;boot[2]=6;boot[4:8]=X.to_bytes(4,'big');boot[16:20]=yi;boot[28:34]=guest;boot[236:240]=bytes.fromhex('63825363');boot[240:]=opts
 u=bytearray(8+len(boot));u[0:2]=(67).to_bytes(2,'big');u[2:4]=(68).to_bytes(2,'big');u[4:6]=len(u).to_bytes(2,'big');u[8:]=boot
 h=bytearray(20);h[0]=0x45;h[2:4]=(20+len(u)).to_bytes(2,'big');h[8]=64;h[9]=17;h[12:16]=server;h[16:20]=bytes([255]*4);h[10:12]=cs(h).to_bytes(2,'big')
 return bytes([255]*6)+peer+b'\x08\0'+h+u
s=socket.socket(socket.AF_INET,socket.SOCK_DGRAM);s.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1);s.bind(('127.0.0.1',5596));s.settimeout(25)
try:
 d,_=s.recvfrom(2048);guest=d[6:12]
 if msgtype(d)!=1:raise RuntimeError('DHCP DISCOVER absent')
 s.sendto(reply(guest,2),('127.0.0.1',5595));d,_=s.recvfrom(2048)
 if msgtype(d)!=3:raise RuntimeError('DHCP REQUEST absent')
 if yi not in d or server not in d:raise RuntimeError('REQUEST lacks offered IP/server id')
 s.sendto(reply(guest,5),('127.0.0.1',5595))
except Exception as e:print('[NETWORK-N7][PEER-FAIL]',e,file=sys.stderr);sys.exit(1)
finally:s.close()
PY
p=$!;set +e;wait $p;pr=$?;wait $q;qr=$?;set -e;cat "$LOG";[[ $pr -eq 0 && ($qr -eq 0 || $qr -eq 124) && -s "$PCAP" ]]
python3 - "$PCAP" <<'PY'
import struct,sys
b=open(sys.argv[1],'rb').read();e='<' if b[:4] in (b'\xd4\xc3\xb2\xa1',b'M<\xb2\xa1') else '>';o=24;fs=[]
while o+16<=len(b):_,_,n,_=struct.unpack(e+'IIII',b[o:o+16]);o+=16;fs.append(b[o:o+n]);o+=n
proof=any(len(f)>=14 and f[12:14]==b'\x88\xba' and b'ZONDERQ_N7_DHCP_CONFIGURED' in f and bytes([10,0,2,15,255,255,255,0,10,0,2,2,1,1,1,1]) in f for f in fs)
if not proof:raise SystemExit('[NETWORK-N7][FAIL] DHCP ACK/config proof absent')
PY
echo '[NETWORK-N7][PASS] DHCP DORA and applied configuration validated'
