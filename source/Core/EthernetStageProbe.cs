#if ZONDERQ_NETWORK_N2_PROBE
using System;
using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.System.Network;

namespace ZonderqOS
{
    /// <summary>
    /// CI-only N2 probe. It exercises raw Ethernet TX and RX without configuring
    /// ARP, IPv4, DHCP or any higher protocol. RX is proved by emitting an ACK
    /// frame only after the guest callback has validated the injected payload.
    /// </summary>
    internal static class EthernetStageProbe
    {
        private const ushort ProbeEtherType = 0x88B5;
        private static readonly byte[] RxMarker = Bytes("ZONDERQ_N2_RX");
        private static readonly byte[] RxAckMarker = Bytes("ZONDERQ_N2_RX_ACK");

        internal static void Run()
        {
            if (!NetworkManager.IsEnabled || !NetworkManager.Ready || !NetworkManager.LinkUp)
            {
                Console.WriteLine("[NETWORK-N2][FAIL] NIC not ready/link-up");
                return;
            }

            if (!NetworkManager.SetRawReceiveHandler(OnRawFrame))
            {
                Console.WriteLine("[NETWORK-N2][RX-FAIL] raw receive handler rejected");
                return;
            }

            SendMarker(Bytes("ZONDERQ_N2_TX"), "TX");
        }

        private static void OnRawFrame(byte[] data, int length)
        {
            if (length < 14 + RxMarker.Length || data.Length < length)
            {
                return;
            }

            if (data[12] != unchecked((byte)(ProbeEtherType >> 8)) || data[13] != unchecked((byte)ProbeEtherType))
            {
                return;
            }

            for (int i = 0; i < RxMarker.Length; i++)
            {
                if (data[14 + i] != RxMarker[i]) return;
            }

            // This ACK can only exist if E1000E delivered the host-injected frame
            // into the guest callback and the payload was validated above.
            SendMarker(RxAckMarker, "RX-ACK");
        }

        private static void SendMarker(byte[] marker, string label)
        {
            MACAddress? mac = NetworkManager.MacAddress;
            if (mac is null) return;

            byte[] source = ParseMac(mac.ToString() ?? string.Empty);
            byte[] frame = new byte[64];
            for (int i = 0; i < 6; i++) frame[i] = 0xFF;
            for (int i = 0; i < 6; i++) frame[6 + i] = source[i];
            frame[12] = unchecked((byte)(ProbeEtherType >> 8));
            frame[13] = unchecked((byte)ProbeEtherType);
            for (int i = 0; i < marker.Length; i++) frame[14 + i] = marker[i];

            bool queued = NetworkManager.Send(frame, frame.Length);
            Console.WriteLine(queued ? "[NETWORK-N2][" + label + "-QUEUED]" : "[NETWORK-N2][" + label + "-FAIL]");
        }

        private static byte[] Bytes(string text)
        {
            byte[] result = new byte[text.Length];
            for (int i = 0; i < text.Length; i++) result[i] = (byte)text[i];
            return result;
        }

        private static byte[] ParseMac(string text)
        {
            byte[] result = new byte[6];
            int output = 0, value = 0, digits = 0;
            for (int i = 0; i < text.Length && output < 6; i++)
            {
                char c = text[i];
                int nibble = c >= '0' && c <= '9' ? c - '0' : c >= 'A' && c <= 'F' ? c - 'A' + 10 : c >= 'a' && c <= 'f' ? c - 'a' + 10 : -1;
                if (nibble < 0) continue;
                value = (value << 4) | nibble;
                if (++digits == 2)
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
