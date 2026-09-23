using System;
using System.Collections.Generic;

namespace ZonderqOS.Hardware
{
    /// <summary>
    /// HAL-neutral snapshot of one PCI/PCIe function. A concrete Cosmos adapter
    /// is responsible only for reading config space and constructing this value.
    /// </summary>
    public readonly struct PciFunctionSnapshot
    {
        public PciFunctionSnapshot(byte bus, byte device, byte function, ushort vendorId, ushort deviceId,
            byte classCode, byte subclass, byte programmingInterface)
        {
            if (device > 31) throw new ArgumentOutOfRangeException(nameof(device));
            if (function > 7) throw new ArgumentOutOfRangeException(nameof(function));
            Bus = bus;
            Device = device;
            Function = function;
            VendorId = vendorId;
            DeviceId = deviceId;
            ClassCode = classCode;
            Subclass = subclass;
            ProgrammingInterface = programmingInterface;
        }

        public byte Bus { get; }
        public byte Device { get; }
        public byte Function { get; }
        public ushort VendorId { get; }
        public ushort DeviceId { get; }
        public byte ClassCode { get; }
        public byte Subclass { get; }
        public byte ProgrammingInterface { get; }

        public DeviceDescriptor ToDeviceDescriptor()
        {
            string address = Bus.ToString("X2") + ":" + Device.ToString("X2") + "." + Function.ToString("X1");
            return new DeviceDescriptor(new DeviceId("pci", address), VendorId, DeviceId, ClassCode, Subclass, ProgrammingInterface);
        }
    }

    /// <summary>
    /// Boundary implemented by the architecture/HAL layer. The system device
    /// model never reaches into Cosmos PCI objects directly.
    /// </summary>
    public interface IPciDiscoverySource
    {
        IEnumerable<PciFunctionSnapshot> Discover();
    }

    /// <summary>
    /// System-facing PCI discovery service. It deliberately owns normalization
    /// so every HAL backend gets identical sentinel, duplicate and ordering rules.
    /// </summary>
    public sealed class PciDiscoveryService
    {
        private readonly IPciDiscoverySource source;

        public PciDiscoveryService(IPciDiscoverySource source)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public List<DeviceDescriptor> DiscoverDevices()
        {
            IEnumerable<PciFunctionSnapshot> discovered = source.Discover();
            if (discovered == null)
                throw new InvalidOperationException("PCI discovery source returned null");
            return PciDeviceEnumerator.Normalize(discovered);
        }
    }

    /// <summary>
    /// Converts bus discovery into stable system descriptors. Input order is not
    /// trusted: results are sorted by BDF and duplicate functions are rejected.
    /// </summary>
    public static class PciDeviceEnumerator
    {
        public static List<DeviceDescriptor> Normalize(IEnumerable<PciFunctionSnapshot> discovered)
        {
            if (discovered == null) throw new ArgumentNullException(nameof(discovered));

            var snapshots = new List<PciFunctionSnapshot>(discovered);
            snapshots.Sort(CompareBdf);
            var result = new List<DeviceDescriptor>(snapshots.Count);
            DeviceId? previous = null;

            for (int i = 0; i < snapshots.Count; i++)
            {
                // 0xFFFF is the PCI configuration-space sentinel for no function.
                if (snapshots[i].VendorId == 0xFFFF) continue;
                DeviceDescriptor descriptor = snapshots[i].ToDeviceDescriptor();
                if (previous.HasValue && previous.Value.Equals(descriptor.Id))
                    throw new InvalidOperationException("Duplicate PCI function discovered: " + descriptor.Id);
                result.Add(descriptor);
                previous = descriptor.Id;
            }

            return result;
        }

        private static int CompareBdf(PciFunctionSnapshot left, PciFunctionSnapshot right)
        {
            int value = left.Bus.CompareTo(right.Bus);
            if (value != 0) return value;
            value = left.Device.CompareTo(right.Device);
            return value != 0 ? value : left.Function.CompareTo(right.Function);
        }
    }
}
