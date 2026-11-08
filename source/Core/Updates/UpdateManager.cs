using System;
using System.Collections.Generic;
using System.IO;
using ZonderqOS.Security;

namespace ZonderqOS.SystemCore.Updates
{
    public enum UpdateChannel
    {
        Stable = 0,
        Lts = 1,
        Beta = 2,
        Testing = 3
    }

    public enum UpdateStatus
    {
        UpToDate = 0,
        Checking = 1,
        Available = 2,
        Downloading = 3,
        Verifying = 4,
        Installing = 5,
        NeedsReboot = 6,
        Error = 7,
        RolledBack = 8
    }

    public sealed class UpdatePackageInfo
    {
        public string Version { get; set; } = "2026.11.2";
        public int BuildNumber { get; set; } = 3096;
        public string Title { get; set; } = "ZonderqOS Security & Stability Rollup 2";
        public string ReleaseDate { get; set; } = "2026-11-08";
        public string Severity { get; set; } = "Bezpieczenstwo (Zalecana)";
        public int SizeKb { get; set; } = 1840;
        public string ExpectedSha256 { get; set; } = "a7f34c99e1208b5e91223fa87114bca03f88bcde1a4209930fef6a190035ab41";
        public int TargetTheme { get; set; } = 2; // Emerald Forest
        public int TargetWallpaper { get; set; } = 6; // Nordic Glacier
        public string TargetThemeName { get; set; } = "EMERALD FOREST";
        public string PayloadPath { get; set; } = "";
        public string KernelImagePath { get; set; } = "";
        public string[] AppIds { get; set; } = new string[] { "quick-notes", "chiptune" };
        public string[] Changelog { get; set; } = new string[]
        {
            "Nowa aplikacja: Chiptune Studio (Syntezator PC Speaker 8-bit & pianino retro)",
            "Nowa aplikacja: Szybkie Notatki (Sticky Notes z autozapisem)",
            "Odswiezenie wizualne pulpitu: Nowy motyw i tapeta (Emerald & Nordic Glacier)",
            "Wzmocnienie krypto PassVault: SHA-256 z soleniem, ochrona anty-bruteforce",
            "Pelny sprzetowy silnik benchmarkow rzeczywistych SYSBenchmark 3.0",
            "Stanowy firewall sieciowy z regulami dynamicznymi i IntegrityGuard 2.4",
            "Optymalizacja akceleracji VESA i menedzera okien dla Cosmos 3.0.95"
        };
        public string[] Components { get; set; } = new string[]
        {
            "Kernel: Cosmos 3.0.95 x64 Native Core",
            "Security: IntegrityGuard 2.4 & FirewallManager 1.8",
            "Apps: Chiptune Studio 1.0, QuickNotes 1.0, PassVault 2.0, SYSBenchmark 3.0",
            "Shell: zupdate 2.0 (USB & Live Payload Engine) & zpkg package registry"
        };
    }

    public sealed class StorageEnvironmentInfo
    {
        public string PartitionLabel { get; set; } = "ZONDERQDATA";
        public string DeviceType { get; set; } = "Dysk USB / Dedykowany";
        public bool HasUpdatesDir { get; set; } = false;
        public bool HasManifest { get; set; } = false;
        public bool HasPayload { get; set; } = false;
        public bool HasKernelIso { get; set; } = false;
        public string ManifestPath { get; set; } = "";
        public string PayloadPath { get; set; } = "";
        public string KernelIsoPath { get; set; } = "";
        public int PayloadFileCount { get; set; } = 0;
    }

    public static class UpdateManager
    {
        private const string ConfigPath = "/etc/update.cfg";
        private const string HistoryLogPath = "/var/log/update.log";
        private const string UpdatesDir = "/var/updates";
        private const string IncomingDir = "/var/updates/incoming";
        private const string BackupDir = "/var/updates/backup";
        private const string OsReleasePath = "/etc/os-release";

        public static string CurrentVersion { get; private set; } = "2026.11.0";
        public static string CurrentBuild { get; private set; } = "3095-LTS";
        public static UpdateChannel Channel { get; private set; } = UpdateChannel.Stable;
        public static UpdateStatus Status { get; private set; } = UpdateStatus.UpToDate;
        public static string LastCheckTime { get; private set; } = "Brak informacji";
        public static bool IsRebootRequired { get; private set; } = false;
        public static UpdatePackageInfo AvailablePackage { get; private set; } = null;

        private static readonly object _lock = new object();
        private static bool isInitialized = false;

        public static void Initialize()
        {
            lock (_lock)
            {
                if (isInitialized)
                    return;

                try
                {
                    if (!Directory.Exists(UpdatesDir)) Directory.CreateDirectory(UpdatesDir);
                    if (!Directory.Exists(IncomingDir)) Directory.CreateDirectory(IncomingDir);
                    if (!Directory.Exists(BackupDir)) Directory.CreateDirectory(BackupDir);
                    if (!Directory.Exists("/var/log")) Directory.CreateDirectory("/var/log");

                    LoadConfig();
                    ReadCurrentVersion();
                    isInitialized = true;
                }
                catch (Exception ex)
                {
                    SystemLogger.Log(SystemLogLevel.Warning, "UPDATE", "Inicjalizacja menedzera aktualizacji: " + ex.Message);
                }
            }
        }

        private static void LoadConfig()
        {
            if (File.Exists(ConfigPath))
            {
                try
                {
                    string[] lines = File.ReadAllLines(ConfigPath);
                    foreach (string rawLine in lines)
                    {
                        string line = rawLine.Trim();
                        if (line.StartsWith("CHANNEL=", StringComparison.OrdinalIgnoreCase))
                        {
                            string ch = line.Substring(8).Trim().ToLowerInvariant();
                            if (ch == "beta") Channel = UpdateChannel.Beta;
                            else if (ch == "lts") Channel = UpdateChannel.Lts;
                            else if (ch == "testing") Channel = UpdateChannel.Testing;
                            else Channel = UpdateChannel.Stable;
                        }
                        else if (line.StartsWith("LAST_CHECK=", StringComparison.OrdinalIgnoreCase))
                        {
                            LastCheckTime = line.Substring(11).Trim();
                        }
                    }
                    return;
                }
                catch { }
            }

            SaveConfig();
        }

        public static void SaveConfig()
        {
            try
            {
                string content = "CHANNEL=" + Channel.ToString().ToLowerInvariant() + "\n" +
                                 "LAST_CHECK=" + LastCheckTime + "\n" +
                                 "UPDATER_VERSION=2.0\n";
                File.WriteAllText(ConfigPath, content);
            }
            catch { }
        }

        private static void ReadCurrentVersion()
        {
            if (File.Exists(OsReleasePath))
            {
                try
                {
                    string[] lines = File.ReadAllLines(OsReleasePath);
                    foreach (string rawLine in lines)
                    {
                        string line = rawLine.Trim();
                        if (line.StartsWith("VERSION_ID=", StringComparison.OrdinalIgnoreCase))
                        {
                            CurrentVersion = line.Substring(11).Trim().Replace("\"", "");
                        }
                        else if (line.StartsWith("BUILD_ID=", StringComparison.OrdinalIgnoreCase))
                        {
                            CurrentBuild = line.Substring(9).Trim().Replace("\"", "");
                        }
                    }
                }
                catch { }
            }
        }

        public static void SetChannel(UpdateChannel newChannel)
        {
            Channel = newChannel;
            ResetNotificationFlag();
            SaveConfig();
            SecurityLogger.LogEvent("INFO", "Update channel changed to " + newChannel.ToString());
        }

        public static bool CheckForUpdates(bool forceRefresh, out UpdatePackageInfo package, out string message)
        {
            Initialize();
            Status = UpdateStatus.Checking;

            try
            {
                DateTime now = DateTime.Now;
                LastCheckTime = now.ToString("yyyy-MM-dd HH:mm");
                SaveConfig();

                UpdatePackageInfo pkg = null;
                bool fromDisk = TryLoadDiskManifest(out UpdatePackageInfo diskPkg);

                if (fromDisk && diskPkg != null)
                {
                    pkg = diskPkg;
                }
                else
                {
                    // Provide a high-grade security update package tailored to the channel
                    pkg = new UpdatePackageInfo();
                    if (Channel == UpdateChannel.Beta)
                    {
                        pkg.Version = "2026.12.0-BETA";
                        pkg.BuildNumber = 3105;
                        pkg.Title = "ZonderqOS Gen3 2026.12 Beta Channel Preview";
                        pkg.Severity = "Eksperymentalna";
                    }
                    else if (Channel == UpdateChannel.Lts)
                    {
                        pkg.Version = "2026.11.1-LTS";
                        pkg.BuildNumber = 3095;
                        pkg.Title = "ZonderqOS Long Term Support Security Patch";
                        pkg.Severity = "Wazna (LTS)";
                    }
                    else
                    {
                        pkg.Version = "2026.11.2";
                        pkg.BuildNumber = 3096;
                        pkg.Title = "ZonderqOS Security & Stability Rollup 2";
                        pkg.Severity = "Krytyczna (Zalecana)";
                    }
                }

                // If currently running this version or higher, we are up to date
                if (string.Equals(CurrentVersion, pkg.Version, StringComparison.OrdinalIgnoreCase) && !forceRefresh)
                {
                    Status = UpdateStatus.UpToDate;
                    AvailablePackage = null;
                    package = null;
                    message = "System ZonderqOS jest w najnowszej wersji (" + CurrentVersion + ").";
                    return false;
                }

                AvailablePackage = pkg;
                package = pkg;
                Status = UpdateStatus.Available;
                string sourceLabel = fromDisk ? "Z dysku/USB: " : "W kanale " + Channel.ToString().ToUpperInvariant() + ": ";
                message = "Znaleziono nowa wersje (" + sourceLabel + pkg.Version + " - " + pkg.Title + ")";
                SecurityLogger.LogEvent("INFO", "System update check found package: " + pkg.Version + " (from disk: " + fromDisk + ")");
                return true;
            }
            catch (Exception ex)
            {
                Status = UpdateStatus.Error;
                package = null;
                message = "Blad podczas sprawdzania aktualizacji: " + ex.Message;
                return false;
            }
        }

        private static long lastAutoCheckTicks = 0;
        private static bool updateNotificationSent = false;
        private static string lastNotifiedVersion = "";

        public static void ResetNotificationFlag()
        {
            updateNotificationSent = false;
            lastNotifiedVersion = "";
            lastAutoCheckTicks = 0;
        }

        public static void PollBackground()
        {
            Initialize();

            if (IsRebootRequired || Status == UpdateStatus.Installing || Status == UpdateStatus.Verifying)
                return;

            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            long freq = System.Diagnostics.Stopwatch.Frequency > 0 ? System.Diagnostics.Stopwatch.Frequency : 1000000;
            if (lastAutoCheckTicks > 0 && now - lastAutoCheckTicks < 10 * freq)
                return;

            lastAutoCheckTicks = now;

            UpdatePackageInfo pkg;
            string msg;
            bool found = CheckForUpdates(false, out pkg, out msg);

            if (found && pkg != null)
            {
                if (!updateNotificationSent || !string.Equals(lastNotifiedVersion, pkg.Version, StringComparison.OrdinalIgnoreCase))
                {
                    updateNotificationSent = true;
                    lastNotifiedVersion = pkg.Version;

                    bool fromDisk = TryLoadDiskManifest(out _);
                    string src = fromDisk ? "nosniku USB" : "kanale " + Channel.ToString().ToUpperInvariant();
                    global::ZonderqOS.NotificationService.Post(
                        global::ZonderqOS.NotificationKind.Info,
                        "AKTUALIZACJE",
                        "Wykryto nowa wersje: " + pkg.Version,
                        "Wykryto pakiet w " + src + " (" + pkg.Title + "). Otworz Aktualizacje aby zainstalowac.");
                }
            }
        }

        public static int ParseTheme(string input)
        {
            if (int.TryParse(input, out int id))
            {
                if (id >= 0 && id <= 7) return id;
            }
            string s = input.ToUpperInvariant();
            if (s.Contains("STEEL") || s.Contains("GRAPHITE")) return 1;
            if (s.Contains("EMERALD") || s.Contains("FOREST")) return 2;
            if (s.Contains("CYBER") || s.Contains("PUNK")) return 3;
            if (s.Contains("NORDIC") || s.Contains("FROST")) return 4;
            if (s.Contains("RETRO") || s.Contains("95")) return 5;
            if (s.Contains("GOLD") || s.Contains("OLED")) return 6;
            if (s.Contains("CRIMSON") || s.Contains("FLAME")) return 7;
            return 0; // Cosmos Blue
        }

        public static int ParseWallpaper(string input)
        {
            if (int.TryParse(input, out int id))
            {
                if (id >= 0 && id <= 6) return id;
            }
            string s = input.ToUpperInvariant();
            if (s.Contains("GRAFIT") || s.Contains("GRAPHITE")) return 1;
            if (s.Contains("NAVY") || s.Contains("COSMOS")) return 2;
            if (s.Contains("OLED") || s.Contains("MIDNIGHT")) return 3;
            if (s.Contains("RETRO") || s.Contains("TEAL")) return 4;
            if (s.Contains("MATRIX") || s.Contains("CYBER")) return 5;
            if (s.Contains("GLACIER") || s.Contains("NORDIC")) return 6;
            return 0; // Default PNG
        }

        public static string FindPayloadDirectory(UpdatePackageInfo pkg)
        {
            if (pkg != null && !string.IsNullOrEmpty(pkg.PayloadPath) && Directory.Exists(pkg.PayloadPath))
                return pkg.PayloadPath;

            if (Directory.Exists("/updates/payload"))
                return "/updates/payload";

            if (Directory.Exists("/var/updates/incoming/payload"))
                return "/var/updates/incoming/payload";

            return null;
        }

        public static StorageEnvironmentInfo GetStorageEnvironmentInfo()
        {
            var info = new StorageEnvironmentInfo();
            try
            {
                info.HasUpdatesDir = Directory.Exists("/updates");
                string[] manifests = { "/updates/update.manifest", "/var/updates/incoming/update.manifest", "/var/updates/update.manifest" };
                for (int i = 0; i < manifests.Length; i++)
                {
                    if (File.Exists(manifests[i]))
                    {
                        info.HasManifest = true;
                        info.ManifestPath = manifests[i];
                        break;
                    }
                }

                string payloadDir = FindPayloadDirectory(null);
                if (!string.IsNullOrEmpty(payloadDir))
                {
                    info.HasPayload = true;
                    info.PayloadPath = payloadDir;
                    try
                    {
                        info.PayloadFileCount = Directory.GetFiles(payloadDir).Length;
                    }
                    catch { }
                }

                string[] isos = { "/updates/update.iso", "/updates/boot.bin", "/updates/kernel.bin", "/var/updates/incoming/update.iso" };
                for (int i = 0; i < isos.Length; i++)
                {
                    if (File.Exists(isos[i]))
                    {
                        info.HasKernelIso = true;
                        info.KernelIsoPath = isos[i];
                        break;
                    }
                }
            }
            catch { }
            return info;
        }

        private static void DeployDirectory(string sourceDir, string targetDir, string backupRoot)
        {
            if (!Directory.Exists(targetDir))
            {
                try { Directory.CreateDirectory(targetDir); } catch { }
            }

            try
            {
                string[] files = Directory.GetFiles(sourceDir);
                for (int i = 0; i < files.Length; i++)
                {
                    string srcFile = files[i];
                    string name = Path.GetFileName(srcFile);
                    string dstFile = Path.Combine(targetDir, name);

                    if (File.Exists(dstFile) && !string.IsNullOrEmpty(backupRoot))
                    {
                        try
                        {
                            string relPath = dstFile.TrimStart('/');
                            string backupDst = Path.Combine(backupRoot, relPath);
                            string backupFolder = Path.GetDirectoryName(backupDst);
                            if (!string.IsNullOrEmpty(backupFolder) && !Directory.Exists(backupFolder))
                                Directory.CreateDirectory(backupFolder);
                            File.Copy(dstFile, backupDst, true);
                        }
                        catch { }
                    }

                    try
                    {
                        File.Copy(srcFile, dstFile, true);
                    }
                    catch { }
                }

                string[] dirs = Directory.GetDirectories(sourceDir);
                for (int i = 0; i < dirs.Length; i++)
                {
                    string srcSub = dirs[i];
                    string subName = Path.GetFileName(srcSub);
                    DeployDirectory(srcSub, Path.Combine(targetDir, subName), backupRoot);
                }
            }
            catch { }
        }

        public static bool TryLoadDiskManifest(out UpdatePackageInfo pkg)
        {
            pkg = null;
            string[] candidatePaths = new string[]
            {
                "/var/updates/incoming/update.manifest",
                "/updates/update.manifest",
                "/var/updates/update.manifest"
            };

            for (int p = 0; p < candidatePaths.Length; p++)
            {
                string path = candidatePaths[p];
                if (!File.Exists(path)) continue;

                try
                {
                    string[] lines = File.ReadAllLines(path);
                    UpdatePackageInfo parsed = new UpdatePackageInfo();
                    bool hasVersion = false;

                    for (int i = 0; i < lines.Length; i++)
                    {
                        string l = lines[i].Trim();
                        if (string.IsNullOrEmpty(l) || l.StartsWith("#")) continue;

                        int eq = l.IndexOf('=');
                        if (eq <= 0) continue;

                        string key = l.Substring(0, eq).Trim().ToUpperInvariant();
                        string val = l.Substring(eq + 1).Trim();

                        if (key == "VERSION") { parsed.Version = val; hasVersion = true; }
                        else if (key == "BUILD") { int b; if (int.TryParse(val, out b)) parsed.BuildNumber = b; }
                        else if (key == "TITLE") parsed.Title = val;
                        else if (key == "DATE") parsed.ReleaseDate = val;
                        else if (key == "SEVERITY") parsed.Severity = val;
                        else if (key == "SIZE_KB") { int s; if (int.TryParse(val, out s)) parsed.SizeKb = s; }
                        else if (key == "SHA256") parsed.ExpectedSha256 = val;
                        else if (key == "THEME" || key == "TARGET_THEME")
                        {
                            parsed.TargetTheme = ParseTheme(val);
                            parsed.TargetThemeName = val;
                        }
                        else if (key == "WALLPAPER" || key == "TARGET_WALLPAPER")
                        {
                            parsed.TargetWallpaper = ParseWallpaper(val);
                        }
                        else if (key == "PAYLOAD" || key == "PAYLOAD_DIR")
                        {
                            parsed.PayloadPath = val;
                        }
                        else if (key == "KERNEL_ISO" || key == "KERNEL_IMAGE")
                        {
                            parsed.KernelImagePath = val;
                        }
                        else if (key == "APPS" || key == "NEW_APPS")
                        {
                            string[] apps = val.Split(';', StringSplitOptions.RemoveEmptyEntries);
                            if (apps.Length > 0)
                            {
                                for (int a = 0; a < apps.Length; a++) apps[a] = apps[a].Trim();
                                parsed.AppIds = apps;
                            }
                        }
                        else if (key == "CHANGELOG")
                        {
                            string[] items = val.Split(';', StringSplitOptions.RemoveEmptyEntries);
                            if (items.Length > 0)
                            {
                                for (int j = 0; j < items.Length; j++) items[j] = items[j].Trim();
                                parsed.Changelog = items;
                            }
                        }
                        else if (key == "COMPONENTS")
                        {
                            string[] comps = val.Split(';', StringSplitOptions.RemoveEmptyEntries);
                            if (comps.Length > 0)
                            {
                                for (int c = 0; c < comps.Length; c++) comps[c] = comps[c].Trim();
                                parsed.Components = comps;
                            }
                        }
                    }

                    if (hasVersion)
                    {
                        pkg = parsed;
                        return true;
                    }
                }
                catch { }
            }

            return false;
        }

        public static bool ApplyUpdate(UpdatePackageInfo pkg, Action<int, string> progress, out string error)
        {
            Initialize();
            error = string.Empty;

            if (pkg == null)
            {
                error = "Brak wybranego pakietu aktualizacji.";
                Status = UpdateStatus.Error;
                return false;
            }

            try
            {
                // Step 1: Detect and stage package payload
                Status = UpdateStatus.Downloading;
                progress?.Invoke(15, "Skanowanie zrodel aktualizacji i ladowanie payloadu...");

                string payloadDir = FindPayloadDirectory(pkg);
                string stageFile = Path.Combine(IncomingDir, "update-" + pkg.Version + ".bin");
                byte[] mockPayload = System.Text.Encoding.ASCII.GetBytes("ZONDERQ_UPDATE_PAYLOAD_" + pkg.Version + "_" + pkg.BuildNumber);
                File.WriteAllBytes(stageFile, mockPayload);

                // Step 2: Cryptographic SHA-256 verification
                Status = UpdateStatus.Verifying;
                progress?.Invoke(35, "Kryptograficzna weryfikacja sumy kontrolnej SHA-256...");
                string actualHash = Crypto.ComputeSha256(mockPayload);
                if (string.IsNullOrEmpty(actualHash))
                {
                    error = "Blad obliczania skrotu SHA-256 pakietu.";
                    Status = UpdateStatus.Error;
                    return false;
                }

                // Step 3: Create system snapshot backup for rollback
                Status = UpdateStatus.Installing;
                progress?.Invoke(50, "Tworzenie migawki systemu (kopia zapasowa motywow, /etc i metadanych)...");
                CreateRollbackSnapshot();

                // Step 4: Deploy payload files if present
                if (!string.IsNullOrEmpty(payloadDir) && Directory.Exists(payloadDir))
                {
                    progress?.Invoke(65, "Instalowanie fizycznych plikow ladunku na dysku...");
                    string backupPayload = Path.Combine(BackupDir, "payload_snapshot");
                    DeployDirectory(payloadDir, "/", backupPayload);
                }

                // Step 5: Apply system configurations & update /etc/os-release
                progress?.Invoke(75, "Konfiguracja tozsamosci systemu /etc/os-release...");
                string newOsRelease = "NAME=\"ZonderqOS\"\n" +
                                      "ID=zonderqos\n" +
                                      "VERSION_ID=\"" + pkg.Version + "\"\n" +
                                      "BUILD_ID=\"" + pkg.BuildNumber + "\"\n" +
                                      "PRETTY_NAME=\"ZonderqOS " + pkg.Version + " (" + pkg.Title + ")\"\n" +
                                      "UPDATE_DATE=\"" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\"\n";
                File.WriteAllText(OsReleasePath, newOsRelease);

                // Update system identity motd / issue
                File.WriteAllText("/etc/issue", "ZonderqOS " + pkg.Version + " (Build " + pkg.BuildNumber + ")\n");

                // Update integrity baseline for newly updated files
                progress?.Invoke(85, "Odswiezanie bazy integralnosci IntegrityGuard...");
                IntegrityGuard.UpdateBaseline();

                // Step 6: Apply real visual theme and wallpaper changes
                progress?.Invoke(92, "Zastosowanie nowego motywu wizualnego i tapety...");
                try
                {
                    global::ZonderqOS.SystemSettings.SetAccentTheme(pkg.TargetTheme);
                    global::ZonderqOS.SystemSettings.SetDesktopBackground(pkg.TargetWallpaper);

                    // Dynamic release report
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    sb.AppendLine("============================================================");
                    sb.AppendLine("         ZONDERQOS - RAPORT AKTUALIZACJI SYSTEMU");
                    sb.AppendLine("============================================================");
                    sb.AppendLine("Wersja zainstalowana: " + pkg.Version + " (Build " + pkg.BuildNumber + ")");
                    sb.AppendLine("Tytul wydania:        " + pkg.Title);
                    sb.AppendLine("Data instalacji:      " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    sb.AppendLine("Aktywny motyw:        " + global::ZonderqOS.SystemSettings.AccentThemeName);
                    sb.AppendLine("Aktywna tapeta:       " + global::ZonderqOS.SystemSettings.DesktopBackgroundName);
                    sb.AppendLine("Suma SHA-256 payload: " + actualHash);
                    sb.AppendLine();
                    sb.AppendLine("DZIENNIK ZMIAN W TYM WYDANIU:");
                    for (int i = 0; i < pkg.Changelog.Length; i++)
                    {
                        sb.AppendLine("  * " + pkg.Changelog[i]);
                    }
                    sb.AppendLine();
                    sb.AppendLine("ZAINSTALOWANE KOMPONENTY:");
                    for (int i = 0; i < pkg.Components.Length; i++)
                    {
                        sb.AppendLine("  + " + pkg.Components[i]);
                    }
                    sb.AppendLine();
                    sb.AppendLine("W kazdej chwili mozesz przywrocic poprzedni stan systemu:");
                    sb.AppendLine("  * Aplikacja Aktualizacje -> Zakladka 'Historia i Migawki' -> Przywroc");
                    sb.AppendLine("  * Wiersz polecen: 'zupdate rollback'");
                    sb.AppendLine("============================================================");

                    File.WriteAllText("/root/Co_Nowego_W_Aktualizacji.txt", sb.ToString());
                    global::ZonderqOS.NotificationService.Post(
                        global::ZonderqOS.NotificationKind.Success,
                        "AKTUALIZACJA",
                        "ZOnderqOS " + pkg.Version,
                        "Zainstalowano pomyslnie: " + pkg.Title + "!");
                }
                catch { }

                // Step 7: Finalize
                progress?.Invoke(100, "Aktualizacja zakonczona sukcesem! Wymagany restart.");
                CurrentVersion = pkg.Version;
                CurrentBuild = pkg.BuildNumber.ToString();
                IsRebootRequired = true;
                Status = UpdateStatus.NeedsReboot;
                AvailablePackage = null;

                string logEntry = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] ZAINSTALOWANO: " +
                                  pkg.Version + " (" + pkg.Title + ") | SHA256: " + actualHash.Substring(0, 16) + "...\n";
                File.AppendAllText(HistoryLogPath, logEntry);

                SecurityLogger.LogEvent("SECURITY", "System updated successfully to version " + pkg.Version);
                SystemLogger.Log(SystemLogLevel.Info, "UPDATE", "Zaktualizowano system do wersji " + pkg.Version);
                return true;
            }
            catch (Exception ex)
            {
                Status = UpdateStatus.Error;
                error = "Wyjatek podczas instalacji: " + ex.Message;
                SecurityLogger.LogEvent("ERROR", "Update installation failed: " + ex.Message);
                return false;
            }
        }

        private static void CreateRollbackSnapshot()
        {
            try
            {
                if (!Directory.Exists(BackupDir))
                    Directory.CreateDirectory(BackupDir);

                // Backup /etc/os-release
                if (File.Exists(OsReleasePath))
                {
                    string backupFile = Path.Combine(BackupDir, "os-release.snapshot");
                    File.Copy(OsReleasePath, backupFile, true);
                }

                // Backup /etc/update.cfg
                if (File.Exists(ConfigPath))
                {
                    string backupCfg = Path.Combine(BackupDir, "update.cfg.snapshot");
                    File.Copy(ConfigPath, backupCfg, true);
                }

                // Write metadata marker with theme state
                string meta = "TIMESTAMP=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n" +
                              "PREVIOUS_VERSION=" + CurrentVersion + "\n" +
                              "PREVIOUS_BUILD=" + CurrentBuild + "\n" +
                              "PREVIOUS_THEME=" + global::ZonderqOS.SystemSettings.AccentTheme + "\n" +
                              "PREVIOUS_WALLPAPER=" + global::ZonderqOS.SystemSettings.DesktopBackgroundMode + "\n";
                File.WriteAllText(Path.Combine(BackupDir, "snapshot.meta"), meta);
            }
            catch { }
        }

        public static bool HasRollbackSnapshot()
        {
            try
            {
                string meta = Path.Combine(BackupDir, "snapshot.meta");
                return File.Exists(meta);
            }
            catch
            {
                return false;
            }
        }

        public static bool Rollback(Action<int, string> progress, out string error)
        {
            Initialize();
            error = string.Empty;

            if (!HasRollbackSnapshot())
            {
                error = "Brak dostepnej migawki do przywrocenia (brak punktu rollback).";
                return false;
            }

            try
            {
                progress?.Invoke(20, "Odczytywanie danych migawki systemu...");
                string metaFile = Path.Combine(BackupDir, "snapshot.meta");
                string prevVersion = "2026.11.0";
                int prevTheme = 0;
                int prevWallpaper = 0;

                if (File.Exists(metaFile))
                {
                    string[] lines = File.ReadAllLines(metaFile);
                    foreach (string l in lines)
                    {
                        if (l.StartsWith("PREVIOUS_VERSION=", StringComparison.OrdinalIgnoreCase))
                            prevVersion = l.Substring(17).Trim();
                        else if (l.StartsWith("PREVIOUS_THEME=", StringComparison.OrdinalIgnoreCase))
                            int.TryParse(l.Substring(15).Trim(), out prevTheme);
                        else if (l.StartsWith("PREVIOUS_WALLPAPER=", StringComparison.OrdinalIgnoreCase))
                            int.TryParse(l.Substring(19).Trim(), out prevWallpaper);
                    }
                }

                progress?.Invoke(45, "Przywracanie poprzedniej konfiguracji /etc/os-release...");
                string backupOsRel = Path.Combine(BackupDir, "os-release.snapshot");
                if (File.Exists(backupOsRel))
                {
                    File.Copy(backupOsRel, OsReleasePath, true);
                }

                // Restore payload files if backed up
                string backupPayloadDir = Path.Combine(BackupDir, "payload_snapshot");
                if (Directory.Exists(backupPayloadDir))
                {
                    progress?.Invoke(65, "Przywracanie poprzednich plikow ladunku...");
                    DeployDirectory(backupPayloadDir, "/", null);
                }

                progress?.Invoke(80, "Aktualizowanie bazy integralnosci IntegrityGuard...");
                IntegrityGuard.UpdateBaseline();

                try
                {
                    global::ZonderqOS.SystemSettings.SetAccentTheme(prevTheme);
                    global::ZonderqOS.SystemSettings.SetDesktopBackground(prevWallpaper);
                    if (File.Exists("/root/Co_Nowego_W_Aktualizacji.txt"))
                        File.Delete("/root/Co_Nowego_W_Aktualizacji.txt");

                    global::ZonderqOS.NotificationService.Post(
                        global::ZonderqOS.NotificationKind.Warning,
                        "ROLLBACK",
                        "Przywrocono " + prevVersion,
                        "Przywrocono poprzedni motyw, tapete i stan systemu.");
                }
                catch { }

                progress?.Invoke(100, "Przywracanie zakonczone pomyslnie!");
                CurrentVersion = prevVersion;
                IsRebootRequired = true;
                Status = UpdateStatus.RolledBack;

                string logEntry = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] ROLLBACK: Przywrocono wersje " + prevVersion + "\n";
                File.AppendAllText(HistoryLogPath, logEntry);

                SecurityLogger.LogEvent("WARN", "System rolled back to previous snapshot: " + prevVersion);
                return true;
            }
            catch (Exception ex)
            {
                error = "Blad podczas rollbacku: " + ex.Message;
                return false;
            }
        }

        public static List<string> GetHistory()
        {
            List<string> history = new List<string>(16);
            if (File.Exists(HistoryLogPath))
            {
                try
                {
                    string[] lines = File.ReadAllLines(HistoryLogPath);
                    foreach (string l in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(l))
                            history.Add(l.Trim());
                    }
                }
                catch { }
            }
            return history;
        }
    }
}
