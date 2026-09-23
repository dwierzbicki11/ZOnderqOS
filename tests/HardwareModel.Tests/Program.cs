using System;
using System.Collections.Generic;
using ZonderqOS.Hardware;

static class Program
{
    private sealed class Driver : IDeviceDriver
    {
        private readonly Func<DeviceDescriptor, bool> supports;
        private readonly bool bind;
        public Driver(string name, Func<DeviceDescriptor, bool> supports, bool bind = true) { Name = name; this.supports = supports; this.bind = bind; }
        public string Name { get; }
        public int BindCalls { get; private set; }
        public bool Supports(DeviceDescriptor device) => supports(device);
        public bool Bind(DeviceDescriptor device) { BindCalls++; return bind; }
    }

    private sealed class DiscoverySource : IPciDiscoverySource
    {
        private readonly IEnumerable<PciFunctionSnapshot>? snapshots;
        public DiscoverySource(IEnumerable<PciFunctionSnapshot>? snapshots) { this.snapshots = snapshots; }
        public IEnumerable<PciFunctionSnapshot> Discover() => snapshots!;
    }

    private sealed class ConfigAccessor : IPciConfigAccessor
    {
        private readonly Dictionary<string, uint> values = new Dictionary<string, uint>();
        private static string Key(byte b, byte d, byte f, byte o) => b + ":" + d + ":" + f + ":" + o;
        public void Set8(byte b, byte d, byte f, byte o, byte value) => values[Key(b,d,f,o)] = value;
        public void Set16(byte b, byte d, byte f, byte o, ushort value) => values[Key(b,d,f,o)] = value;
        public void Set32(byte b, byte d, byte f, byte o, uint value) => values[Key(b,d,f,o)] = value;
        public byte Read8(byte b, byte d, byte f, byte o) => values.TryGetValue(Key(b,d,f,o), out uint v) ? (byte)v : (byte)0;
        public ushort Read16(byte b, byte d, byte f, byte o) => values.TryGetValue(Key(b,d,f,o), out uint v) ? (ushort)v : (ushort)0xFFFF;
        public uint Read32(byte b, byte d, byte f, byte o)
        {
            if ((o & 3) != 0) throw new ArgumentOutOfRangeException(nameof(o));
            return values.TryGetValue(Key(b,d,f,o), out uint v) ? v : 0xFFFFFFFFu;
        }
    }

    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    static void AddFunction(ConfigAccessor c, byte b, byte d, byte f, ushort vendor, ushort device, byte cls, byte sub, byte pi, byte header = 0)
    {
        c.Set16(b,d,f,0x00,vendor); c.Set16(b,d,f,0x02,device); c.Set8(b,d,f,0x09,pi); c.Set8(b,d,f,0x0A,sub); c.Set8(b,d,f,0x0B,cls); c.Set8(b,d,f,0x0E,header);
    }

    static int Main()
    {
        try
        {
            var input = new[] {
                new PciFunctionSnapshot(2, 0, 0, 0x1234, 2, 2, 0, 0),
                new PciFunctionSnapshot(0, 31, 7, 0xFFFF, 0, 0, 0, 0),
                new PciFunctionSnapshot(0, 1, 1, 0x8086, 1, 1, 6, 1),
                new PciFunctionSnapshot(0, 1, 0, 0x8086, 3, 1, 6, 1)
            };
            List<DeviceDescriptor> devices = new PciDiscoveryService(new DiscoverySource(input)).DiscoverDevices();
            Require(devices.Count == 3, "sentinel must be omitted");
            Require(devices[0].Id.Address == "00:01.0" && devices[1].Id.Address == "00:01.1" && devices[2].Id.Address == "02:00.0", "BDF order must be deterministic");

            bool duplicateRejected = false;
            try { new PciDiscoveryService(new DiscoverySource(new[] { input[2], input[2] })).DiscoverDevices(); } catch (InvalidOperationException) { duplicateRejected = true; }
            Require(duplicateRejected, "duplicate BDF must be rejected");
            bool nullDiscoveryRejected = false;
            try { new PciDiscoveryService(new DiscoverySource(null)).DiscoverDevices(); } catch (InvalidOperationException) { nullDiscoveryRejected = true; }
            Require(nullDiscoveryRejected, "null HAL discovery result must be rejected");
            bool invalidFunctionRejected = false;
            try { _ = new PciFunctionSnapshot(0, 0, 8, 1, 1, 0, 0, 0); } catch (ArgumentOutOfRangeException) { invalidFunctionRejected = true; }
            Require(invalidFunctionRejected, "invalid function must be rejected");

            var config = new ConfigAccessor();
            AddFunction(config, 0, 1, 0, 0x1111, 1, 0x06, 0x04, 0); config.Set8(0,1,0,0x19,2);
            AddFunction(config, 0, 2, 0, 0x2222, 2, 0x02, 0, 0, 0x80);
            AddFunction(config, 0, 2, 1, 0x2222, 3, 0x02, 0, 1);
            AddFunction(config, 2, 0, 0, 0x3333, 4, 0x01, 0x06, 1);
            List<DeviceDescriptor> topology = new PciDiscoveryService(new PciConfigDiscoverySource(config)).DiscoverDevices();
            Require(topology.Count == 4, "topology walker must discover root, multifunction and bridged functions");
            Require(topology[0].Id.Address == "00:01.0" && topology[1].Id.Address == "00:02.0" && topology[2].Id.Address == "00:02.1" && topology[3].Id.Address == "02:00.0", "topology discovery BDF mismatch");

            // D2.2 config boundary: dword BAR reads preserve all bits and reject
            // unaligned accesses rather than silently aliasing another register.
            config.Set32(0, 2, 0, 0x10, 0xFEDC0004u);
            Require(config.Read32(0, 2, 0, 0x10) == 0xFEDC0004u, "PCI dword config read mismatch");
            bool unalignedDwordRejected = false;
            try { _ = config.Read32(0, 2, 0, 0x11); } catch (ArgumentOutOfRangeException) { unalignedDwordRejected = true; }
            Require(unalignedDwordRejected, "unaligned PCI dword read must be rejected");

            var multiRoot = new ConfigAccessor();
            AddFunction(multiRoot, 0, 0, 0, 0x8086, 0x1000, 0x06, 0x00, 0, 0x80);
            AddFunction(multiRoot, 0, 0, 2, 0x8086, 0x1002, 0x06, 0x00, 0);
            AddFunction(multiRoot, 2, 3, 0, 0x1AF4, 0x1000, 0x02, 0x00, 0);
            List<DeviceDescriptor> roots = new PciDiscoveryService(new PciConfigDiscoverySource(multiRoot)).DiscoverDevices();
            Require(roots.Count == 3, "multifunction host controller functions and devices on every present root bus must all be discovered");
            Require(roots[0].Id.Address == "00:00.0" && roots[1].Id.Address == "00:00.2" && roots[2].Id.Address == "02:03.0", "multifunction host root discovery mismatch");

            var registry = new DriverRegistry();
            var failingSpecific = new Driver("specific-fails", d => d.VendorId == 0x8086, false);
            var fallback = new Driver("fallback", d => true);
            registry.Register(failingSpecific); registry.Register(fallback);
            Require(registry.TryBind(devices[0], out IDeviceDriver bound), "binding must succeed");
            Require(object.ReferenceEquals(bound, fallback), "failed specific driver must fall through");
            Require(registry.TryBind(devices[0], out IDeviceDriver rebound) && object.ReferenceEquals(bound, rebound), "rebinding must be stable");
            Require(fallback.BindCalls == 1, "already-bound device must not bind twice");

            // D2.1: storage discovery/binding must be strict and HAL-neutral.
            var ahci = new DeviceDescriptor(new DeviceId("pci", "00:1F.2"), 0x8086, 0x2922, 0x01, 0x06, 0x01);
            var nvme = new DeviceDescriptor(new DeviceId("pci", "00:04.0"), 0x1B36, 0x0010, 0x01, 0x08, 0x02);
            var legacySata = new DeviceDescriptor(new DeviceId("pci", "00:1F.1"), 0x8086, 0x1234, 0x01, 0x06, 0x00);
            var unknownNvm = new DeviceDescriptor(new DeviceId("pci", "00:05.0"), 0x1234, 0x5678, 0x01, 0x08, 0x00);
            Require(StorageControllerClassifier.TryClassify(ahci, out StorageControllerKind ahciKind) && ahciKind == StorageControllerKind.Ahci, "AHCI class/subclass/PI must classify");
            Require(StorageControllerClassifier.TryClassify(nvme, out StorageControllerKind nvmeKind) && nvmeKind == StorageControllerKind.Nvme, "NVMe class/subclass/PI must classify");
            Require(!StorageControllerClassifier.TryClassify(legacySata, out _), "non-AHCI SATA PI must not bind as AHCI");
            Require(!StorageControllerClassifier.TryClassify(unknownNvm, out _), "unknown NVM PI must not bind as NVMe");
            var storageRegistry = new DriverRegistry();
            storageRegistry.Register(new AhciControllerDriver());
            storageRegistry.Register(new NvmeControllerDriver());
            Require(storageRegistry.TryBind(ahci, out IDeviceDriver ahciDriver) && ahciDriver.Name == "ahci", "AHCI binding mismatch");
            Require(storageRegistry.TryBind(nvme, out IDeviceDriver nvmeDriver) && nvmeDriver.Name == "nvme", "NVMe binding mismatch");
            Require(!storageRegistry.TryBind(legacySata, out _), "legacy SATA must remain unbound in D2.1");
            Require(!storageRegistry.TryBind(unknownNvm, out _), "unknown NVM must remain unbound in D2.1");

            Console.WriteLine("D1/D2.1/D2.2 hardware model tests passed");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
