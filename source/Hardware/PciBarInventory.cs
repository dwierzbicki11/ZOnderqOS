using System;
using System.Collections.Generic;

namespace ZonderqOS.Hardware
{
    /// <summary>Read-only PCI BAR decoding for RTL810xE 10EC:8136.
    /// Never probes BAR size (which would require writes), enables bus mastering,
    /// maps MMIO, or claims the NIC is initialized.</summary>
    public enum PciBarKind { Io, Memory32, Memory64 }

    public readonly struct PciBarDescriptor
    {
        public PciBarDescriptor(byte index, PciBarKind kind, ulong address, bool prefetchable)
        {
            Index = index;
            Kind = kind;
            Address = address;
            Prefetchable = prefetchable;
        }

        public byte Index { get; }
        public PciBarKind Kind { get; }
        public ulong Address { get; }
        public bool Prefetchable { get; }
    }

    public readonly struct PciNicConfigState
    {
        public PciNicConfigState(ushort command, ushort status, byte irqLine, byte irqPin)
        {
            Command = command;
            Status = status;
            IrqLine = irqLine;
            IrqPin = irqPin;
        }
        public ushort Command { get; }
        public ushort Status { get; }
        public byte IrqLine { get; }
        public byte IrqPin { get; }
        public bool IoEnabled => (Command & 0x0001) != 0;
        public bool MemoryEnabled => (Command & 0x0002) != 0;
        public bool BusMasterEnabled => (Command & 0x0004) != 0;
        public bool LegacyInterruptDisabled => (Command & 0x0400) != 0;
    }

    public static class PciBarInventory
    {
        /// <summary>Read-only PCI command/status and legacy IRQ routing.
        /// IRQ line/pin are NOT proof of a working interrupt or MSI/MSI-X.</summary>
        public static bool TryReadRealtek810xEState(IPciConfigAccessor config, byte bus,
            byte device, byte function, out PciNicConfigState state)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            state = default;
            if (!IsSupportedRealtek(config, bus, device, function)) return false;
            state = new PciNicConfigState(
                config.Read16(bus, device, function, 0x04),
                config.Read16(bus, device, function, 0x06),
                config.Read8(bus, device, function, 0x3C),
                config.Read8(bus, device, function, 0x3D));
            return true;
        }

        private static bool IsSupportedRealtek(IPciConfigAccessor config, byte bus,
            byte device, byte function)
        {
            return device <= 31 && function <= 7 &&
                config.Read16(bus, device, function, 0x00) == 0x10EC &&
                config.Read16(bus, device, function, 0x02) == 0x8136 &&
                config.Read8(bus, device, function, 0x0B) == 0x02 &&
                config.Read8(bus, device, function, 0x0A) == 0x00 &&
                (config.Read8(bus, device, function, 0x0E) & 0x7F) == 0;
        }

        public static bool TryReadRealtek810xE(IPciConfigAccessor config, byte bus,
            byte device, byte function, out List<PciBarDescriptor> bars)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            bars = new List<PciBarDescriptor>();
            if (!IsSupportedRealtek(config, bus, device, function)) return false;

            for (byte index = 0; index < 6; index++)
            {
                byte offset = (byte)(0x10 + index * 4);
                uint low = Read32(config, bus, device, function, offset);
                // Zero means unconfigured. All ones is not a usable BAR.
                if (low == 0 || low == uint.MaxValue) continue;
                if ((low & 1) != 0)
                {
                    ulong address = low & 0xFFFFFFFCu;
                    if (address != 0)
                        bars.Add(new PciBarDescriptor(index, PciBarKind.Io, address, false));
                    continue;
                }

                uint type = (low >> 1) & 3;
                if (type == 0)
                {
                    ulong address = low & 0xFFFFFFF0u;
                    if (address != 0)
                        bars.Add(new PciBarDescriptor(index, PciBarKind.Memory32,
                            address, (low & 8) != 0));
                }
                else if (type == 2)
                {
                    // A 64-bit BAR consumes the next register. Never mistake
                    // its high half for an independent I/O or MMIO BAR.
                    if (index == 5)
                    {
                        bars.Clear();
                        return false;
                    }
                    uint high = Read32(config, bus, device, function, (byte)(offset + 4));
                    ulong address = ((ulong)high << 32) | (low & 0xFFFFFFF0u);
                    if (address != 0 && address != 0xFFFFFFFFFFFFFFF0UL)
                        bars.Add(new PciBarDescriptor(index, PciBarKind.Memory64,
                            address, (low & 8) != 0));
                    index++;
                }
                // Reserved memory types are not decoded as usable resources.
            }
            return true;
        }

        private static uint Read32(IPciConfigAccessor config, byte bus,
            byte device, byte function, byte offset)
        {
            // The existing HAL exposes only aligned read-only 16-bit accesses.
            return (uint)config.Read16(bus, device, function, offset) |
                   ((uint)config.Read16(bus, device, function, (byte)(offset + 2)) << 16);
        }
    }
}
