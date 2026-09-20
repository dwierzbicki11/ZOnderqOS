#if ZONDERQ_NETWORK_N4_PROBE
using System;
using Cosmos.Kernel.System.Network;

namespace ZonderqOS
{
    /// <summary>N4 proof: resolve peer with ARP, then exchange and validate real Ethernet/IPv4 packets.</summary>
    internal static class Ipv4StageProbe
    {
        private const byte Protocol = 253; // experimental/test protocol; deliberately not ICMP (N5)
        private const ushort ProofEtherType = 0x88B7;
        private static readonly byte[] LocalIp = {10,0,2,15};
        private static readonly byte[] PeerIp = {10,0,2,2};
        private static readonly byte[] PeerMac = new byte[6];
        private static readonly byte[] TxPayload = Bytes("ZONDERQ_N4_IPV4_TX");
        private static readonly byte[] RxPayload = Bytes("ZONDERQ_N4_IPV4_RX");
        private static readonly byte[] ProofMarker = Bytes("ZONDERQ_N4_IPV4_VALIDATED");
        private static byte[] LocalMac = new byte[6];
        private static bool ArpResolved, Ipv4Validated;

        internal static void Run()
        {
            if (!NetworkManager.IsEnabled || !NetworkManager.Ready || !NetworkManager.LinkUp) { Console.WriteLine("[NETWORK-N4][FAIL] NIC not ready/link-up"); return; }
            if (!NetworkManager.SetRawReceiveHandler(OnRawFrame)) { Console.WriteLine("[NETWORK-N4][FAIL] raw receive handler rejected"); return; }
            LocalMac = ParseMac(NetworkManager.MacAddress?.ToString() ?? string.Empty);
            byte[] f=new byte[64]; for(int i=0;i<6;i++)f[i]=0xff; Copy(LocalMac,0,f,6,6); f[12]=0x08;f[13]=0x06;
            Put16(f,14,1);Put16(f,16,0x0800);f[18]=6;f[19]=4;Put16(f,20,1);Copy(LocalMac,0,f,22,6);Copy(LocalIp,0,f,28,4);Copy(PeerIp,0,f,38,4);
            NetworkManager.Send(f,f.Length);
        }

        private static void OnRawFrame(byte[] d,int n)
        {
            if(n<14 || d.Length<n) return;
            if(!ArpResolved && n>=42 && d[12]==0x08 && d[13]==0x06 && Get16(d,20)==2 && Eq(d,28,PeerIp) && Eq(d,38,LocalIp)) {
                Copy(d,22,PeerMac,0,6); ArpResolved=true; SendIpv4(); return;
            }
            if(Ipv4Validated || !ArpResolved || n<34 || d[12]!=0x08 || d[13]!=0x00) return;
            int ihl=(d[14]&15)*4; if((d[14]>>4)!=4 || ihl<20 || n<14+ihl || d[23]!=Protocol) return;
            int total=Get16(d,16); if(total<ihl || 14+total>n || !Eq(d,26,PeerIp) || !Eq(d,30,LocalIp)) return;
            if(Checksum(d,14,ihl)!=0 || total-ihl!=RxPayload.Length) return;
            for(int i=0;i<RxPayload.Length;i++) if(d[14+ihl+i]!=RxPayload[i]) return;
            Ipv4Validated=true; SendProof();
        }

        private static void SendIpv4()
        {
            int ipLen=20+TxPayload.Length; byte[] f=new byte[Math.Max(64,14+ipLen)]; Copy(PeerMac,0,f,0,6);Copy(LocalMac,0,f,6,6);f[12]=0x08;f[13]=0x00;
            int o=14;f[o]=0x45;f[o+1]=0;Put16(f,o+2,(ushort)ipLen);Put16(f,o+4,0x4e34);Put16(f,o+6,0x4000);f[o+8]=64;f[o+9]=Protocol;Copy(LocalIp,0,f,o+12,4);Copy(PeerIp,0,f,o+16,4);Put16(f,o+10,Checksum(f,o,20));Copy(TxPayload,0,f,o+20,TxPayload.Length);
            NetworkManager.Send(f,f.Length);
        }

        private static void SendProof()
        {
            byte[] f=new byte[64];Copy(PeerMac,0,f,0,6);Copy(LocalMac,0,f,6,6);f[12]=(byte)(ProofEtherType>>8);f[13]=(byte)(ProofEtherType&255);Copy(ProofMarker,0,f,14,ProofMarker.Length);NetworkManager.Send(f,f.Length);
        }

        private static ushort Checksum(byte[] b,int o,int n){uint s=0;for(int i=0;i<n;i+=2)s+=(uint)((b[o+i]<<8)+(i+1<n?b[o+i+1]:0));while((s>>16)!=0)s=(s&65535)+(s>>16);return(ushort)~s;}
        private static bool Eq(byte[] b,int o,byte[] x){for(int i=0;i<x.Length;i++)if(b[o+i]!=x[i])return false;return true;}
        private static ushort Get16(byte[] b,int o)=>(ushort)((b[o]<<8)|b[o+1]);
        private static void Put16(byte[] b,int o,ushort v){b[o]=(byte)(v>>8);b[o+1]=(byte)(v&255);}
        private static void Copy(byte[] s,int so,byte[] d,int o,int n){for(int i=0;i<n;i++)d[o+i]=s[so+i];}
        private static byte[] Bytes(string s){byte[] r=new byte[s.Length];for(int i=0;i<s.Length;i++)r[i]=(byte)s[i];return r;}
        private static byte[] ParseMac(string s){byte[] r=new byte[6];int p=0,v=0,k=0;for(int i=0;i<s.Length&&p<6;i++){char c=s[i];int x=c>='0'&&c<='9'?c-'0':c>='A'&&c<='F'?c-'A'+10:c>='a'&&c<='f'?c-'a'+10:-1;if(x<0)continue;v=(v<<4)|x;if(++k==2){r[p++]=(byte)v;v=0;k=0;}}return r;}
    }
}
#endif
