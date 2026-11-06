using System;
using System.Collections.Generic;

namespace ZonderqOS.Hardware
{
    /// <summary>
    /// Minimal PCI configuration-space read boundary. Architecture-specific HAL
    /// code implements only these reads; topology walking stays system-owned.
    /// </summary>
    public interface IPciConfigAccessor
    {
        byte Read8(byte bus, byte device, byte function, byte offset);
        ushort Read16(byte bus, byte device, byte function, byte offset);
    }

    /// <summary>
    /// PCI topology walker shared by legacy config-I/O and PCIe ECAM backends.
    /// It discovers host-controller root buses, follows PCI-to-PCI bridge
    /// secondary buses, honours multifunction headers, and never scans a bus twice.
    /// </summary>
    public sealed class PciConfigDiscoverySource : IPciDiscoverySource
    {
        private const ushort MissingVendor = 0xFFFF;
        private const byte HeaderTypeOffset = 0x0E;
        private const byte MultifunctionBit = 0x80;
        private const byte BridgeClass = 0x06;
        private const byte PciBridgeSubclass = 0x04;
        private const byte SecondaryBusOffset = 0x19;

        private readonly IPciConfigAccessor config;

        public PciConfigDiscoverySource(IPciConfigAccessor config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public IEnumerable<PciFunctionSnapshot> Discover()
        {
            var result = new List<PciFunctionSnapshot>();
            var pending = new Queue<byte>();
            var visited = new bool[256];
            EnqueueRootBuses(pending);

            while (pending.Count != 0)
            {
                byte bus = pending.Dequeue();
                if (visited[bus]) continue;
                visited[bus] = true;

                for (byte device = 0; device < 32; device++)
                {
                    if (config.Read16(bus, device, 0, 0x00) == MissingVendor) continue;
                    byte functionCount = (config.Read8(bus, device, 0, HeaderTypeOffset) & MultifunctionBit) != 0 ? (byte)8 : (byte)1;

                    for (byte function = 0; function < functionCount; function++)
                    {
                        ushort vendor = config.Read16(bus, device, function, 0x00);
                        if (vendor == MissingVendor) continue;

                        ushort deviceId = config.Read16(bus, device, function, 0x02);
                        byte programmingInterface = config.Read8(bus, device, function, 0x09);
                        byte subclass = config.Read8(bus, device, function, 0x0A);
                        byte classCode = config.Read8(bus, device, function, 0x0B);
                        // Read-only PCI revision and subsystem identity help select the
                        // correct Realtek RTL810xE silicon variant on physical hardware.
                        // Subsystem IDs at 0x2C/0x2E exist only in type-0 headers;
                        // reading those offsets on bridges would misidentify devices.
                        byte revisionId = 0xFF;
                        ushort subsystemVendorId = 0xFFFF;
                        ushort subsystemDeviceId = 0xFFFF;
                        if (classCode == 0x02)
                        {
                            revisionId = config.Read8(bus, device, function, 0x08);
                            byte headerType = (byte)(config.Read8(bus, device, function, HeaderTypeOffset) & 0x7F);
                            if (headerType == 0x00)
                            {
                                subsystemVendorId = config.Read16(bus, device, function, 0x2C);
                                subsystemDeviceId = config.Read16(bus, device, function, 0x2E);
                            }
                        }
                        result.Add(new PciFunctionSnapshot(bus, device, function, vendor, deviceId,
                            classCode, subclass, programmingInterface, revisionId, subsystemVendorId, subsystemDeviceId));

                        if (classCode == BridgeClass && subclass == PciBridgeSubclass)
                        {
                            byte secondaryBus = config.Read8(bus, device, function, SecondaryBusOffset);
                            if (secondaryBus != bus && !visited[secondaryBus]) pending.Enqueue(secondaryBus);
                        }
                    }
                }
            }

            return result;
        }

        private void EnqueueRootBuses(Queue<byte> pending)
        {
            // A conventional single-function host bridge owns bus 0.  On systems
            // exposing a multifunction host controller at 00:00.x, each present
            // function x represents an independent root bus x (PCI firmware model).
            // Discover those roots before following downstream bridges so devices
            // behind additional host bridges are not silently omitted.
            ushort rootVendor = config.Read16(0, 0, 0, 0x00);
            if (rootVendor == MissingVendor || (config.Read8(0, 0, 0, HeaderTypeOffset) & MultifunctionBit) == 0)
            {
                pending.Enqueue(0);
                return;
            }

            for (byte function = 0; function < 8; function++)
            {
                if (config.Read16(0, 0, function, 0x00) != MissingVendor)
                    pending.Enqueue(function);
            }

            // Function zero was present above, therefore at least bus 0 is queued.
        }
    }
}
