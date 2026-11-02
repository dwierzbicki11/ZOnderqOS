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
        /// <summary>
        /// Inspect the legacy PCI INTx routing metadata for the isolated QEMU
        /// E1000E. This is a prerequisite check, NOT evidence of guest ISR
        /// execution, interrupt delivery or DMA completion. MSI/MSI-X devices
        /// must use a different diagnostic instead of this legacy-only gate.
        /// </summary>
        public static bool TryReadLegacyIrqRoute(IPciConfigAccessor config,
            out byte irqLine, out byte irqPin)
        {
            irqLine = 0;
            irqPin = 0;
            if (!TryReadReady(config, out ushort command) ||
                (command & 0x0400) != 0) // PCI COMMAND.INTx Disable
                return false;

            try
            {
                const byte bus = 0, slot = 2, function = 0;
                byte line = config.Read8(bus, slot, function, 0x3C);
                byte pin = config.Read8(bus, slot, function, 0x3D);
                // Line 0/FF is not a usable legacy route in this QEMU profile;
                // the PCI pin must be one of INTA# through INTD#.
                if (line == 0 || line == 0xFF || pin < 1 || pin > 4)
                    return false;
                irqLine = line;
                irqPin = pin;
                return true;
            }
            catch (Exception)
            {
                irqLine = 0;
                irqPin = 0;
                return false;
            }
        }

        // Preserve the original API for callers that only need the command
        // register. The isolated probe also records BAR0 across TX.
        public static bool TryReadReady(IPciConfigAccessor config, out ushort command)
        {
            return TryReadReady(config, out command, out _);
        }

        public static bool TryReadReady(IPciConfigAccessor config,
            out ushort command, out ulong bar0)
        {
            command = 0;
            bar0 = 0;
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

                // BAR0 is the E1000E MMIO aperture. Read the assigned
                // address without writing all-ones sizing probes to PCI.
                // Type 0 (32-bit) and type 2 (64-bit) memory BARs are valid.
                // I/O, reserved types and unassigned/all-ones addresses fail.
                uint low = (uint)config.Read16(bus, slot, function, 0x10) |
                    ((uint)config.Read16(bus, slot, function, 0x12) << 16);
                uint type = low & 0x06u;
                if ((low & 0x01u) != 0 || (type != 0 && type != 0x04u))
                    return false;

                ulong address = (ulong)(low & 0xFFFFFFF0u);
                if (type == 0x04u)
                {
                    uint high = (uint)config.Read16(bus, slot, function, 0x14) |
                        ((uint)config.Read16(bus, slot, function, 0x16) << 16);
                    address |= (ulong)high << 32;
                }
                // An unprogrammed 32-bit BAR may read back as 0xFFFFFFF0.
                // Reject it as well as an all-ones 64-bit BAR; neither is
                // evidence of an assigned, usable MMIO aperture.
                if (address == 0 ||
                    (type == 0 && address == 0xFFFFFFF0UL) ||
                    address == 0xFFFFFFFFFFFFFFF0UL)
                    return false;

                bar0 = address;
                command = observed;
                return true;
            }
            catch (Exception)
            {
                // Missing PCI config I/O or malformed responses fail closed.
                command = 0;
                bar0 = 0;
                return false;
            }
        }
    }
}
