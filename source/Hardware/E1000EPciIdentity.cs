using System;

namespace ZonderqOS.Hardware
{
    /// <summary>
    /// Read-only PCI configuration identity gate for the isolated QEMU E1000E
    /// at 0000:00:02.0. DriverKit binding alone does not establish that the
    /// function still decodes MMIO and permits bus-master DMA.
    /// This is NOT a proof of DMA completion or interrupt handler execution.
    /// </summary>
    public static class E1000EPciIdentity
    {
        public static bool TryReadReady(IPciConfigAccessor config, out ushort command)
        {
            command = 0;
            if (config == null)
                return false;

            try
            {
                // Never probe arbitrary PCI functions or write config space.
                const byte bus = 0, slot = 2, function = 0;
                if (config.Read16(bus, slot, function, 0x00) != 0x8086 ||
                    config.Read16(bus, slot, function, 0x02) != 0x10D3 ||
                    config.Read8(bus, slot, function, 0x0B) != 0x02 ||
                    config.Read8(bus, slot, function, 0x0A) != 0x00 ||
                    config.Read8(bus, slot, function, 0x09) != 0x00 ||
                    (config.Read8(bus, slot, function, 0x0E) & 0x7F) != 0x00)
                    return false;

                ushort observed = config.Read16(bus, slot, function, 0x04);
                // Memory-space decoding and PCI bus mastering are prerequisites
                // for the MMIO/DMA path; the bits alone do not prove TX or RX.
                if ((observed & 0x0006) != 0x0006)
                    return false;

                command = observed;
                return true;
            }
            catch (Exception)
            {
                // Missing PCI config I/O or malformed responses fail closed.
                command = 0;
                return false;
            }
        }
    }
}
