using System;

namespace ZonderqOS.Hardware
{
    public enum StorageControllerKind
    {
        Ahci,
        Nvme
    }

    /// <summary>
    /// D2.1 classification only. No BAR/MMIO access is performed here; that is
    /// deliberately deferred until D2.2 after discovery/binding has runtime proof.
    /// </summary>
    public static class StorageControllerClassifier
    {
        public static bool TryClassify(DeviceDescriptor device, out StorageControllerKind kind)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));

            // PCI class 01h = mass storage. AHCI is SATA subclass 06h with PI=01h.
            if (device.ClassCode == 0x01 && device.Subclass == 0x06 && device.ProgrammingInterface == 0x01)
            {
                kind = StorageControllerKind.Ahci;
                return true;
            }

            // PCI class 01h, subclass 08h = Non-Volatile Memory controller.
            // PI=02h is NVM Express. Do not bind vendor-specific/unknown NVM PIs.
            if (device.ClassCode == 0x01 && device.Subclass == 0x08 && device.ProgrammingInterface == 0x02)
            {
                kind = StorageControllerKind.Nvme;
                return true;
            }

            kind = default;
            return false;
        }
    }

    public abstract class StorageControllerDriver : IDeviceDriver
    {
        protected StorageControllerDriver(StorageControllerKind kind, string name)
        {
            Kind = kind;
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public StorageControllerKind Kind { get; }
        public string Name { get; }

        public bool Supports(DeviceDescriptor device) =>
            StorageControllerClassifier.TryClassify(device, out StorageControllerKind kind) && kind == Kind;

        public bool Bind(DeviceDescriptor device)
        {
            // D2.1 establishes a typed system binding only. Hardware ownership,
            // BAR mapping and MMIO begin in D2.2 and must not be implied here.
            return Supports(device);
        }
    }

    public sealed class AhciControllerDriver : StorageControllerDriver
    {
        public AhciControllerDriver() : base(StorageControllerKind.Ahci, "ahci") { }
    }

    public sealed class NvmeControllerDriver : StorageControllerDriver
    {
        public NvmeControllerDriver() : base(StorageControllerKind.Nvme, "nvme") { }
    }
}
