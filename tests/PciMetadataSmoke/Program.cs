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
barConfig.HeaderType = 1;
int previousReads = barConfig.BarReads;
Check(!PciBarInventory.TryReadRealtek810xE(barConfig, 0, 2, 0, out bars) &&
      barConfig.BarReads == previousReads, "Non-type-0 headers must not read BARs");
barConfig.HeaderType = 0;
barConfig.VendorId = 0x1234;
Check(!PciBarInventory.TryReadRealtek810xE(barConfig, 0, 2, 0, out bars) &&
      barConfig.BarReads == previousReads, "Non-Realtek function must not read BARs");
barConfig.VendorId = 0x10EC;
Check(!PciBarInventory.TryReadRealtek810xE(barConfig, 0, 32, 0, out bars),
    "Invalid PCI device number must fail closed");
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
    public ushort VendorId { get; set; } = 0x10EC;
    public byte HeaderType { get; set; }
    public int BarReads { get; private set; }
    public void SetBar(int index, uint value) => values[index] = value;

    public byte Read8(byte bus, byte device, byte function, byte offset)
    {
        if (bus != 0 || device != 2 || function != 0) throw new Exception("Unexpected BDF");
        return offset switch
        {
            0x0B => 0x02,
            0x0A => 0x00,
            0x0E => HeaderType,
            _ => throw new Exception("Unexpected byte config read")
        };
    }

    public ushort Read16(byte bus, byte device, byte function, byte offset)
    {
        if (bus != 0 || device != 2 || function != 0) throw new Exception("Unexpected BDF");
        if (offset == 0x00) return VendorId;
        if (offset == 0x02) return 0x8136;
        if (offset < 0x10 || offset > 0x26 || (offset & 1) != 0)
            throw new Exception("Unexpected BAR offset");
        BarReads++;
        uint value = values[(offset - 0x10) / 4];
        return (offset & 2) != 0 ? (ushort)(value >> 16) : (ushort)value;
    }
}
