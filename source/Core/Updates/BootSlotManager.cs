using System;
using System.IO;
using ZonderqOS.Security;

namespace ZonderqOS.SystemCore.Updates
{
    public enum SlotHealth
    {
        Empty = 0,
        PendingConfirmation = 1,
        Healthy = 2,
        Failed = 3
    }

    /// <summary>
    /// Implements robust A/B dual-slot kernel boot architecture.
    /// Manages boot slots, kernel staging, atomic switching, boot attempts tracking,
    /// automatic watchdog failover, and Limine bootloader configuration.
    /// </summary>
    public static class BootSlotManager
    {
        public const string SlotA = "A";
        public const string SlotB = "B";

        public const string BootStatePath = "/boot/boot_state.cfg";
        public const string SlotAPath = "/boot/slot_a/ZonderqOS.elf";
        public const string SlotBPath = "/boot/slot_b/ZonderqOS.elf";
        public const string LimineBootConfig = "/boot/limine.conf";
        public const string LimineRootConfig = "/limine.conf";

        public const int MaxBootAttempts = 3;

        public static string ActiveSlot { get; private set; } = SlotA;
        public static string FallbackSlot { get; private set; } = SlotB;
        public static string PendingSlot { get; private set; } = string.Empty;
        public static int BootAttempts { get; private set; } = 0;

        public static SlotHealth SlotAStatus { get; private set; } = SlotHealth.Healthy;
        public static SlotHealth SlotBStatus { get; private set; } = SlotHealth.Empty;

        public static string SlotAVersion { get; private set; } = KernelBuildInfo.KernelVersion;
        public static int SlotABuild { get; private set; } = KernelBuildInfo.KernelBuild;
        public static string SlotBVersion { get; private set; } = string.Empty;
        public static int SlotBBuild { get; private set; } = 0;

        private static bool isInitialized = false;

        public static void Initialize()
        {
            if (isInitialized)
                return;

            try
            {
                if (!Directory.Exists("/boot"))
                    Directory.CreateDirectory("/boot");
                if (!Directory.Exists("/boot/slot_a"))
                    Directory.CreateDirectory("/boot/slot_a");
                if (!Directory.Exists("/boot/slot_b"))
                    Directory.CreateDirectory("/boot/slot_b");

                if (!File.Exists(BootStatePath))
                {
                    CreateInitialBootState();
                }
                else
                {
                    LoadBootState();
                }

                // Check pending confirmation and watchdog boot attempts
                CheckWatchdogFailover();

                // Reflect running slot in KernelBuildInfo
                KernelBuildInfo.RunningSlot = ActiveSlot;
                KernelBuildInfo.RunningSlotPath = ActiveSlot == SlotB ? SlotBPath : SlotAPath;

                isInitialized = true;
            }
            catch (Exception ex)
            {
                SystemLogger.Log(SystemLogLevel.Warning, "BOOTSLOT", "Blad inicjalizacji BootSlotManager: " + ex.Message);
            }
        }

        private static void CreateInitialBootState()
        {
            ActiveSlot = SlotA;
            FallbackSlot = SlotB;
            PendingSlot = string.Empty;
            BootAttempts = 0;
            SlotAStatus = SlotHealth.Healthy;
            SlotBStatus = SlotHealth.Empty;
            SlotAVersion = KernelBuildInfo.KernelVersion;
            SlotABuild = KernelBuildInfo.KernelBuild;

            SaveBootState();
            SyncLimineConfig();
        }

        public static void LoadBootState()
        {
            try
            {
                if (!File.Exists(BootStatePath))
                    return;

                string[] lines = File.ReadAllLines(BootStatePath);
                for (int i = 0; i < lines.Length; i++)
                {
                    string l = lines[i].Trim();
                    if (l.Length == 0 || l.StartsWith("#")) continue;

                    int eq = l.IndexOf('=');
                    if (eq <= 0) continue;

                    string key = l.Substring(0, eq).Trim().ToUpperInvariant();
                    string val = l.Substring(eq + 1).Trim();

                    switch (key)
                    {
                        case "ACTIVE_SLOT":
                            ActiveSlot = val.ToUpperInvariant() == SlotB ? SlotB : SlotA;
                            break;
                        case "FALLBACK_SLOT":
                            FallbackSlot = val.ToUpperInvariant() == SlotA ? SlotA : SlotB;
                            break;
                        case "PENDING_SLOT":
                            PendingSlot = val.ToUpperInvariant();
                            break;
                        case "BOOT_ATTEMPTS":
                            int.TryParse(val, out int att);
                            BootAttempts = att;
                            break;
                        case "SLOT_A_STATUS":
                            SlotAStatus = ParseHealth(val);
                            break;
                        case "SLOT_B_STATUS":
                            SlotBStatus = ParseHealth(val);
                            break;
                        case "SLOT_A_VERSION":
                            SlotAVersion = val;
                            break;
                        case "SLOT_A_BUILD":
                            int.TryParse(val, out int ab);
                            SlotABuild = ab;
                            break;
                        case "SLOT_B_VERSION":
                            SlotBVersion = val;
                            break;
                        case "SLOT_B_BUILD":
                            int.TryParse(val, out int bb);
                            SlotBBuild = bb;
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                SystemLogger.Log(SystemLogLevel.Warning, "BOOTSLOT", "LoadBootState error: " + ex.Message);
            }
        }

        public static void SaveBootState()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("# ZonderqOS A/B Bootloader State Configuration");
                sb.AppendLine("ACTIVE_SLOT=" + ActiveSlot);
                sb.AppendLine("FALLBACK_SLOT=" + FallbackSlot);
                sb.AppendLine("PENDING_SLOT=" + PendingSlot);
                sb.AppendLine("BOOT_ATTEMPTS=" + BootAttempts);
                sb.AppendLine("MAX_BOOT_ATTEMPTS=" + MaxBootAttempts);
                sb.AppendLine("SLOT_A_STATUS=" + SlotAStatus);
                sb.AppendLine("SLOT_A_VERSION=" + SlotAVersion);
                sb.AppendLine("SLOT_A_BUILD=" + SlotABuild);
                sb.AppendLine("SLOT_B_STATUS=" + SlotBStatus);
                sb.AppendLine("SLOT_B_VERSION=" + SlotBVersion);
                sb.AppendLine("SLOT_B_BUILD=" + SlotBBuild);
                sb.AppendLine("LAST_MODIFIED=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                File.WriteAllText(BootStatePath, sb.ToString());
            }
            catch (Exception ex)
            {
                SystemLogger.Log(SystemLogLevel.Warning, "BOOTSLOT", "SaveBootState error: " + ex.Message);
            }
        }

        private static SlotHealth ParseHealth(string val)
        {
            string s = val.ToUpperInvariant();
            if (s.Contains("HEALTHY")) return SlotHealth.Healthy;
            if (s.Contains("PENDING")) return SlotHealth.PendingConfirmation;
            if (s.Contains("FAILED")) return SlotHealth.Failed;
            return SlotHealth.Empty;
        }

        private static void CheckWatchdogFailover()
        {
            if (!string.IsNullOrEmpty(PendingSlot))
            {
                BootAttempts++;
                SystemLogger.Log(SystemLogLevel.Warning, "BOOTSLOT",
                    $"Proba uruchomienia slotu {PendingSlot}: {BootAttempts}/{MaxBootAttempts}");

                if (BootAttempts >= MaxBootAttempts)
                {
                    // Automatic failover to fallback slot!
                    SystemLogger.Log(SystemLogLevel.Error, "BOOTSLOT",
                        $"AWARYJNY ROLLBACK: Slot {PendingSlot} nie osiagnal stabilnego stanu po {BootAttempts} probach. Przepiecie na slot {FallbackSlot}!");

                    if (PendingSlot == SlotA)
                        SlotAStatus = SlotHealth.Failed;
                    else
                        SlotBStatus = SlotHealth.Failed;

                    ActiveSlot = FallbackSlot;
                    PendingSlot = string.Empty;
                    BootAttempts = 0;

                    SyncLimineConfig();
                    SaveBootState();
                    return;
                }

                SaveBootState();
            }
        }

        /// <summary>
        /// Confirms that the currently running slot has successfully booted
        /// and reached a healthy interactive state.
        /// </summary>
        public static void ConfirmSlotHealth()
        {
            Initialize();

            if (!string.IsNullOrEmpty(PendingSlot) && PendingSlot == ActiveSlot)
            {
                if (ActiveSlot == SlotA)
                    SlotAStatus = SlotHealth.Healthy;
                else
                    SlotBStatus = SlotHealth.Healthy;

                PendingSlot = string.Empty;
                BootAttempts = 0;
                KernelBuildInfo.IsHealthyAndConfirmed = true;

                SaveBootState();
                SystemLogger.Log(SystemLogLevel.Info, "BOOTSLOT", $"Slot {ActiveSlot} pomyslnie zatwierdzony jako zdrowy (HEALTHY).");
            }
            else
            {
                KernelBuildInfo.IsHealthyAndConfirmed = true;
            }
        }

        public static string GetInactiveSlot()
        {
            return ActiveSlot == SlotA ? SlotB : SlotA;
        }

        public static string GetSlotKernelPath(string slot)
        {
            return slot == SlotB ? SlotBPath : SlotAPath;
        }

        /// <summary>
        /// Stages a new kernel into the inactive slot and verifies its SHA-256 hash directly on disk.
        /// </summary>
        public static bool StageKernelToInactiveSlot(string sourceKernelPath, string expectedSha256,
            string version, int build, Action<int, string> progress, out string error)
        {
            Initialize();
            error = string.Empty;

            if (string.IsNullOrEmpty(sourceKernelPath) || !File.Exists(sourceKernelPath))
            {
                error = "Plik zrodlowy nowego kernela nie istnieje: " + sourceKernelPath;
                return false;
            }

            string targetSlot = GetInactiveSlot();
            string targetPath = GetSlotKernelPath(targetSlot);
            string targetDir = Path.GetDirectoryName(targetPath);

            try
            {
                if (!Directory.Exists(targetDir))
                    Directory.CreateDirectory(targetDir);

                progress?.Invoke(25, $"Kopiowanie kernela do nieaktywnego slotu {targetSlot} ({targetPath})...");

                // Copy binary
                File.Copy(sourceKernelPath, targetPath, true);

                progress?.Invoke(50, "Weryfikacja sumy kontrolnej SHA-256 zapisanego pliku kernela na dysku...");

                // Read back bytes directly from destination on disk
                byte[] writtenBytes = File.ReadAllBytes(targetPath);
                string writtenHash = Crypto.ComputeSha256(writtenBytes);

                if (!string.IsNullOrEmpty(expectedSha256) &&
                    !Crypto.FixedTimeEquals(writtenHash.ToLowerInvariant(), expectedSha256.ToLowerInvariant()))
                {
                    // Checksum failure: delete corrupted target file immediately
                    try { File.Delete(targetPath); } catch { }
                    error = $"Blad integralnosci zapisanego kernela w slocie {targetSlot}! Oczekiwano SHA256: {expectedSha256}, otrzymano: {writtenHash}";
                    SystemLogger.Log(SystemLogLevel.Error, "BOOTSLOT", error);
                    return false;
                }

                // Write metadata files
                File.WriteAllText(Path.Combine(targetDir, "kernel.sha256"), writtenHash + "\n");
                File.WriteAllText(Path.Combine(targetDir, "version.info"), $"VERSION={version}\nBUILD={build}\nDATE={DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");

                if (targetSlot == SlotA)
                {
                    SlotAVersion = version;
                    SlotABuild = build;
                    SlotAStatus = SlotHealth.PendingConfirmation;
                }
                else
                {
                    SlotBVersion = version;
                    SlotBBuild = build;
                    SlotBStatus = SlotHealth.PendingConfirmation;
                }

                progress?.Invoke(70, $"Kernel pomyslnie zainstalowany w slocie {targetSlot} [SHA256: {writtenHash.Substring(0, 16)}...]");
                SaveBootState();
                return true;
            }
            catch (Exception ex)
            {
                error = "Wyjatek podczas instalacji kernela w slocie: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Switches active boot slot to the newly installed slot and updates Limine bootloader configuration.
        /// </summary>
        public static bool ActivateSlot(string targetSlot, out string error)
        {
            Initialize();
            error = string.Empty;

            if (targetSlot != SlotA && targetSlot != SlotB)
            {
                error = "Nieprawidlowy slot docelowy: " + targetSlot;
                return false;
            }

            try
            {
                string kernelPath = GetSlotKernelPath(targetSlot);
                if (!File.Exists(kernelPath))
                {
                    error = $"Nie mozna aktywowac slotu {targetSlot}: brak pliku kernela {kernelPath}!";
                    return false;
                }

                FallbackSlot = ActiveSlot;
                ActiveSlot = targetSlot;
                PendingSlot = targetSlot;
                BootAttempts = 0;

                if (targetSlot == SlotA)
                    SlotAStatus = SlotHealth.PendingConfirmation;
                else
                    SlotBStatus = SlotHealth.PendingConfirmation;

                SaveBootState();
                SyncLimineConfig();

                SystemLogger.Log(SystemLogLevel.Info, "BOOTSLOT",
                    $"Aktywowano slot {targetSlot}. Limine default_entry zaktualizowany. Wymagany restart.");
                return true;
            }
            catch (Exception ex)
            {
                error = "Blad podczas aktywacji slotu: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Performs manual rollback to the previous fallback slot.
        /// </summary>
        public static bool ManualRollback(out string error)
        {
            Initialize();
            error = string.Empty;

            try
            {
                string previousSlot = FallbackSlot;
                string kernelPath = GetSlotKernelPath(previousSlot);

                if (!File.Exists(kernelPath))
                {
                    error = $"Brak pliku kernela w slocie przywracania {previousSlot} ({kernelPath})!";
                    return false;
                }

                ActiveSlot = previousSlot;
                PendingSlot = string.Empty;
                BootAttempts = 0;

                SyncLimineConfig();
                SaveBootState();

                SystemLogger.Log(SystemLogLevel.Warning, "BOOTSLOT", $"Reczny powrot (Rollback) do slotu {previousSlot}.");
                return true;
            }
            catch (Exception ex)
            {
                error = "Blad manualnego rollbacku: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Writes Limine configuration files pointing default_entry to the active slot.
        /// Slot A = entry 1, Slot B = entry 2, Fallback ISO = entry 3.
        /// </summary>
        public static void SyncLimineConfig()
        {
            try
            {
                int defaultEntry = (ActiveSlot == SlotB) ? 2 : 1;

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("# Cosmos Gen3 / Limine v10 A/B dual-slot boot configuration.");
                sb.AppendLine("timeout: 5");
                sb.AppendLine("default_entry: " + defaultEntry);
                sb.AppendLine();
                sb.AppendLine("/ZonderqOS (Slot A" + (ActiveSlot == SlotA ? " - Aktywny" : "") + ")");
                sb.AppendLine("    protocol: limine");
                sb.AppendLine("    resolution: 1366x768x32");
                sb.AppendLine("    path: fslabel(ZONDERQDATA):/boot/slot_a/ZonderqOS.elf");
                sb.AppendLine();
                sb.AppendLine("/ZonderqOS (Slot B" + (ActiveSlot == SlotB ? " - Aktywny" : "") + ")");
                sb.AppendLine("    protocol: limine");
                sb.AppendLine("    resolution: 1366x768x32");
                sb.AppendLine("    path: fslabel(ZONDERQDATA):/boot/slot_b/ZonderqOS.elf");
                sb.AppendLine();
                sb.AppendLine("/ZonderqOS (Kopia zapasowa ISO / Ratunkowa)");
                sb.AppendLine("    protocol: limine");
                sb.AppendLine("    resolution: 1366x768x32");
                sb.AppendLine("    path: boot():/boot/ZonderqOS.elf");

                string config = sb.ToString();

                if (!Directory.Exists("/boot"))
                    Directory.CreateDirectory("/boot");

                File.WriteAllText(LimineBootConfig, config);
                File.WriteAllText(LimineRootConfig, config);

                if (Directory.Exists("/boot/limine"))
                {
                    File.WriteAllText("/boot/limine/limine.conf", config);
                }
            }
            catch (Exception ex)
            {
                SystemLogger.Log(SystemLogLevel.Warning, "BOOTSLOT", "SyncLimineConfig error: " + ex.Message);
            }
        }
    }
}
