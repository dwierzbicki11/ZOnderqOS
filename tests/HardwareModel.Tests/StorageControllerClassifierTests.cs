using System;
using System.Runtime.CompilerServices;
using ZonderqOS.Hardware;

internal static class StorageControllerClassifierTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var ahci = new DeviceDescriptor(new DeviceId("pci", "00:1f.2"),
            0x8086, 0x2922, 0x01, 0x06, 0x01);
        var nvme = new DeviceDescriptor(new DeviceId("pci", "01:00.0"),
            0x144D, 0xA808, 0x01, 0x08, 0x02);
        var sataLegacy = new DeviceDescriptor(new DeviceId("pci", "00:1f.1"),
            0x8086, 0x1234, 0x01, 0x01, 0x80);
        var unknownNvm = new DeviceDescriptor(new DeviceId("pci", "02:00.0"),
            0x1234, 0x5678, 0x01, 0x08, 0x01);

        Require(StorageControllerClassifier.TryClassify(ahci, out StorageControllerKind ahciKind) &&
                ahciKind == StorageControllerKind.Ahci, "AHCI classification failed");
        Require(StorageControllerClassifier.TryClassify(nvme, out StorageControllerKind nvmeKind) &&
                nvmeKind == StorageControllerKind.Nvme, "NVMe classification failed");
        Require(!StorageControllerClassifier.TryClassify(sataLegacy, out _),
                "legacy/vendor SATA must not be claimed as AHCI");
        Require(!StorageControllerClassifier.TryClassify(unknownNvm, out _),
                "unknown NVM programming interface must fail closed");

        var registry = new DriverRegistry();
        var ahciDriver = new AhciControllerDriver();
        var nvmeDriver = new NvmeControllerDriver();
        registry.Register(ahciDriver);
        registry.Register(nvmeDriver);

        Require(registry.TryBind(ahci, out IDeviceDriver boundAhci) &&
                object.ReferenceEquals(boundAhci, ahciDriver), "AHCI typed binding failed");
        Require(registry.TryBind(nvme, out IDeviceDriver boundNvme) &&
                object.ReferenceEquals(boundNvme, nvmeDriver), "NVMe typed binding failed");
        Require(!registry.TryBind(sataLegacy, out _), "unrecognized storage controller must remain unbound");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new Exception("D2.1 storage classifier: " + message);
    }
}
