#if ZONDERQ_NETWORK_N3_PROBE
using System;
using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.System.Network;

namespace ZonderqOS
{
    /// <summary>CI-only N3 proof: emits a real ARP request and only reports resolution after a validated ARP reply is cached.</summary>
    internal static class ArpStageProbe
    {
        private const ushort ProofEtherType = 0x88B6;
        private static readonly byte[] LocalIp = { 10, 0, 2, 15 };
        private static readonly byte[] PeerIp = { 10, 0, 2, 2 };
        private static readonly byte[] ResolvedMac = new byte[6];
        private static readonly byte[] ProofMarker = Bytes("ZONDERQ_N3_ARP_RESOLVED");
        private static bool Resolved;

        internal static void Run()
        {
            if (!NetworkManager.IsEnabled || !NetworkManager.Ready || !NetworkManager.LinkUp)
            {
                Console.WriteLine("[NETWORK-N3][FAIL] NIC not ready/link-up");
                return;
            }
            if (!NetworkManager.SetRawReceiveHandler(OnRawFrame))
            {
                Console.WriteLine("[NETWORK-N3][FAIL] raw receive handler rejected");
                return;
            }
            byte[] localMac = ParseMac(NetworkManager.MacAddress?.ToString() ?? string.Empty);
            byte[] frame = new byte[64];
            for (int i = 0; i < 6; i++) frame[i] = 0xff;
            Copy(localMac, 0, frame, 6, 6);
            frame[12] = 0x08; frame[13] = 0x06;
            Put16(frame, 14, 1); Put16(frame, 16, 0x0800); frame[18] = 6; frame[19] = 4; Put16(frame, 20, 1);
            Copy(localMac, 0, frame, 22, 6); Copy(LocalIp, 0, frame, 28, 4);
            for (int i = 0; i < 6; i++) frame[32 + i] = 0;
            Copy(PeerIp, 0, frame, 38, 4);
            bool queued = NetworkManager.Send(frame, frame.Length);
            Console.WriteLine(queued ? "[NETWORK-N3][ARP-REQUEST-QUEUED]" : "[NETWORK-N3][ARP-REQUEST-FAIL]");
        }

        private static void OnRawFrame(byte[] data, int length)
        {
            if (Resolved || length < 42 || data.Length < length || data[12] != 0x08 || data[13] != 0x06) return;
            if (Get16(data, 14) != 1 || Get16(data, 16) != 0x0800 || data[18] != 6 || data[19] != 4 || Get16(data, 20) != 2) return;
            for (int i = 0; i < 4; i++) if (data[28 + i] != PeerIp[i] || data[38 + i] != LocalIp[i]) return;
            for (int i = 0; i < 6; i++) ResolvedMac[i] = data[22 + i];
            Resolved = true;
            Console.WriteLine("[NETWORK-N3][ARP-REPLY-VALID]");
            Console.WriteLine("[NETWORK-N3][CACHE-RESOLVED] " + MacText(ResolvedMac));
            SendResolutionProof();
        }

        // Emit a distinct Ethernet frame only after a validated ARP reply populated
        // ResolvedMac. QEMU PCAP can therefore prove RX + validation + cache state even
        // when the Gen3 console does not mirror probe Console.WriteLine calls to serial.
        private static void SendResolutionProof()
        {
            byte[] localMac = ParseMac(NetworkManager.MacAddress?.ToString() ?? string.Empty);
            byte[] frame = new byte[64];
            Copy(ResolvedMac, 0, frame, 0, 6);
            Copy(localMac, 0, frame, 6, 6);
            frame[12] = (byte)(ProofEtherType >> 8); frame[13] = (byte)ProofEtherType;
            Copy(ProofMarker, 0, frame, 14, ProofMarker.Length);
            Copy(ResolvedMac, 0, frame, 14 + ProofMarker.Length, 6);
            NetworkManager.Send(frame, frame.Length);
        }

        private static ushort Get16(byte[] b, int o) => (ushort)((b[o] << 8) | b[o + 1]);
        private static void Put16(byte[] b, int o, ushort v) { b[o] = (byte)(v >> 8); b[o + 1] = (byte)v; }
        private static void Copy(byte[] s, int so, byte[] d, int o, int n) { for (int i = 0; i < n; i++) d[o + i] = s[so + i]; }
        private static string MacText(byte[] b) => Hex(b[0])+":"+Hex(b[1])+":"+Hex(b[2])+":"+Hex(b[3])+":"+Hex(b[4])+":"+Hex(b[5]);
        private static string Hex(byte b) { const string h="0123456789ABCDEF"; return new string(new[]{h[b>>4],h[b&15]}); }
        private static byte[] Bytes(string text) { byte[] r=new byte[text.Length]; for(int i=0;i<text.Length;i++) r[i]=(byte)text[i]; return r; }
        private static byte[] ParseMac(string text)
        {
            byte[] r=new byte[6]; int output=0,value=0,digits=0;
            for(int i=0;i<text.Length&&output<6;i++){char c=text[i];int n=c>='0'&&c<='9'?c-'0':c>='A'&&c<='F'?c-'A'+10:c>='a'&&c<='f'?c-'a'+10:-1;if(n<0)continue;value=(value<<4)|n;if(++digits==2){r[output++]=(byte)value;value=0;digits=0;}}
            return r;
        }
    }
}
#endif
