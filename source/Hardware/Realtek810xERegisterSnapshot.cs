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
            int speedMbps, bool fullDuplex, byte rawPhyStatus)
        {
            MacAddress = macAddress;
            Carrier = carrier;
            SpeedMbps = speedMbps;
            FullDuplex = fullDuplex;
            RawPhyStatus = rawPhyStatus;
        }

        public string MacAddress { get; }
        public bool Carrier { get; }
        // Zero means no confirmed carrier, not a negotiated speed.
        public int SpeedMbps { get; }
        public bool FullDuplex { get; }
        public byte RawPhyStatus { get; }
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
        private const byte LinkStatus = 0x02;
        private const byte Speed10 = 0x04;
        private const byte Speed100 = 0x08;
        private const byte Speed1000 = 0x10;
        private const byte FullDuplex = 0x01;

        public static bool TryDecode(byte[] stationAddress, byte phyStatus,
            out Realtek810xERegisterSnapshot snapshot)
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
                speed, duplex, phyStatus);
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
    /// Read precisely IDR0..IDR5 and PHYstatus (seven byte reads) from a
    /// caller-validated register window. Does not discover/map hardware.
    /// The caller must check PCI identity, D0, memory decoding and BAR aperture.
    /// </summary>
    public static class Realtek810xERegisterSampler
    {
        public static bool TrySample(IRealtek810xEByteReader registers,
            out Realtek810xERegisterSnapshot snapshot)
        {
            snapshot = default;
            if (registers == null) return false;

            byte[] mac = new byte[6];
            byte phy;
            try
            {
                for (ushort i = 0; i < 6; i++)
                    mac[i] = registers.Read8((ushort)(Realtek810xERegisterDecoder.StationAddressOffset + i));
                phy = registers.Read8(Realtek810xERegisterDecoder.PhyStatusOffset);
            }
            catch (Exception)
            {
                // A read error must never be misreported as a valid MAC/link.
                return false;
            }

            return Realtek810xERegisterDecoder.TryDecode(mac, phy, out snapshot);
        }
    }

}
