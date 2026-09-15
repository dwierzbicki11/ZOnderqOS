namespace Cosmos.Kernel.System.Diagnostics
{
    /// <summary>
    /// Minimal diagnostics surface for the Raspberry Pi 4 ARM64 smoke build.
    /// Cosmos 3.0.84's ARM64 package currently omits the public Diagnostics
    /// projection used by the desktop compatibility layer. The smoke kernel only
    /// needs compile-time placeholders; real telemetry is intentionally disabled.
    /// </summary>
    public static class MemoryInfo
    {
        public static ulong TotalPages => 0UL;
        public static ulong FreePages => 0UL;
        public static ulong PageSizeBytes => 4096UL;
        public static ulong RamSizeBytes => 0UL;
        public static int TotalCollections => 0;
    }
}
