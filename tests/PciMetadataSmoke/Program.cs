using System;
using ZonderqOS.Hardware;

// Actual C# execution of the same PCI walker and metadata types used by
// ZonderqOS. This is a synthetic config-space test, NOT a physical NIC proof.
var config = new FakePciConfig();
var devices = new PciDiscoveryService(new PciConfigDiscoverySource(config)).DiscoverDevices();
Check(devices.Count == 3, "Expected host bridge and two network functions");
var realtek = devices.Find(d => d.VendorId == 0x10EC && d.DeviceId == 0x8136);
Check(realtek != null, "RTL810xE PCI function missing");
Check(realtek.Id.Address == "00:02.0", "RTL810xE BDF changed");
Check(realtek.RevisionId == 0x06, "RTL810xE silicon revision not forwarded");
Check(realtek.SubsystemVendorId == 0x1043 && realtek.SubsystemDeviceId == 0x200F,
    "RTL810xE subsystem vendor/device not forwarded");
Check(realtek.ClassCode == 0x02 && realtek.Subclass == 0x00, "RTL810xE class changed");
var unusual = devices.Find(d => d.VendorId == 0x1234 && d.DeviceId == 0x5678);
Check(unusual != null && unusual.RevisionId == 0x04, "Non-type-0 network revision lost");
Check(unusual.SubsystemVendorId == 0xFFFF && unusual.SubsystemDeviceId == 0xFFFF,
    "Non-type-0 header must not fabricate subsystem IDs");
var host = devices.Find(d => d.Id.Address == "00:00.0");
Check(host != null && host.RevisionId == 0xFF && host.SubsystemVendorId == 0xFFFF,
    "Non-network PCI function must keep unknown metadata");
Check(config.SubsystemReads == 2, "Only type-0 network function may read subsystem registers");
var legacy = new DeviceDescriptor(new DeviceId("pci", "00:01.0"), 0x8086, 0x100E, 0x02, 0x00, 0x00);
Check(legacy.RevisionId == 0xFF && legacy.SubsystemVendorId == 0xFFFF,
    "Legacy device descriptor construction must stay compatible");

// Synthetic BAR register fixtures: these execute the production C# decoder,
// not a NIC driver and not real laptop hardware.
var barConfig = new FakeBarConfig();
barConfig.SetBar(0, 0xFEBF0000u);   // non-prefetchable MMIO32
barConfig.SetBar(1, 0x0000C001u);   // I/O at C000
barConfig.SetBar(2, 0x0000000Cu);   // prefetchable 64-bit BAR
barConfig.SetBar(3, 0x00000001u);   // high half, base 0x100000000
Check(PciBarInventory.TryReadRealtek810xE(barConfig, 0, 2, 0, out var bars),
    "Expected type-0 RTL810xE BAR inventory");
Check(bars.Count == 3, "BAR pair must count as one resource");
Check(bars[0].Index == 0 && bars[0].Kind == PciBarKind.Memory32 &&
      bars[0].Address == 0xFEBF0000UL && !bars[0].Prefetchable,
    "MMIO32 BAR decoding failed");
Check(bars[1].Index == 1 && bars[1].Kind == PciBarKind.Io &&
      bars[1].Address == 0xC000UL, "I/O BAR masking failed");
Check(bars[2].Index == 2 && bars[2].Kind == PciBarKind.Memory64 &&
      bars[2].Address == 0x100000000UL && bars[2].Prefetchable,
    "64-bit BAR high half or prefetch bit decoding failed");
Check(barConfig.BarReads == 12, "BAR inventory must read six dwords only");
Check(PciBarInventory.TryReadRealtek810xEState(barConfig, 0, 2, 0, out var state),
    "Expected read-only Realtek PCI state");
Check(state.Command == 0x0007 && state.Status == 0x0010 &&
      state.IoEnabled && state.MemoryEnabled && state.BusMasterEnabled &&
      !state.LegacyInterruptDisabled && state.IrqLine == 11 && state.IrqPin == 1,
    "PCI command/status/IRQ decode failed");
barConfig.SetBar(0, 0);
barConfig.SetBar(1, 0xFFFFFFFFu);
barConfig.SetBar(2, 0);
barConfig.SetBar(3, 0);
Check(PciBarInventory.TryReadRealtek810xE(barConfig, 0, 2, 0, out bars) &&
      bars.Count == 0, "Unassigned BARs must not be reported as usable");
barConfig.SetBar(5, 0x00000004u);
Check(!PciBarInventory.TryReadRealtek810xE(barConfig, 0, 2, 0, out bars) &&
      bars.Count == 0, "Truncated 64-bit BAR must fail closed");
barConfig.SetBar(5, 0);
barConfig.SetBar(0, 0x00000002u); // reserved memory BAR type 01
Check(!PciBarInventory.TryReadRealtek810xE(barConfig, 0, 2, 0, out bars) &&
      bars.Count == 0, "Reserved memory BAR encoding must fail closed");
barConfig.SetBar(0, 0);

// Conventional PCI capabilities: read-only PM -> MSI -> MSI-X linked list.
barConfig.SetCapability(0x40, 0x01, 0x50);
barConfig.SetCapability(0x50, 0x05, 0x60);
barConfig.SetCapability(0x60, 0x11, 0);
Check(PciBarInventory.TryReadRealtek810xECapabilities(barConfig, 0, 2, 0, out var caps),
    "Expected read-only RTL810xE capability walk");
Check(caps.Count == 3 && caps[0].Id == 0x01 && caps[0].Offset == 0x40 &&
      caps[1].Id == 0x05 && caps[1].Offset == 0x50 &&
      caps[2].Id == 0x11 && caps[2].Offset == 0x60,
    "PM/MSI/MSI-X capability list decoding failed");
barConfig.SetCapability(0x60, 0x11, 0x40); // cycle
Check(!PciBarInventory.TryReadRealtek810xECapabilities(barConfig, 0, 2, 0, out caps) &&
      caps.Count == 0, "Cyclic capability list must fail closed without partial data");
barConfig.SetCapability(0x60, 0x11, 0);
barConfig.CapabilityPointer = 0x30;
Check(!PciBarInventory.TryReadRealtek810xECapabilities(barConfig, 0, 2, 0, out caps) &&
      caps.Count == 0, "Out-of-range capability pointer must fail closed");
barConfig.CapabilityPointer = 0x41; // malformed unaligned head
Check(!PciBarInventory.TryReadRealtek810xECapabilities(barConfig, 0, 2, 0, out caps) &&
      caps.Count == 0, "Unaligned capability head must fail closed");
barConfig.CapabilityPointer = 0x40;
barConfig.SetCapability(0x50, 0x05, 0x61); // malformed unaligned next
Check(!PciBarInventory.TryReadRealtek810xECapabilities(barConfig, 0, 2, 0, out caps) &&
      caps.Count == 0, "Unaligned capability next pointer must fail closed");
barConfig.SetCapability(0x50, 0x05, 0x60);
barConfig.CapabilityPointer = 0x40;
barConfig.SetCapability(0x50, 0xFF, 0);
Check(!PciBarInventory.TryReadRealtek810xECapabilities(barConfig, 0, 2, 0, out caps) &&
      caps.Count == 0, "Invalid capability ID must fail closed");
barConfig.SetCapability(0x50, 0x05, 0x60);
barConfig.CapabilityListPresent = false;
int capReadsBefore = barConfig.CapabilityReads;
Check(PciBarInventory.TryReadRealtek810xECapabilities(barConfig, 0, 2, 0, out caps) &&
      caps.Count == 0 && barConfig.CapabilityReads == capReadsBefore,
    "Status without capability-list flag must not walk pointers");
barConfig.CapabilityListPresent = true;
// Read-only MSI and MSI-X controls. The bit being set is NOT an IRQ proof.
barConfig.SetCapability(0x50, 0x05, 0x60);
barConfig.SetMsiControl(0x0081); // MSI enabled + 64-bit capable
barConfig.SetMsixControl(0xC003); // enabled, function masked, 4 vectors
barConfig.SetMsixTable(0x00002002u, 0x00003004u); // BAR2 / BAR4
Check(PciBarInventory.TryReadRealtek810xEInterruptCapabilities(barConfig, 0, 2, 0,
      out var irqInfo), "Expected read-only interrupt capability inventory");
Check(irqInfo.MsiPresent && irqInfo.MsiEnabled && irqInfo.Msi64Bit &&
      irqInfo.MsiXPresent && irqInfo.MsiXEnabled && irqInfo.MsiXFunctionMasked &&
      irqInfo.MsiXVectors == 4 && irqInfo.TableBir == 2 &&
      irqInfo.TableOffset == 0x2000 && irqInfo.PbaBir == 4 &&
      irqInfo.PbaOffset == 0x3000,
    "MSI/MSI-X control, vector count or table/PBA BAR decode failed");
// MSI-X table and PBA must point to assigned MEMORY BARs. A 64-bit
// BAR's high half is never an independently addressable BAR.
// Earlier negative BAR fixtures cleared BAR2/3: explicitly restore them.
barConfig.SetBar(2, 0x0000000Cu);
barConfig.SetBar(3, 0x00000001u);
Check(!PciBarInventory.TryResolveRealtek810xEMsiXMemoryBars(barConfig, 0, 2, 0,
      out var msixBars), "Unassigned PBA BAR must fail closed");
barConfig.SetBar(4, 0xFEBE0000u);
Check(PciBarInventory.TryResolveRealtek810xEMsiXMemoryBars(barConfig, 0, 2, 0,
      out msixBars) && msixBars.TableBir == 2 &&
      msixBars.TableAddress == 0x100002000UL &&
      msixBars.PbaBir == 4 && msixBars.PbaAddress == 0xFEBE3000UL,
    "MSI-X memory BAR address resolution failed");
barConfig.SetMsixTable(0x00002003u, 0x00003004u); // BAR3 is high half of BAR2
Check(!PciBarInventory.TryResolveRealtek810xEMsiXMemoryBars(barConfig, 0, 2, 0,
      out msixBars), "64-bit BAR high half must never resolve MSI-X table");
barConfig.SetMsixTable(0x00002002u, 0x00003004u);
barConfig.SetBar(4, 0x0000C001u); // I/O BAR is invalid for MSI-X PBA
Check(!PciBarInventory.TryResolveRealtek810xEMsiXMemoryBars(barConfig, 0, 2, 0,
      out msixBars), "MSI-X PBA must not resolve through I/O BAR");
barConfig.SetBar(4, 0xFEBE0000u);
barConfig.SetBar(2, 0xFFFFF00Cu);
barConfig.SetBar(3, 0xFFFFFFFFu);
Check(!PciBarInventory.TryResolveRealtek810xEMsiXMemoryBars(barConfig, 0, 2, 0,
      out msixBars), "MSI-X table address overflow must fail closed");
barConfig.SetBar(2, 0x0000000Cu);
barConfig.SetBar(3, 0x00000001u);
barConfig.SetBar(4, 0);
barConfig.SetMsixTable(0x00002007u, 0x00003004u); // invalid BAR7
Check(!PciBarInventory.TryReadRealtek810xEInterruptCapabilities(barConfig, 0, 2, 0,
      out irqInfo), "MSI-X BAR indicator 6/7 must fail closed");
barConfig.SetMsixTable(0x00002002u, 0x00003004u);
// PMCSR D3hot + PME enabled/status. Do not transition to D0.
barConfig.SetPmcsr(0x8103);
Check(PciBarInventory.TryReadRealtek810xEPowerState(barConfig, 0, 2, 0,
      out var powerInfo) && powerInfo.PmPresent && powerInfo.State == 3 &&
      powerInfo.PmeEnabled && powerInfo.PmeStatus,
    "Read-only PMCSR power-state decode failed");
barConfig.CapabilityPointer = 0x50; // no PM capability
Check(PciBarInventory.TryReadRealtek810xEPowerState(barConfig, 0, 2, 0,
      out powerInfo) && !powerInfo.PmPresent,
    "Absent PM capability must not fabricate a power state");
barConfig.CapabilityPointer = 0x40;
barConfig.HeaderType = 1;
Check(!PciBarInventory.TryReadRealtek810xEState(barConfig, 0, 2, 0, out state),
    "Non-type-0 PCI state must fail closed");
Check(!PciBarInventory.TryReadRealtek810xECapabilities(barConfig, 0, 2, 0, out caps),
    "Non-type-0 PCI capability list must fail closed");
int previousReads = barConfig.BarReads;
Check(!PciBarInventory.TryReadRealtek810xE(barConfig, 0, 2, 0, out bars) &&
      barConfig.BarReads == previousReads, "Non-type-0 headers must not read BARs");
barConfig.HeaderType = 0;
barConfig.VendorId = 0x1234;
Check(!PciBarInventory.TryReadRealtek810xEState(barConfig, 0, 2, 0, out state),
    "Non-Realtek PCI state must fail closed");
Check(!PciBarInventory.TryReadRealtek810xE(barConfig, 0, 2, 0, out bars) &&
      barConfig.BarReads == previousReads, "Non-Realtek function must not read BARs");
barConfig.VendorId = 0x10EC;
Check(!PciBarInventory.TryReadRealtek810xE(barConfig, 0, 32, 0, out bars),
    "Invalid PCI device number must fail closed");
Console.WriteLine("[NET3095-PCI-PM] PASS C# read-only PMCSR D3hot/PME flags and absent-PM guard; no power transition");
Console.WriteLine("[NET3095-PCI-MSIX-BAR] PASS C# MSI-X memory BAR resolution and high-half/IO/unassigned/overflow guards; no MMIO or IRQ proof");
Console.WriteLine("[NET3095-PCI-IRQ] PASS C# read-only MSI/MSI-X control, vector count, BAR indicators and malformed BAR guards; no IRQ delivery proof");
Console.WriteLine("[NET3095-PCI-CAPS] PASS C# read-only PM/MSI/MSI-X linked list, cycles, reserved IDs, pointers and fail-closed guards");
Console.WriteLine("[NET3095-PCI-BAR] PASS C# read-only MMIO32/IO/MMIO64, masking, header/identity guards and fail-closed BAR pairs");

Console.WriteLine("[NET3095-PCI-METADATA] PASS C# read-only revision/subsystem propagation, header guard and legacy compatibility");

static void Check(bool ok, string message)
{
    if (!ok) throw new Exception(message);
}

sealed class FakePciConfig : IPciConfigAccessor
{
    public int SubsystemReads { get; private set; }

    public ushort Read16(byte bus, byte device, byte function, byte offset)
    {
        if (bus != 0 || function != 0) return 0xFFFF;
        if (offset == 0x00)
            return device switch { 0 => 0x8086, 2 => 0x10EC, 4 => 0x1234, _ => 0xFFFF };
        if (offset == 0x02)
            return device switch { 0 => 0x1234, 2 => 0x8136, 4 => 0x5678, _ => 0xFFFF };
        if (offset == 0x2C || offset == 0x2E)
        {
            if (device != 2) throw new Exception("Subsystem read outside type-0 network header");
            SubsystemReads++;
            return offset == 0x2C ? (ushort)0x1043 : (ushort)0x200F;
        }
        throw new Exception($"Unexpected PCI Read16 offset 0x{offset:X2}");
    }

    public byte Read8(byte bus, byte device, byte function, byte offset)
    {
        if (bus != 0 || function != 0 || (device != 0 && device != 2 && device != 4))
            throw new Exception("Unexpected PCI Read8 target");
        return offset switch
        {
            0x08 => device == 2 ? (byte)0x06 : (byte)0x04,
            0x09 => 0,
            0x0A => 0,
            0x0B => device == 0 ? (byte)0x06 : (byte)0x02,
            0x0E => device == 4 ? (byte)0x01 : (byte)0x00,
            _ => throw new Exception($"Unexpected PCI Read8 offset 0x{offset:X2}")
        };
    }
}

// Read-only synthetic PCI config. No write API exists on IPciConfigAccessor.
sealed class FakeBarConfig : IPciConfigAccessor
{
    private readonly uint[] values = new uint[6];
    private readonly System.Collections.Generic.Dictionary<byte, (byte Id, byte Next)> caps = new();
    public byte CapabilityPointer { get; set; } = 0x40;
    public bool CapabilityListPresent { get; set; } = true;
    public int CapabilityReads { get; private set; }
    public void SetCapability(byte offset, byte id, byte next) => caps[offset] = (id, next);
    private ushort pmcsr;
    public void SetPmcsr(ushort value) => pmcsr = value;
    private ushort msiControl;
    private ushort msixControl;
    private uint msixTable;
    private uint msixPba;
    public void SetMsiControl(ushort value) => msiControl = value;
    public void SetMsixControl(ushort value) => msixControl = value;
    public void SetMsixTable(uint table, uint pba) { msixTable = table; msixPba = pba; }
    public ushort VendorId { get; set; } = 0x10EC;
    public byte HeaderType { get; set; }
    public int BarReads { get; private set; }
    public void SetBar(int index, uint value) => values[index] = value;

    public byte Read8(byte bus, byte device, byte function, byte offset)
    {
        if (bus != 0 || device != 2 || function != 0) throw new Exception("Unexpected BDF");
        if (offset == 0x34)
        {
            CapabilityReads++;
            return CapabilityPointer;
        }
        byte aligned = (byte)(offset & 0xFC);
        if (caps.TryGetValue(aligned, out var entry) &&
            (offset == aligned || offset == aligned + 1))
        {
            CapabilityReads++;
            return offset == aligned ? entry.Id : entry.Next;
        }
        return offset switch
        {
            0x0B => 0x02,
            0x0A => 0x00,
            0x0E => HeaderType,
            0x3C => 11,
            0x3D => 1,
            _ => throw new Exception("Unexpected byte config read")
        };
    }

    public ushort Read16(byte bus, byte device, byte function, byte offset)
    {
        if (bus != 0 || device != 2 || function != 0) throw new Exception("Unexpected BDF");
        if (offset == 0x00) return VendorId;
        if (offset == 0x02) return 0x8136;
        if (offset == 0x04) return 0x0007;
        if (offset == 0x06) return CapabilityListPresent ? (ushort)0x0010 : (ushort)0;
        if (offset == 0x44) return pmcsr;
        if (offset == 0x52) return msiControl;
        if (offset == 0x62) return msixControl;
        if (offset >= 0x64 && offset <= 0x6A && (offset & 1) == 0)
        {
            uint word = offset < 0x68 ? msixTable : msixPba;
            return (offset & 2) != 0 ? (ushort)(word >> 16) : (ushort)word;
        }
        if (offset < 0x10 || offset > 0x26 || (offset & 1) != 0)
            throw new Exception("Unexpected BAR offset");
        BarReads++;
        uint value = values[(offset - 0x10) / 4];
        return (offset & 2) != 0 ? (ushort)(value >> 16) : (ushort)value;
    }
}
