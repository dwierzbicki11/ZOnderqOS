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
            if (!TryReadReady(config, out ushort command, out ulong initialBar0) ||
                (command & 0x0400) != 0) // PCI COMMAND.INTx Disable
                return false;

            try
            {
                // An INTx line/pin is not an active legacy interrupt route
                // while the device has MSI or MSI-X enabled. Inspect the
                // conventional capability list without writing PCI config.
                if (!TryReadLegacyInterruptMode(config, out ulong initialMode))
                    return false;
                const byte bus = 0, slot = 2, function = 0;
                byte line = config.Read8(bus, slot, function, 0x3C);
                byte pin = config.Read8(bus, slot, function, 0x3D);
                // Line 0/FF is not a usable legacy route in this QEMU profile;
                // the PCI pin must be one of INTA# through INTD#.
                if (line == 0 || line == 0xFF || pin < 1 || pin > 4)
                    return false;
                // A read-only snapshot can become stale if PCI command or
                // routing changes while the diagnostic is sampling it.
                // Fail closed on a changed INTx enable bit, line or pin.
                // This is only a stable PCI snapshot, NOT ISR proof.
                // The route must still belong to the SAME PCI MMIO/DMA
                // mapping. A stable IRQ line with a reprogrammed BAR is not
                // evidence that the originally bound E1000E is still usable.
                if (!TryReadReady(config, out ushort finalCommand, out ulong finalBar0) ||
                    finalCommand != command || finalBar0 != initialBar0 ||
                    config.Read8(bus, slot, function, 0x3C) != line ||
                    config.Read8(bus, slot, function, 0x3D) != pin ||
                    config.Read16(bus, slot, function, 0x04) != command ||
                    !TryReadLegacyInterruptMode(config, out ulong finalMode) ||
                    finalMode != initialMode)
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

        // PCI STATUS.CAP_LIST and conventional capabilities are read-only.
        // Reject malformed lists, loops, duplicate MSI/MSI-X capabilities,
        // enabled message-signaled interrupts, or changes between the two
        // routing snapshots. This is NOT evidence of ISR execution.
        private static bool TryReadLegacyInterruptMode(IPciConfigAccessor config,
            out ulong signature)
        {
            signature = 0;
            const byte bus = 0, slot = 2, function = 0;
            ushort status = config.Read16(bus, slot, function, 0x06);
            if ((status & 0x0010) == 0)
            {
                signature = status;
                return true;
            }

            byte first = config.Read8(bus, slot, function, 0x34);
            if (first == 0)
                return false;
            byte pointer = first;
            ulong visited = 0;
            ulong hash = 14695981039346656037UL;
            bool seenPm = false, seenMsi = false, seenMsiX = false;
            int count = 0;
            while (pointer != 0)
            {
                if (pointer < 0x40 || pointer > 0xFC ||
                    (pointer & 3) != 0 || count++ >= 48)
                    return false;
                ulong bit = 1UL << ((pointer - 0x40) / 4);
                if ((visited & bit) != 0)
                    return false;
                visited |= bit;
                byte id = config.Read8(bus, slot, function, pointer);
                byte next = config.Read8(bus, slot, function, (byte)(pointer + 1));
                if (id == 0 || id == 0xFF)
                    return false;
                ushort control = 0;
                if (id == 0x01) // PCI Power Management capability
                {
                    // PMCSR is at capability+4. Do not wrap a malformed
                    // pointer past the 256-byte conventional PCI header.
                    // A NIC in D1/D2/D3hot cannot be trusted for MMIO/DMA.
                    if (seenPm || pointer > 0xF8)
                        return false;
                    seenPm = true;
                    ushort pmcsr = config.Read16(bus, slot, function,
                        (byte)(pointer + 4));
                    if ((pmcsr & 0x0003) != 0) // Require D0
                        return false;
                    // Only power-state bits are relevant to this route.
                    // PME status is asynchronous and must not destabilize it.
                    control = (ushort)(pmcsr & 0x0003);
                }
                else if (id == 0x05 || id == 0x11)
                {
                    if (id == 0x05 ? seenMsi : seenMsiX)
                        return false;
                    if (id == 0x05) seenMsi = true;
                    else seenMsiX = true;
                    control = config.Read16(bus, slot, function, (byte)(pointer + 2));
                    if ((id == 0x05 && (control & 0x0001) != 0) ||
                        (id == 0x11 && (control & 0x8000) != 0))
                        return false;
                }
                unchecked
                {
                    hash = (hash ^ pointer) * 1099511628211UL;
                    hash = (hash ^ id) * 1099511628211UL;
                    hash = (hash ^ next) * 1099511628211UL;
                    hash = (hash ^ control) * 1099511628211UL;
                }
                pointer = next;
            }
            if (config.Read16(bus, slot, function, 0x06) != status ||
                config.Read8(bus, slot, function, 0x34) != first)
                return false;
            signature = hash ^ ((ulong)status << 48);
            return true;
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
                // I/O, reserved types, prefetchable register BARs and
                // unassigned/all-ones addresses fail. Intel 82574L BAR0
                // contains side-effecting device registers, not cacheable RAM.
                uint low = (uint)config.Read16(bus, slot, function, 0x10) |
                    ((uint)config.Read16(bus, slot, function, 0x12) << 16);
                uint type = low & 0x06u;
                if ((low & 0x09u) != 0 || (type != 0 && type != 0x04u))
                    return false;

                ulong address = (ulong)(low & 0xFFFFFFF0u);
                uint high = 0;
                if (type == 0x04u)
                {
                    high = (uint)config.Read16(bus, slot, function, 0x14) |
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

                // PCI configuration may change during hot-unplug, rebind or
                // reprogramming. Never return a mixed identity/COMMAND/BAR
                // snapshot to a DMA-capable caller. Read-only second pass:
                // no BAR sizing probes and no PCI config writes.
                if (config.Read16(bus, slot, function, 0x00) != 0x8086 ||
                    config.Read16(bus, slot, function, 0x02) != 0x10D3 ||
                    config.Read8(bus, slot, function, 0x0B) != 0x02 ||
                    config.Read8(bus, slot, function, 0x0A) != 0x00 ||
                    config.Read8(bus, slot, function, 0x09) != 0x00 ||
                    (config.Read8(bus, slot, function, 0x0E) & 0x7F) != 0x00 ||
                    config.Read16(bus, slot, function, 0x04) != observed ||
                    ((uint)config.Read16(bus, slot, function, 0x10) |
                     ((uint)config.Read16(bus, slot, function, 0x12) << 16)) != low ||
                    (type == 0x04u &&
                     ((uint)config.Read16(bus, slot, function, 0x14) |
                      ((uint)config.Read16(bus, slot, function, 0x16) << 16)) != high))
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
