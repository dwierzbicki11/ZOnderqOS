using System;
using Cosmos.Kernel.Core;
using ZonderqOS.Hardware;

namespace ZonderqOS.Platform.X64
{
    /// <summary>
    /// x86/x64 PCI Configuration Mechanism #1 backend for the architecture-neutral
    /// D1 topology walker. Hardware policy remains in PciConfigDiscoverySource.
    /// </summary>
    public sealed class PciConfigIoAccessor : IPciConfigAccessor
    {
        private const ushort ConfigAddressPort = 0xCF8;
        private const ushort ConfigDataPort = 0xCFC;
        private const uint EnableBit = 0x80000000;
        private const int BusShift = 16;
        private const int DeviceShift = 11;
        private const int FunctionShift = 8;
        private const byte DwordAlignMask = 0xFC;
        private const int BitsPerByte = 8;

        public byte Read8(byte bus, byte device, byte function, byte offset)
        {
            ValidateBdf(device, function);
            uint value = ReadAlignedDword(bus, device, function, offset);
            int shift = (offset & 3) * BitsPerByte;
            return (byte)((value >> shift) & 0xFFu);
        }

        public ushort Read16(byte bus, byte device, byte function, byte offset)
        {
            ValidateBdf(device, function);
            if ((offset & 1) != 0)
                throw new ArgumentOutOfRangeException(nameof(offset), "PCI 16-bit configuration reads must be word aligned.");

            uint value = ReadAlignedDword(bus, device, function, offset);
            int shift = (offset & 2) * BitsPerByte;
            return (ushort)((value >> shift) & 0xFFFFu);
        }

        private static uint ReadAlignedDword(byte bus, byte device, byte function, byte offset)
        {
            uint address = EnableBit
                | ((uint)bus << BusShift)
                | ((uint)device << DeviceShift)
                | ((uint)function << FunctionShift)
                | (uint)(offset & DwordAlignMask);

            PlatformHAL.PortIO.WriteDWord(ConfigAddressPort, address);
            return PlatformHAL.PortIO.ReadDWord(ConfigDataPort);
        }

        private static void ValidateBdf(byte device, byte function)
        {
            if (device >= 32)
                throw new ArgumentOutOfRangeException(nameof(device));
            if (function >= 8)
                throw new ArgumentOutOfRangeException(nameof(function));
        }
    }
}
