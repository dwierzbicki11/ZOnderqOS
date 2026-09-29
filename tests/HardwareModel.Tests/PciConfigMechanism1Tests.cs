using System;
using System.Runtime.CompilerServices;
using ZonderqOS.Hardware;

internal static class PciConfigMechanism1Tests
{
    [ModuleInitializer]
    internal static void Run()
    {
        // 80:1f.7 register 0x13 must align to dword 0x10.
        uint address = PciConfigMechanism1.EncodeAddress(0x80, 0x1F, 7, 0x13);
        Require(address == 0x8080FF10u, "PCI mechanism #1 CF8 address encoding mismatch");

        const uint sample = 0xA1B2C3D4u;
        Require(PciConfigMechanism1.Extract8(sample, 0) == 0xD4, "byte lane 0 mismatch");
        Require(PciConfigMechanism1.Extract8(sample, 1) == 0xC3, "byte lane 1 mismatch");
        Require(PciConfigMechanism1.Extract8(sample, 2) == 0xB2, "byte lane 2 mismatch");
        Require(PciConfigMechanism1.Extract8(sample, 3) == 0xA1, "byte lane 3 mismatch");
        Require(PciConfigMechanism1.Extract16(sample, 0) == 0xC3D4, "word lane 0 mismatch");
        Require(PciConfigMechanism1.Extract16(sample, 2) == 0xA1B2, "word lane 2 mismatch");

        bool unalignedRejected = false;
        try { _ = PciConfigMechanism1.Extract16(sample, 1); } catch (ArgumentOutOfRangeException) { unalignedRejected = true; }
        Require(unalignedRejected, "unaligned PCI word read must be rejected");

        bool deviceRejected = false;
        try { _ = PciConfigMechanism1.EncodeAddress(0, 32, 0, 0); } catch (ArgumentOutOfRangeException) { deviceRejected = true; }
        Require(deviceRejected, "PCI device 32 must be rejected");

        bool functionRejected = false;
        try { _ = PciConfigMechanism1.EncodeAddress(0, 0, 8, 0); } catch (ArgumentOutOfRangeException) { functionRejected = true; }
        Require(functionRejected, "PCI function 8 must be rejected");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
