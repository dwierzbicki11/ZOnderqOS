using System.Runtime.InteropServices;

namespace ZonderqOS.Platform.X64
{
    /// <summary>
    /// Direct port I/O access for x86_64 architecture using the native C runtime entry points.
    /// Excluded from ARM64 builds.
    /// </summary>
    public static partial class X64PortIo
    {
        [LibraryImport("*", EntryPoint = "_native_io_read_byte")]
        [SuppressGCTransition]
        public static partial byte ReadByte(ushort port);

        [LibraryImport("*", EntryPoint = "_native_io_write_byte")]
        [SuppressGCTransition]
        public static partial void WriteByte(ushort port, byte value);

        [LibraryImport("*", EntryPoint = "_native_io_read_word")]
        [SuppressGCTransition]
        public static partial ushort ReadWord(ushort port);

        [LibraryImport("*", EntryPoint = "_native_io_write_word")]
        [SuppressGCTransition]
        public static partial void WriteWord(ushort port, ushort value);

        [LibraryImport("*", EntryPoint = "_native_io_read_dword")]
        [SuppressGCTransition]
        public static partial uint ReadDWord(ushort port);

        [LibraryImport("*", EntryPoint = "_native_io_write_dword")]
        [SuppressGCTransition]
        public static partial void WriteDWord(ushort port, uint value);
    }
}
