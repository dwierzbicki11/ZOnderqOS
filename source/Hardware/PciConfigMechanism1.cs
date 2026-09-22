using System;

namespace ZonderqOS.Hardware
{
    /// <summary>
    /// Pure PCI Configuration Mechanism #1 address/field codec. Keeping this logic
    /// independent of port I/O makes the privileged x86 backend small and lets CI
    /// verify every bit of the CF8 address before QEMU runtime validation.
    /// </summary>
    public static class PciConfigMechanism1
    {
        private const uint EnableBit = 0x80000000u;

        public static uint EncodeAddress(byte bus, byte device, byte function, byte offset)
        {
            ValidateBdf(device, function);
            return EnableBit
                | ((uint)bus << 16)
                | ((uint)device << 11)
                | ((uint)function << 8)
                | (uint)(offset & 0xFC);
        }

        public static byte Extract8(uint value, byte offset)
        {
            int shift = (offset & 3) * 8;
            return (byte)((value >> shift) & 0xFFu);
        }

        public static ushort Extract16(uint value, byte offset)
        {
            if ((offset & 1) != 0)
                throw new ArgumentOutOfRangeException(nameof(offset), "PCI 16-bit configuration reads must be word aligned.");
            int shift = (offset & 2) * 8;
            return (ushort)((value >> shift) & 0xFFFFu);
        }

        public static void ValidateBdf(byte device, byte function)
        {
            if (device >= 32)
                throw new ArgumentOutOfRangeException(nameof(device));
            if (function >= 8)
                throw new ArgumentOutOfRangeException(nameof(function));
        }
    }
}
