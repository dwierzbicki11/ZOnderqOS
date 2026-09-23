using System;

namespace ZonderqOS.Hardware
{
    /// <summary>
    /// D2.2 read-only PCI configuration helper for storage controller MMIO BARs.
    /// It never sizes, writes, enables decoding, resets hardware, or touches MMIO.
    /// </summary>
    public static class StorageControllerBars
    {
        private const byte Bar0Offset = 0x10;
        private const byte AhciAbarOffset = 0x24;

        public static bool TryReadAhciAbar(IPciConfigAccessor config, PciFunctionSnapshot function, out PciMemoryBar bar)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            bar = default;
            if (!IsAhci(function)) return false;

            uint low = config.Read32(function.Bus, function.Device, function.Function, AhciAbarOffset);
            // BAR5 cannot legally be the low half of a 64-bit BAR because there is
            // no BAR6 to contain the high dword. Fail closed rather than reading
            // beyond the standard BAR window.
            if (((low >> 1) & 0x3u) == 2u) return false;
            return PciBarDecoder.TryDecodeMemory(low, 0, out bar);
        }

        public static bool TryReadNvmeBar0(IPciConfigAccessor config, PciFunctionSnapshot function, out PciMemoryBar bar)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            bar = default;
            if (!IsNvme(function)) return false;

            uint low = config.Read32(function.Bus, function.Device, function.Function, Bar0Offset);
            uint high = 0;
            if (((low >> 1) & 0x3u) == 2u)
                high = config.Read32(function.Bus, function.Device, function.Function, Bar0Offset + 4);
            return PciBarDecoder.TryDecodeMemory(low, high, out bar);
        }

        private static bool IsAhci(PciFunctionSnapshot f) =>
            f.ClassCode == 0x01 && f.Subclass == 0x06 && f.ProgrammingInterface == 0x01;

        private static bool IsNvme(PciFunctionSnapshot f) =>
            f.ClassCode == 0x01 && f.Subclass == 0x08 && f.ProgrammingInterface == 0x02;
    }
}
