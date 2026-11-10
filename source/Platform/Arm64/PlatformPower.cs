namespace ZonderqOS.Platform
{
    /// <summary>
    /// ARM64 has no x86 legacy I/O ports. The shutdown screen and fallback
    /// halt in SystemPower remain available until a firmware power-off
    /// backend is implemented for the target board.
    /// </summary>
    public static class PlatformPower
    {
        public static void TryShutdownViaFirmware()
        {
            // Intentionally empty: do not try x86-specific port I/O on ARM.
        }
    }
}
