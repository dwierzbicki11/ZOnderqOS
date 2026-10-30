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
