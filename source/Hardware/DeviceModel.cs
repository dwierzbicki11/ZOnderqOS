using System;
using System.Collections.Generic;

namespace ZonderqOS.Hardware
{
    /// <summary>
    /// Architecture-neutral identity exposed by the system device layer.
    /// HAL implementations translate bus-specific discovery into this model.
    /// </summary>
    public readonly struct DeviceId : IEquatable<DeviceId>
    {
        public DeviceId(string bus, string address)
        {
            Bus = bus ?? throw new ArgumentNullException(nameof(bus));
            Address = address ?? throw new ArgumentNullException(nameof(address));
        }

        public string Bus { get; }
        public string Address { get; }

        public bool Equals(DeviceId other) =>
            string.Equals(Bus, other.Bus, StringComparison.Ordinal) &&
            string.Equals(Address, other.Address, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is DeviceId other && Equals(other);
        public override int GetHashCode() => (Bus.GetHashCode() * 397) ^ Address.GetHashCode();
        public override string ToString() => Bus + ":" + Address;
    }

    public sealed class DeviceDescriptor
    {
        public DeviceDescriptor(DeviceId id, ushort vendorId, ushort deviceId, byte classCode, byte subclass, byte programmingInterface)
        {
            Id = id;
            VendorId = vendorId;
            DeviceId = deviceId;
            ClassCode = classCode;
            Subclass = subclass;
            ProgrammingInterface = programmingInterface;
        }

        public DeviceId Id { get; }
        public ushort VendorId { get; }
        public ushort DeviceId { get; }
        public byte ClassCode { get; }
        public byte Subclass { get; }
        public byte ProgrammingInterface { get; }
    }

    /// <summary>System-facing driver contract; contains no PCI/MMIO implementation details.</summary>
    public interface IDeviceDriver
    {
        string Name { get; }
        bool Supports(DeviceDescriptor device);
        bool Bind(DeviceDescriptor device);
    }

    /// <summary>
    /// Deterministic first-match binder. Registration order is explicit so that
    /// a generic fallback can safely follow more specific drivers.
    /// </summary>
    public sealed class DriverRegistry
    {
        private readonly List<IDeviceDriver> drivers = new List<IDeviceDriver>();
        private readonly Dictionary<DeviceId, IDeviceDriver> bindings = new Dictionary<DeviceId, IDeviceDriver>();

        public void Register(IDeviceDriver driver)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            drivers.Add(driver);
        }

        public bool TryBind(DeviceDescriptor device, out IDeviceDriver driver)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            if (bindings.TryGetValue(device.Id, out driver)) return true;

            for (int i = 0; i < drivers.Count; i++)
            {
                IDeviceDriver candidate = drivers[i];
                if (!candidate.Supports(device)) continue;
                if (!candidate.Bind(device)) continue;
                bindings.Add(device.Id, candidate);
                driver = candidate;
                return true;
            }

            driver = null;
            return false;
        }

        public bool TryGetBinding(DeviceId id, out IDeviceDriver driver) => bindings.TryGetValue(id, out driver);
    }
}
