using System;

namespace ZonderqOS.Hardware
{
    /// <summary>
    /// Decoded RTL810xE register values, supplied by a future hardware reader.
    /// A decoded snapshot is NOT evidence that MMIO was mapped, that the
    /// Ethernet link was sampled, or that a driver has bound to the device.
    /// </summary>
    public readonly struct Realtek810xERegisterSnapshot
    {
        public Realtek810xERegisterSnapshot(string macAddress, bool carrier,
            int speedMbps, bool fullDuplex, byte rawPhyStatus, uint rawTxConfig = 0)
        {
            MacAddress = macAddress;
            Carrier = carrier;
            SpeedMbps = speedMbps;
            FullDuplex = fullDuplex;
            RawPhyStatus = rawPhyStatus;
            RawTxConfig = rawTxConfig;
        }

        public string MacAddress { get; }
        public bool Carrier { get; }
        // Zero means no confirmed carrier, not a negotiated speed.
        public int SpeedMbps { get; }
        public bool FullDuplex { get; }
        public byte RawPhyStatus { get; }
        // TxConfig is read-only identification metadata, never a TX completion proof.
        public uint RawTxConfig { get; }
        public ushort Xid => (ushort)((RawTxConfig >> 20) & 0xFCF);
        public string ChipVariant => Realtek810xERegisterDecoder.GetChipVariant(Xid);
    }

    /// <summary>
    /// Pure decoder for the six station-address bytes at IDR0..IDR5 and
    /// the RTL810xE PHYstatus byte. No PCI, MMIO, DMA or IRQ access.
    /// The register layout is shared with Linux r8169's 810x family;
    /// a hardware reader must first verify the exact MAC variant.
    /// </summary>
    public static class Realtek810xERegisterDecoder
    {
        public const ushort StationAddressOffset = 0x00;
        public const ushort PhyStatusOffset = 0x6C;
        public const ushort TxConfigOffset = 0x40;
        private const byte LinkStatus = 0x02;
        private const byte Speed10 = 0x04;
        private const byte Speed100 = 0x08;
        private const byte Speed1000 = 0x10;
        private const byte FullDuplex = 0x01;

        // Linux r8169 extracts XID from TxConfig bits 20..31 and uses
        // ordered masks. Only the two reviewed 10/100 families are labelled;
        // an unrecognised XID must never be treated as a supported DMA variant.
        public static string GetChipVariant(ushort xid)
        {
            if ((xid & 0x7C8) == 0x448) return "RTL8106e";
            if ((xid & 0x7CF) == 0x409 || (xid & 0x7C8) == 0x408)
                return "RTL8105e";
            return "unknown";
        }

        public static bool TryDecode(byte[] stationAddress, byte phyStatus,
            out Realtek810xERegisterSnapshot snapshot, uint rawTxConfig = 0)
        {
            snapshot = default;
            if (stationAddress == null || stationAddress.Length != 6)
                return false;

            // Ethernet source addresses must be unicast and nonzero.
            if ((stationAddress[0] & 1) != 0)
                return false;
            bool anyNonzero = false;
            for (int i = 0; i < stationAddress.Length; i++)
                anyNonzero |= stationAddress[i] != 0;
            if (!anyNonzero) return false;

            bool carrier = (phyStatus & LinkStatus) != 0;
            int speed = 0;
            bool duplex = false;
            if (carrier)
            {
                // RTL810xE is 10/100 Fast Ethernet. Reject conflicting,
                // absent or unexpected gigabit speed indications.
                bool ten = (phyStatus & Speed10) != 0;
                bool hundred = (phyStatus & Speed100) != 0;
                if (ten == hundred || (phyStatus & Speed1000) != 0)
                    return false;
                speed = hundred ? 100 : 10;
                duplex = (phyStatus & FullDuplex) != 0;
            }

            string mac = stationAddress[0].ToString("X2");
            for (int i = 1; i < stationAddress.Length; i++)
                mac += ":" + stationAddress[i].ToString("X2");
            snapshot = new Realtek810xERegisterSnapshot(mac, carrier,
                speed, duplex, phyStatus, rawTxConfig);
            return true;
        }
    }
    /// <summary>
    /// A bounded, read-only view of an ALREADY mapped RTL810xE register window.
    /// Implementations must not write PCI config, enable DMA or change IRQ masks.
    /// </summary>
    public interface IRealtek810xEByteReader
    {
        byte Read8(ushort offset);
    }

    /// <summary>
    /// Read IDR0..IDR5, PHYstatus and TxConfig twice (22 bounded byte reads)
    /// from a caller-validated register window. Reject changed snapshots. Does not discover/map hardware.
    /// The caller must check PCI identity, D0, memory decoding and BAR aperture.
    /// </summary>
    public static class Realtek810xERegisterSampler
    {
        public static bool TrySample(IRealtek810xEByteReader registers,
            out Realtek810xERegisterSnapshot snapshot)
        {
            snapshot = default;
            if (registers == null) return false;

            // PHY can renegotiate during a register read. A successful
            // snapshot requires two identical read-only passes.
            if (!TryReadOnce(registers, out var first) ||
                !TryReadOnce(registers, out var second))
                return false;
            if (first.MacAddress != second.MacAddress ||
                first.RawPhyStatus != second.RawPhyStatus ||
                first.RawTxConfig != second.RawTxConfig)
                return false;

            snapshot = second;
            return true;
        }

        private static bool TryReadOnce(IRealtek810xEByteReader registers,
            out Realtek810xERegisterSnapshot snapshot)
        {
            snapshot = default;
            byte[] mac = new byte[6];
            byte phy;
            uint txConfig = 0;
            try
            {
                for (ushort i = 0; i < 6; i++)
                    mac[i] = registers.Read8((ushort)(Realtek810xERegisterDecoder.StationAddressOffset + i));
                phy = registers.Read8(Realtek810xERegisterDecoder.PhyStatusOffset);
                // Four little-endian bytes; no register writes or DMA setup.
                for (ushort i = 0; i < 4; i++)
                    txConfig |= (uint)registers.Read8((ushort)(Realtek810xERegisterDecoder.TxConfigOffset + i)) << (8 * i);
            }
            catch (Exception)
            {
                // A read error must never be misreported as a valid MAC/link.
                return false;
            }

            return Realtek810xERegisterDecoder.TryDecode(mac, phy, out snapshot, txConfig);
        }
    }

}
