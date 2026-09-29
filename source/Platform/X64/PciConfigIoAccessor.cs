using System.Runtime.InteropServices;
using ZonderqOS.Hardware;

namespace ZonderqOS.Platform.X64
{
    /// <summary>
    /// x86/x64 PCI Configuration Mechanism #1 backend for the architecture-neutral
    /// D1 topology walker. Hardware policy remains in PciConfigDiscoverySource.
    /// </summary>
    public sealed partial class PciConfigIoAccessor : IPciConfigAccessor
    {
        private const ushort ConfigAddressPort = 0xCF8;
        private const ushort ConfigDataPort = 0xCFC;

        public byte Read8(byte bus, byte device, byte function, byte offset)
        {
            uint value = ReadAlignedDword(bus, device, function, offset);
            return PciConfigMechanism1.Extract8(value, offset);
        }

        public ushort Read16(byte bus, byte device, byte function, byte offset)
        {
            // Validate alignment before touching privileged I/O.
            if ((offset & 1) != 0)
                return PciConfigMechanism1.Extract16(0, offset);

            uint value = ReadAlignedDword(bus, device, function, offset);
            return PciConfigMechanism1.Extract16(value, offset);
        }

        private static uint ReadAlignedDword(byte bus, byte device, byte function, byte offset)
        {
            uint address = PciConfigMechanism1.EncodeAddress(bus, device, function, offset);
            NativePortIo.WriteDWord(ConfigAddressPort, address);
            return NativePortIo.ReadDWord(ConfigDataPort);
        }

        // Cosmos Gen3 keeps PlatformHAL/IPortIO internal. Bind only to the same x64
        // native ABI used by Cosmos.Kernel.Core.X64 instead of reaching through an
        // inaccessible managed HAL type. This file is excluded from ARM64 builds.
        private static partial class NativePortIo
        {
            [LibraryImport("*", EntryPoint = "_native_io_read_dword")]
            [SuppressGCTransition]
            internal static partial uint ReadDWord(ushort port);

            [LibraryImport("*", EntryPoint = "_native_io_write_dword")]
            [SuppressGCTransition]
            internal static partial void WriteDWord(ushort port, uint value);
        }
    }
}
