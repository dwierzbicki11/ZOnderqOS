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

    /// <summary>One conventional PCI capability. Presence does not mean the
    /// corresponding interrupt or power-management feature is active.</summary>
    public readonly struct PciCapabilityDescriptor
    {
        public PciCapabilityDescriptor(byte offset, byte id)
        {
            Offset = offset;
            Id = id;
        }
        public byte Offset { get; }
        public byte Id { get; }
    }

    /// <summary>Read-only PCI interrupt capability configuration, NOT proof
    /// of IRQ delivery or an initialized NIC. Table/PBA offsets are not mapped.</summary>
    public readonly struct PciNicInterruptCapabilities
    {
        public PciNicInterruptCapabilities(bool msiPresent, bool msiEnabled,
            bool msi64Bit, bool msixPresent, bool msixEnabled,
            bool msixFunctionMasked, ushort msixVectors, byte tableBir,
            uint tableOffset, byte pbaBir, uint pbaOffset)
        {
            MsiPresent = msiPresent;
            MsiEnabled = msiEnabled;
            Msi64Bit = msi64Bit;
            MsiXPresent = msixPresent;
            MsiXEnabled = msixEnabled;
            MsiXFunctionMasked = msixFunctionMasked;
            MsiXVectors = msixVectors;
            TableBir = tableBir;
            TableOffset = tableOffset;
            PbaBir = pbaBir;
            PbaOffset = pbaOffset;
        }
        public bool MsiPresent { get; }
        public bool MsiEnabled { get; }
        public bool Msi64Bit { get; }
        public bool MsiXPresent { get; }
        public bool MsiXEnabled { get; }
        public bool MsiXFunctionMasked { get; }
        public ushort MsiXVectors { get; }
        public byte TableBir { get; }
        public uint TableOffset { get; }
        public byte PbaBir { get; }
        public uint PbaOffset { get; }
    }

    /// <summary>Read-only PCI PMCSR state, not a request to transition power.
    /// A D0 report is necessary but not sufficient for safe NIC MMIO.</summary>
    public readonly struct PciNicPowerState
    {
        public PciNicPowerState(bool pmPresent, byte state, bool pmeEnabled, bool pmeStatus)
        {
            PmPresent = pmPresent;
            State = state;
            PmeEnabled = pmeEnabled;
            PmeStatus = pmeStatus;
        }
        public bool PmPresent { get; }
        public byte State { get; }
        public bool PmeEnabled { get; }
        public bool PmeStatus { get; }
    }

    /// <summary>
    /// Addresses derived solely from PCI BAR and MSI-X capability metadata.
    /// BAR aperture sizes and actual MMIO accessibility are NOT verified.
    /// Neither the MSI-X table nor PBA is mapped or accessed.
    /// </summary>
    public readonly struct PciNicMsiXBarResolution
    {
        public PciNicMsiXBarResolution(byte tableBir, ulong tableAddress,
            byte pbaBir, ulong pbaAddress)
        {
            TableBir = tableBir;
            TableAddress = tableAddress;
            PbaBir = pbaBir;
            PbaAddress = pbaAddress;
        }
        public byte TableBir { get; }
        public ulong TableAddress { get; }
        public byte PbaBir { get; }
        public ulong PbaAddress { get; }
    }

    public static class PciBarInventory
    {
        /// <summary>
        /// Resolve MSI-X table/PBA offsets only against assigned memory BARs.
        /// Reject I/O BARs, unassigned BARs, the high half of a 64-bit BAR,
        /// and 64-bit address overflow. This does not validate BAR aperture
        /// sizes, map MMIO, program MSI-X or establish IRQ delivery.
        /// </summary>
        public static bool TryResolveRealtek810xEMsiXMemoryBars(IPciConfigAccessor config,
            byte bus, byte device, byte function, out PciNicMsiXBarResolution resolution)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            resolution = default;
            if (!TryReadRealtek810xEInterruptCapabilities(config, bus, device, function,
                    out var irq) || !irq.MsiXPresent ||
                !TryReadRealtek810xE(config, bus, device, function, out var bars))
                return false;

            if (!TryResolveMemoryBar(bars, irq.TableBir, irq.TableOffset, out ulong table) ||
                !TryResolveMemoryBar(bars, irq.PbaBir, irq.PbaOffset, out ulong pba))
                return false;

            resolution = new PciNicMsiXBarResolution(irq.TableBir, table,
                irq.PbaBir, pba);
            return true;
        }

        private static bool TryResolveMemoryBar(List<PciBarDescriptor> bars, byte bir,
            uint offset, out ulong address)
        {
            address = 0;
            for (int i = 0; i < bars.Count; i++)
            {
                PciBarDescriptor bar = bars[i];
                if (bar.Index != bir) continue;
                if (bar.Kind == PciBarKind.Io || bar.Address > ulong.MaxValue - (ulong)offset)
                    return false;
                address = bar.Address + offset;
                return true;
            }
            return false;
        }

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

        /// <summary>Walk the conventional PCI capability linked list without
        /// changing any configuration bits. Invalid pointers, reserved IDs and
        /// cycles fail closed; the output is cleared on failure. MSI/MSI-X
        /// capability presence is not evidence of a working IRQ.</summary>
        public static bool TryReadRealtek810xECapabilities(IPciConfigAccessor config,
            byte bus, byte device, byte function, out List<PciCapabilityDescriptor> capabilities)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            capabilities = new List<PciCapabilityDescriptor>();
            if (!IsSupportedRealtek(config, bus, device, function)) return false;
            ushort status = config.Read16(bus, device, function, 0x06);
            if ((status & 0x0010) == 0) return true;

            byte pointer = config.Read8(bus, device, function, 0x34);
            if (pointer == 0) return false;
            var visited = new bool[256];
            for (int count = 0; pointer != 0; count++)
            {
                // PCI capability pointers are DWORD-aligned by specification.
                // Do not silently mask malformed low bits: doing so could
                // interpret unrelated config bytes as a valid capability.
                byte offset = pointer;
                if ((offset & 3) != 0 || offset < 0x40 || offset > 0xFC ||
                    visited[offset] || count >= 48)
                {
                    capabilities.Clear();
                    return false;
                }
                visited[offset] = true;
                byte id = config.Read8(bus, device, function, offset);
                byte next = config.Read8(bus, device, function, (byte)(offset + 1));
                if (id == 0 || id == 0xFF)
                {
                    capabilities.Clear();
                    return false;
                }
                capabilities.Add(new PciCapabilityDescriptor(offset, id));
                pointer = next;
            }
            return true;
        }

        /// <summary>Read MSI and MSI-X control fields and MSI-X BAR indicators.
        /// No PCI command, MSI enable, MSI-X table or interrupt registers are
        /// written. An enabled bit is firmware configuration, NOT IRQ proof.</summary>
        public static bool TryReadRealtek810xEInterruptCapabilities(IPciConfigAccessor config,
            byte bus, byte device, byte function, out PciNicInterruptCapabilities info)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            info = default;
            if (!TryReadRealtek810xECapabilities(config, bus, device, function, out var caps))
                return false;
            bool msi = false, msiEnabled = false, msi64 = false;
            bool msix = false, msixEnabled = false, msixMasked = false;
            ushort vectors = 0;
            byte tableBir = 0, pbaBir = 0;
            uint tableOffset = 0, pbaOffset = 0;
            for (int i = 0; i < caps.Count; i++)
            {
                byte offset = caps[i].Offset;
                if (caps[i].Id == 0x05)
                {
                    if (msi) return false; // duplicate MSI is malformed
                    msi = true;
                    ushort control = config.Read16(bus, device, function, (byte)(offset + 2));
                    msiEnabled = (control & 0x0001) != 0;
                    msi64 = (control & 0x0080) != 0;
                }
                else if (caps[i].Id == 0x11)
                {
                    if (msix || offset > 0xF4) return false;
                    msix = true;
                    ushort control = config.Read16(bus, device, function, (byte)(offset + 2));
                    uint table = Read32(config, bus, device, function, (byte)(offset + 4));
                    uint pba = Read32(config, bus, device, function, (byte)(offset + 8));
                    tableBir = (byte)(table & 7);
                    pbaBir = (byte)(pba & 7);
                    if (tableBir > 5 || pbaBir > 5) return false;
                    tableOffset = table & 0xFFFFFFF8u;
                    pbaOffset = pba & 0xFFFFFFF8u;
                    vectors = (ushort)((control & 0x07FF) + 1);
                    msixMasked = (control & 0x4000) != 0;
                    msixEnabled = (control & 0x8000) != 0;
                }
            }
            info = new PciNicInterruptCapabilities(msi, msiEnabled, msi64,
                msix, msixEnabled, msixMasked, vectors,
                tableBir, tableOffset, pbaBir, pbaOffset);
            return true;
        }

        /// <summary>Read the power-management capability's PMCSR at cap+4.
        /// No D0 transition, PMCSR write, DMA or MMIO access is performed.</summary>
        public static bool TryReadRealtek810xEPowerState(IPciConfigAccessor config,
            byte bus, byte device, byte function, out PciNicPowerState info)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            info = default;
            if (!TryReadRealtek810xECapabilities(config, bus, device, function, out var caps))
                return false;
            bool present = false;
            ushort pmcsr = 0;
            for (int i = 0; i < caps.Count; i++)
            {
                if (caps[i].Id != 0x01) continue;
                if (present || caps[i].Offset > 0xF8) return false;
                present = true;
                pmcsr = config.Read16(bus, device, function, (byte)(caps[i].Offset + 4));
            }
            info = new PciNicPowerState(present, (byte)(pmcsr & 3),
                (pmcsr & 0x0100) != 0, (pmcsr & 0x8000) != 0);
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
                else
                {
                    // PCI memory BAR type 01/11 is reserved. Do not report
                    // partial resource inventories as valid.
                    bars.Clear();
                    return false;
                }
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
