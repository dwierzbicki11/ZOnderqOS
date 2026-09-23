namespace ZonderqOS.Hardware
{
    public enum PciMemoryBarKind : byte
    {
        Memory32 = 0,
        Memory64 = 2
    }

    public readonly struct PciMemoryBar
    {
        public PciMemoryBar(ulong address, PciMemoryBarKind kind, bool prefetchable)
        {
            Address = address;
            Kind = kind;
            Prefetchable = prefetchable;
        }

        public ulong Address { get; }
        public PciMemoryBarKind Kind { get; }
        public bool Prefetchable { get; }
    }

    /// <summary>
    /// Architecture-neutral PCI BAR decoder used before any D2 MMIO access.
    /// It intentionally rejects I/O BARs, reserved memory types, zero/unassigned
    /// addresses and the all-ones sentinel. BAR sizing/probing is not performed here.
    /// </summary>
    public static class PciBarDecoder
    {
        public static bool TryDecodeMemory(uint low, uint high, out PciMemoryBar bar)
        {
            bar = default;
            if (low == 0 || low == 0xFFFFFFFF || (low & 0x1u) != 0)
                return false;

            uint type = (low >> 1) & 0x3u;
            bool prefetchable = (low & 0x8u) != 0;
            ulong addressLow = (ulong)(low & 0xFFFFFFF0u);

            if (type == 0)
            {
                if (addressLow == 0) return false;
                bar = new PciMemoryBar(addressLow, PciMemoryBarKind.Memory32, prefetchable);
                return true;
            }

            if (type == 2)
            {
                if (high == 0xFFFFFFFFu) return false;
                ulong address = ((ulong)high << 32) | addressLow;
                if (address == 0) return false;
                bar = new PciMemoryBar(address, PciMemoryBarKind.Memory64, prefetchable);
                return true;
            }

            // PCI memory BAR types 01b and 11b are reserved/legacy and are not
            // accepted for AHCI/NVMe MMIO in D2.
            return false;
        }
    }
}
