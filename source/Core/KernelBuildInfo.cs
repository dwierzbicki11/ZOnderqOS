using System;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Immutable compile-time and boot-time identity of the executing ZonderqOS kernel.
    /// This metadata is compiled directly into the binary assembly and cannot be falsified
    /// by modifying files on disk such as /etc/os-release.
    /// </summary>
    public static class KernelBuildInfo
    {
        public const string KernelVersion = "1.0.0-alpha";
        public const int KernelBuild = 3100;
        public const string Architecture = "x86_64";
        public const string CosmosVersion = "Cosmos 3.0.95 Native Core";
        public const string GitCommit = "3f48d33e";
        public const string CompileTimestamp = "2026-11-08 17:35:00 UTC";
        public const string KernelBinaryName = "ZonderqOS.elf";

        /// <summary>
        /// Boot slot actively reported by the bootloader state manager:
        /// "A", "B", or "ISO_RECOVERY".
        /// </summary>
        public static string RunningSlot { get; internal set; } = "UNKNOWN";

        /// <summary>
        /// True filesystem path to the kernel executable being executed, if mounted.
        /// </summary>
        public static string RunningSlotPath { get; internal set; } = "boot():/boot/ZonderqOS.elf";

        /// <summary>
        /// Whether the current kernel boot has reached desktop/console stability
        /// and has been confirmed healthy by BootSlotManager.
        /// </summary>
        public static bool IsHealthyAndConfirmed { get; internal set; } = false;

        public static string GetSummary()
        {
            return $"ZonderqOS Gen3 {KernelVersion} (Build {KernelBuild}) [{Architecture}] - {CosmosVersion}";
        }
    }
}
