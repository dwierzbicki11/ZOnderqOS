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

    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

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

            var registry = new DriverRegistry();
            var failingSpecific = new Driver("specific-fails", d => d.VendorId == 0x8086, false);
            var fallback = new Driver("fallback", d => true);
            registry.Register(failingSpecific);
            registry.Register(fallback);
            Require(registry.TryBind(devices[0], out IDeviceDriver bound), "binding must succeed");
            Require(object.ReferenceEquals(bound, fallback), "failed specific driver must fall through");
            Require(registry.TryBind(devices[0], out IDeviceDriver rebound) && object.ReferenceEquals(bound, rebound), "rebinding must be stable");
            Require(fallback.BindCalls == 1, "already-bound device must not bind twice");

            Console.WriteLine("D1 hardware model tests passed");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
