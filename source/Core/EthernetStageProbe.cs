#if ZONDERQ_NETWORK_N2_PROBE
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.Network;

namespace ZonderqOS
{
    /// <summary>
    /// CI-only N2 probe. It exercises the real NetworkManager -> E1000E TX path
    /// with a private experimental EtherType. It intentionally does not configure
    /// IPv4, ARP, DHCP or any higher protocol, preserving the staged dependency.
    /// </summary>
    internal static class EthernetStageProbe
    {
        private const ushort ProbeEtherType = 0x88B5;

        internal static void RunTx()
        {
            if (!NetworkManager.IsEnabled || !NetworkManager.Ready || !NetworkManager.LinkUp)
            {
                Serial.WriteString("[NETWORK-N2][TX-FAIL] NIC not ready/link-up\n");
                return;
            }

            MACAddress? mac = NetworkManager.MacAddress;
            if (mac is null)
            {
                Serial.WriteString("[NETWORK-N2][TX-FAIL] no MAC\n");
                return;
            }

            byte[] source = ParseMac(mac.ToString());
            byte[] frame = new byte[64];
            for (int i = 0; i < 6; i++) frame[i] = 0xFF;
            for (int i = 0; i < 6; i++) frame[6 + i] = source[i];
            frame[12] = (byte)(ProbeEtherType >> 8);
            frame[13] = (byte)ProbeEtherType;

            byte[] marker = { (byte)'Z', (byte)'O', (byte)'N', (byte)'D', (byte)'E', (byte)'R', (byte)'Q', (byte)'_', (byte)'N', (byte)'2', (byte)'_', (byte)'T', (byte)'X' };
            for (int i = 0; i < marker.Length; i++) frame[14 + i] = marker[i];

            bool queued = NetworkManager.Send(frame, frame.Length);
            Serial.WriteString(queued ? "[NETWORK-N2][TX-QUEUED] raw Ethernet frame queued\n" : "[NETWORK-N2][TX-FAIL] driver rejected raw Ethernet frame\n");
        }

        private static byte[] ParseMac(string text)
        {
            byte[] result = new byte[6];
            int output = 0;
            int value = 0;
            int digits = 0;
            for (int i = 0; i < text.Length && output < 6; i++)
            {
                char c = text[i];
                int nibble = c >= '0' && c <= '9' ? c - '0' : c >= 'A' && c <= 'F' ? c - 'A' + 10 : c >= 'a' && c <= 'f' ? c - 'a' + 10 : -1;
                if (nibble < 0) continue;
                value = (value << 4) | nibble;
                digits++;
                if (digits == 2)
                {
                    result[output++] = (byte)value;
                    value = 0;
                    digits = 0;
                }
            }
            return result;
        }
    }
}
#endif
