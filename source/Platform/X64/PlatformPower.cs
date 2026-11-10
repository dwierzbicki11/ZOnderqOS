namespace ZonderqOS.Platform
{
    /// <summary>
    /// Platform-specific x86_64 shutdown probes. These I/O ports exist only
    /// on supported PC emulators / chipsets, never on ARM64.
    /// </summary>
    public static class PlatformPower
    {
        public static void TryShutdownViaFirmware()
        {
            // Preserve the existing shutdown sequence: an acknowledged port
            // normally powers off immediately, otherwise SystemPower halts.
            try
            {
                X64.X64PortIo.WriteWord(0x604, 0x2000);  // QEMU/KVM
                X64.X64PortIo.WriteWord(0xB004, 0x2000); // Bochs
                X64.X64PortIo.WriteWord(0x4004, 0x3400); // VirtualBox
                X64.X64PortIo.WriteWord(0x1804, 0x3C00); // Intel PCH PM1a
                X64.X64PortIo.WriteWord(0x0404, 0x3400);
                X64.X64PortIo.WriteWord(0x0804, 0x2000);
            }
            catch
            {
                // Unsupported hardware falls back to the safe halt loop.
            }
        }
    }
}
