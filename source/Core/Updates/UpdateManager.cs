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

    public enum UpdateSourceMode
    {
        UsbOffline = 0, // Primary & verified: USB / local storage
        CloudAuto = 1,  // Cloud check fallback
        CloudOnly = 2   // Cloud servers only
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
        public string Version { get; set; } = "1.0.0";
        public int BuildNumber { get; set; } = 3100;
        public string Architecture { get; set; } = "x86_64";
        public string Title { get; set; } = "Aktualizacja Systemu ZonderqOS";
        public string ReleaseDate { get; set; } = "2026-11-08";
        public string Severity { get; set; } = "Zalecana";
        public int SizeKb { get; set; } = 0;
        public string ExpectedSha256 { get; set; } = string.Empty;
        public string KernelSha256 { get; set; } = string.Empty;
        public string KernelImagePath { get; set; } = string.Empty;
        public string PayloadPath { get; set; } = string.Empty;
        public string ManifestPath { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public bool IsSigned { get; set; } = false;
        public bool IsSignatureValid { get; set; } = false;
        public int TargetTheme { get; set; } = 0;
        public string TargetThemeName { get; set; } = "COSMOS BLUE";
        public int TargetWallpaper { get; set; } = 0;
        public List<UpdateFileEntry> Files { get; set; } = new List<UpdateFileEntry>();
        public string[] Changelog { get; set; } = Array.Empty<string>();
        public string[] Components { get; set; } = Array.Empty<string>();
    }

    public sealed class StorageEnvironmentInfo
    {
        public string PartitionLabel { get; set; } = "ZONDERQDATA";
        public string DeviceType { get; set; } = "Dysk USB / Dedykowany";
        public bool HasUpdatesDir { get; set; } = false;
        public bool HasManifest { get; set; } = false;
        public bool HasPayload { get; set; } = false;
        public bool HasKernelBinary { get; set; } = false;
        public string ManifestPath { get; set; } = string.Empty;
        public string PayloadPath { get; set; } = string.Empty;
        public string KernelBinaryPath { get; set; } = string.Empty;
        public int PayloadFileCount { get; set; } = 0;
        public string ActiveBootSlot { get; set; } = "A";
        public string RunningKernelVersion { get; set; } = KernelBuildInfo.KernelVersion;
        public int RunningKernelBuild { get; set; } = KernelBuildInfo.KernelBuild;
    }

    public static class UpdateManager
    {
        private const string ConfigPath = "/etc/update.cfg";
        private const string HistoryLogPath = "/var/log/update.log";
        private const string UpdatesDir = "/var/updates";
        private const string IncomingDir = "/var/updates/incoming";
        private const string BackupDir = "/var/updates/backup";
        private const string OsReleasePath = "/etc/os-release";

        public static string CurrentVersion { get; private set; } = KernelBuildInfo.KernelVersion;
        public static string CurrentBuild { get; private set; } = KernelBuildInfo.KernelBuild.ToString();
        public static string InstalledVersion { get; private set; } = string.Empty;
        public static string InstalledBuild { get; private set; } = string.Empty;
        public static UpdateChannel Channel { get; private set; } = UpdateChannel.Stable;
        public static UpdateSourceMode SourceMode { get; private set; } = UpdateSourceMode.UsbOffline;
        public static string CloudServerUrl { get; private set; } = "https://updates.zonderqos.org/v1";
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
                    BootSlotManager.Initialize();

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
                        else if (line.StartsWith("INSTALLED_VERSION=", StringComparison.OrdinalIgnoreCase))
                        {
                            InstalledVersion = line.Substring(18).Trim();
                        }
                        else if (line.StartsWith("INSTALLED_BUILD=", StringComparison.OrdinalIgnoreCase))
                        {
                            InstalledBuild = line.Substring(16).Trim();
                        }
                        else if (line.StartsWith("LAST_NOTIFIED=", StringComparison.OrdinalIgnoreCase))
                        {
                            lastNotifiedVersion = line.Substring(14).Trim();
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
                                 "UPDATER_VERSION=3.0\n" +
                                 "INSTALLED_VERSION=" + (string.IsNullOrEmpty(InstalledVersion) ? CurrentVersion : InstalledVersion) + "\n" +
                                 "INSTALLED_BUILD=" + (string.IsNullOrEmpty(InstalledBuild) ? CurrentBuild : InstalledBuild) + "\n" +
                                 "LAST_NOTIFIED=" + lastNotifiedVersion + "\n";
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

        public static void SetSourceMode(UpdateSourceMode newMode)
        {
            SourceMode = newMode;
            ResetNotificationFlag();
            SaveConfig();
            SecurityLogger.LogEvent("INFO", "Update source mode changed to " + newMode.ToString());
        }

        /// <summary>
        /// Explicitly unhandled online downloading on Cosmos stock without full HTTPS network stack.
        /// Does NOT fabricate a fake download or mock payload.
        /// </summary>
        public static bool DownloadOnlinePackage(UpdatePackageInfo pkg, Action<int, string> progress, out string error)
        {
            error = "Pobieranie OTA przez siec niedostepne w tej wersji: " +
                    "Stos sieciowy HTTPS nie jest aktywny w stock Cosmos 3.0.95 x64. " +
                    "Uzyj rzeczywistego pakietu z dedykowanego nosnika USB (/updates/update.manifest).";
            Status = UpdateStatus.Error;
            return false;
        }

        public static bool IsNewerVersion(string candidateVer, int candidateBuild, string currentVer, string currentBuild)
        {
            if (string.IsNullOrWhiteSpace(candidateVer)) return false;
            if (string.IsNullOrWhiteSpace(currentVer)) return true;

            int curBuildNum = 0;
            int.TryParse(currentBuild, out curBuildNum);

            int[] cand = ExtractVersionParts(candidateVer);
            int[] curr = ExtractVersionParts(currentVer);

            for (int i = 0; i < 3; i++)
            {
                if (cand[i] > curr[i]) return true;
                if (cand[i] < curr[i]) return false;
            }

            return candidateBuild > curBuildNum;
        }

        private static int[] ExtractVersionParts(string ver)
        {
            int[] res = new int[3];
            if (string.IsNullOrEmpty(ver)) return res;

            int dash = ver.IndexOf('-');
            string clean = dash >= 0 ? ver.Substring(0, dash) : ver;
            string[] parts = clean.Split('.');
            for (int i = 0; i < parts.Length && i < 3; i++)
            {
                int val;
                if (int.TryParse(parts[i].Trim(), out val))
                    res[i] = val;
            }
            return res;
        }

        public static string FindKernelSource(UpdatePackageInfo pkg)
        {
            // Never fall back to a different package's kernel when the caller
            // provided a concrete package. Discovery-only fallback is separate.
            if (pkg != null)
                return !string.IsNullOrEmpty(pkg.KernelImagePath) && File.Exists(pkg.KernelImagePath)
                    ? pkg.KernelImagePath : null;

            string[] candidates = {
                "/updates/ZonderqOS.elf",
                "/updates/kernel/ZonderqOS.elf",
                "/var/updates/incoming/ZonderqOS.elf",
                "/var/updates/incoming/kernel/ZonderqOS.elf"
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i]))
                    return candidates[i];
            }
            return null;
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
                    // No disk package found
                    Status = UpdateStatus.UpToDate;
                    AvailablePackage = null;
                    package = null;

                    if (SourceMode == UpdateSourceMode.CloudOnly)
                    {
                        message = "Tryb CloudOnly: Stos sieciowy HTTPS nie jest wlaczony w jadrze. Brak pakietow.";
                    }
                    else
                    {
                        message = "System ZonderqOS jest w najnowszej wersji (" + KernelBuildInfo.KernelVersion + ", build " + KernelBuildInfo.KernelBuild + "). Brak nowych pakietow na USB (/updates).";
                    }
                    return false;
                }

                // Check version against the genuinely executing kernel and installed version
                bool isNewerThanRunning = IsNewerVersion(pkg.Version, pkg.BuildNumber, KernelBuildInfo.KernelVersion, KernelBuildInfo.KernelBuild.ToString());
                bool isNewerThanInstalled = IsNewerVersion(pkg.Version, pkg.BuildNumber, CurrentVersion, CurrentBuild);

                if (!isNewerThanRunning && !isNewerThanInstalled && !forceRefresh)
                {
                    Status = UpdateStatus.UpToDate;
                    AvailablePackage = null;
                    package = null;
                    message = "System ZonderqOS jest w najnowszej wersji (" + KernelBuildInfo.KernelVersion + "). Pakiet " + pkg.Version + " nie jest nowszy.";
                    return false;
                }

                // Discovery is not verification. Never advertise a legacy HMAC
                // package as available/trusted while the P0 gate is active.
                if (!TryVerifyTrustedPackage(pkg, out string verificationError))
                {
                    AvailablePackage = null;
                    package = null;
                    Status = UpdateStatus.Error;
                    message = "Pakiet USB odrzucony: " + verificationError;
                    return false;
                }
                pkg.IsSigned = true;
                pkg.IsSignatureValid = true;

                AvailablePackage = pkg;
                package = pkg;
                Status = UpdateStatus.Available;

                string signInfo = pkg.IsSigned ? (pkg.IsSignatureValid ? " [PODPIS CYFROWY: ZAUFANY]" : " [PODPIS: NIEPOPRAWNY!]") : " [NIEPODPISANY]";
                message = "Znaleziono zweryfikowana aktualizacje USB: " + pkg.Version + " (Build " + pkg.BuildNumber + ") - " + pkg.Title + signInfo;

                SecurityLogger.LogEvent("INFO", "Update package found on USB: " + pkg.Version + signInfo);
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
        private static string lastNotifiedVersion = string.Empty;

        public static void ResetNotificationFlag()
        {
            updateNotificationSent = false;
            lastNotifiedVersion = string.Empty;
            lastAutoCheckTicks = 0;
        }

        public static void PollBackground()
        {
            Initialize();

            if (IsRebootRequired || Status == UpdateStatus.Installing || Status == UpdateStatus.Verifying)
                return;

            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            long freq = System.Diagnostics.Stopwatch.Frequency > 0 ? System.Diagnostics.Stopwatch.Frequency : 1000000;
            if (lastAutoCheckTicks > 0 && now - lastAutoCheckTicks < 15 * freq)
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

                    global::ZonderqOS.NotificationService.Post(
                        global::ZonderqOS.NotificationKind.Info,
                        "AKTUALIZACJE USB",
                        "Wykryto nowa wersje: " + pkg.Version,
                        "Wykryto pakiet aktualizacji w /updates (" + pkg.Title + "). Otworz Centrum Aktualizacji.");
                }
            }
        }

        public static int ParseTheme(string input)
        {
            if (int.TryParse(input, out int id))
            {
                if (id >= 0 && id <= 7) return id;
            }
            string s = (input ?? "").ToUpperInvariant();
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
            string s = (input ?? "").ToUpperInvariant();
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
            if (pkg != null)
                return !string.IsNullOrEmpty(pkg.PayloadPath) && Directory.Exists(pkg.PayloadPath)
                    ? pkg.PayloadPath : null;

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
                info.ActiveBootSlot = BootSlotManager.ActiveSlot;
                info.RunningKernelVersion = KernelBuildInfo.KernelVersion;
                info.RunningKernelBuild = KernelBuildInfo.KernelBuild;

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

                string kernelSrc = FindKernelSource(null);
                if (!string.IsNullOrEmpty(kernelSrc))
                {
                    info.HasKernelBinary = true;
                    info.KernelBinaryPath = kernelSrc;
                }
            }
            catch { }
            return info;
        }

        public static bool TryLoadDiskManifest(out UpdatePackageInfo pkg)
        {
            pkg = null;
            string[] candidatePaths = new string[]
            {
                "/updates/update.manifest",
                "/var/updates/incoming/update.manifest",
                "/var/updates/update.manifest"
            };

            for (int p = 0; p < candidatePaths.Length; p++)
            {
                string path = candidatePaths[p];
                if (!File.Exists(path)) continue;

                try
                {
                    string content = File.ReadAllText(path);
                    if (UpdateManifest.TryParse(content, out UpdateManifest manifest, out string error))
                    {
                        var info = new UpdatePackageInfo
                        {
                            Version = manifest.Version,
                            BuildNumber = manifest.BuildNumber,
                            Architecture = manifest.Architecture,
                            Title = manifest.Title,
                            ReleaseDate = manifest.ReleaseDate,
                            Severity = manifest.Severity,
                            TargetTheme = manifest.TargetTheme,
                            TargetThemeName = manifest.TargetThemeName,
                            TargetWallpaper = manifest.TargetWallpaper,
                            ManifestPath = path,
                            Signature = manifest.Signature,
                            KernelSha256 = manifest.KernelSha256,
                            Files = manifest.Files,
                            Changelog = manifest.Changelog.ToArray(),
                            Components = manifest.Components.ToArray()
                        };

                        // Parsing is not authentication. Detached Ed25519
                        // verification happens in TryVerifyTrustedPackage
                        // before the package can be advertised or installed.
                        info.IsSigned = false;
                        info.IsSignatureValid = false;

                        // Determine kernel image location
                        string baseDir = Path.GetDirectoryName(path);
                        string kernelCandidate = Path.Combine(baseDir, manifest.KernelImage);
                        if (File.Exists(kernelCandidate))
                        {
                            info.KernelImagePath = kernelCandidate;
                            try
                            {
                                FileInfo fi = new FileInfo(kernelCandidate);
                                info.SizeKb = (int)(fi.Length / 1024);
                            }
                            catch { }
                        }
                        // Never substitute a kernel from another USB location:
                        // the manifest and all binaries must come from the same
                        // update package. Missing file is a verification error.

                        // Determine payload directory
                        if (!string.IsNullOrEmpty(manifest.PayloadDirectory))
                        {
                            string payloadCand = Path.Combine(baseDir, manifest.PayloadDirectory);
                            if (Directory.Exists(payloadCand))
                                info.PayloadPath = payloadCand;
                        }

                        pkg = info;
                        return true;
                    }
                }
                catch { }
            }

            return false;
        }

        /// <summary>
        /// Genuinely verifies all cryptographic hashes of files listed in package.
        /// </summary>
        public static bool VerifyPackageIntegrity(UpdatePackageInfo pkg, Action<int, string> progress, out string error)
        {
            error = string.Empty;
            if (pkg == null)
            {
                error = "Brak pakietu do weryfikacji.";
                return false;
            }

            progress?.Invoke(10, "Sprawdzanie obecnosci plikow pakietu...");

            // 1. Verify kernel binary
            string kernelSrc = FindKernelSource(pkg);
            if (string.IsNullOrEmpty(kernelSrc) || !File.Exists(kernelSrc))
            {
                error = "Nie znaleziono pliku binarnego kernela (ZonderqOS.elf) w pakiecie.";
                return false;
            }

            progress?.Invoke(30, "Kryptograficzne obliczanie sumy SHA-256 z rzeczywistego pliku kernela na dysku...");
            byte[] kernelBytes = File.ReadAllBytes(kernelSrc);
            string actualKernelSha = Crypto.ComputeSha256(kernelBytes);

            if (string.IsNullOrEmpty(pkg.KernelSha256))
            {
                error = "Odrzucono pakiet: brak SHA-256 kernela.";
                return false;
            }
            {
                if (!Crypto.FixedTimeEquals(actualKernelSha.ToLowerInvariant(), pkg.KernelSha256.ToLowerInvariant()))
                {
                    error = $"Niezgodnosc sumy kontrolnej kernela! Oczekiwano: {pkg.KernelSha256}, rzeczywista: {actualKernelSha}";
                    return false;
                }
            }
            pkg.ExpectedSha256 = actualKernelSha;
            progress?.Invoke(45, "Suma SHA-256 kernela prawidlowa.");

            // 2. Verify payload files and reject all undeclared additions.
            var declaredPayloadFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string payloadBase = FindPayloadDirectory(pkg);
            if (pkg.Files != null && pkg.Files.Count > 0)
            {
                if (string.IsNullOrEmpty(payloadBase))
                {
                    error = "Brak wymaganego katalogu payload.";
                    return false;
                }
                for (int i = 0; i < pkg.Files.Count; i++)
                {
                    var fileEntry = pkg.Files[i];
                    if (fileEntry == null)
                    {
                        error = "Pusty wpis FILE w pakiecie.";
                        return false;
                    }
                    int pct = 30 + (int)((i + 1) * 50.0 / pkg.Files.Count);
                    progress?.Invoke(pct, "Weryfikacja sumy pliku: " + fileEntry.RelativePath + "...");

                    if (!declaredPayloadFiles.Add(fileEntry.RelativePath) ||
                        !UpdateManifest.IsSafeRelativePath(fileEntry.RelativePath) ||
                        !IsAllowedUpdatePayloadPath(fileEntry.RelativePath))
                    {
                        error = "Niedozwolona lub niepoprawna sciezka w pakiecie.";
                        return false;
                    }
                    string fullPath = string.IsNullOrEmpty(payloadBase) ? null : Path.Combine(payloadBase, fileEntry.RelativePath);
                    if (fullPath != null && !IsSafeUpdateFilePath(payloadBase, fileEntry.RelativePath))
                    {
                        error = "Niedozwolony dowiazany symbolicznie plik lub katalog payload: " + fileEntry.RelativePath;
                        return false;
                    }
                    if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
                    {
                        error = "Brak pliku w payload: " + fileEntry.RelativePath;
                        return false;
                    }

                    long actualSize = new FileInfo(fullPath).Length;
                    if (fileEntry.SizeBytes > 0 && actualSize != fileEntry.SizeBytes)
                    {
                        error = "Niezgodny rozmiar pliku payload: " + fileEntry.RelativePath;
                        return false;
                    }
                    byte[] fBytes = File.ReadAllBytes(fullPath);
                    string actualFHash = Crypto.ComputeSha256(fBytes);
                    if (!Crypto.FixedTimeEquals(actualFHash.ToLowerInvariant(), fileEntry.ExpectedSha256.ToLowerInvariant()))
                    {
                        error = $"Blad sumy SHA-256 dla {fileEntry.RelativePath}! Oczekiwano: {fileEntry.ExpectedSha256}, otrzymano: {actualFHash}";
                        return false;
                    }
                }
            }

            if (!string.IsNullOrEmpty(payloadBase) && Directory.Exists(payloadBase))
            {
                string[] physicalFiles = Directory.GetFiles(payloadBase, "*", SearchOption.AllDirectories);
                for (int i = 0; i < physicalFiles.Length; i++)
                {
                    string relative = physicalFiles[i].Substring(payloadBase.TrimEnd('/').Length).TrimStart('/', '\\');
                    relative = relative.Replace('\\', '/');
                    if (!declaredPayloadFiles.Contains(relative) ||
                        !IsSafeUpdateFilePath(payloadBase, relative))
                    {
                        error = "Pakiet zawiera niezadeklarowany lub niebezpieczny plik payload: " + relative;
                        return false;
                    }
                }
            }

            // 3. Integrity is distinct from authenticity. This method
            // checks SHA-256 only; ApplyUpdateStep additionally requires a
            // valid Ed25519 signature over every byte of the manifest.
            progress?.Invoke(90, "Wszystkie zadeklarowane sumy SHA-256 sa prawidlowe.");
            return true;
        }

        // Early allowlist: do not install arbitrary packages into /boot,
        // /etc, /var, /root or foreign partitions. Installed system payloads
        // must live under these two explicitly managed roots.
        private static bool IsAllowedUpdatePayloadPath(string relativePath)
        {
            return UpdateManifest.IsSafeRelativePath(relativePath) &&
                (relativePath.StartsWith("system/", StringComparison.OrdinalIgnoreCase) ||
                 relativePath.StartsWith("usr/", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsSafeUpdateFilePath(string root, string relativePath)
        {
            if (string.IsNullOrEmpty(root) || !UpdateManifest.IsSafeRelativePath(relativePath))
                return false;
            try
            {
                if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                    return false;
                string current = root;
                string[] parts = relativePath.Split('/');
                for (int i = 0; i < parts.Length; i++)
                {
                    current = Path.Combine(current, parts[i]);
                    if ((File.Exists(current) || Directory.Exists(current)) &&
                        (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        return false;
                }
                return true;
            }
            catch { return false; }
        }

        // This gate requires a build-provisioned immutable PUBLIC key.
        // It can never be enabled by data found on USB.
        private static bool HasVerifiedAsymmetricUpdateTrust()
        {
            return ReleaseSignatureVerifier.HasProductionKey();
        }

        /// <summary>
        /// Authenticate exact original manifest bytes and ensure every mutable
        /// UpdatePackageInfo field still corresponds to the signed manifest.
        /// Re-run this check before every installer step to prevent resuming an
        /// untrusted partially-completed update.
        /// </summary>
        private static bool TryVerifyTrustedPackage(UpdatePackageInfo pkg, out string error)
        {
            error = string.Empty;
            if (!HasVerifiedAsymmetricUpdateTrust())
            {
                error = "Brak zaufanego klucza publicznego Ed25519 w obrazie kernela.";
                return false;
            }
            if (pkg == null || string.IsNullOrEmpty(pkg.ManifestPath))
            {
                error = "Brak manifestu aktualizacji.";
                return false;
            }
            try
            {
                // Only explicit, managed update locations are valid sources.
                string manifestPath = pkg.ManifestPath;
                if (manifestPath != "/updates/update.manifest" &&
                    manifestPath != "/var/updates/incoming/update.manifest" &&
                    manifestPath != "/var/updates/update.manifest")
                {
                    error = "Niezatwierdzona lokalizacja manifestu: " + manifestPath;
                    return false;
                }
                string baseDir = Path.GetDirectoryName(manifestPath);
                if (!IsSafeUpdateFilePath(baseDir, "update.manifest") ||
                    !IsSafeUpdateFilePath(baseDir, "update.manifest.sig"))
                {
                    error = "Niedozwolony symlink manifestu lub podpisu.";
                    return false;
                }

                byte[] bytes = File.ReadAllBytes(manifestPath);
                if (bytes.Length == 0 || bytes.Length > 1024 * 1024)
                {
                    error = "Niepoprawny rozmiar podpisanego manifestu.";
                    return false;
                }
                string sigPath = manifestPath + ".sig";
                if (!File.Exists(sigPath) || new FileInfo(sigPath).Length != 64)
                {
                    error = "Brak 64-bajtowego podpisu Ed25519.";
                    return false;
                }
                byte[] signature = File.ReadAllBytes(sigPath);
                if (!ReleaseSignatureVerifier.VerifyProductionManifest(bytes, signature))
                {
                    error = "Podpis Ed25519 manifestu jest niepoprawny.";
                    return false;
                }
                pkg.IsSigned = true;
                pkg.IsSignatureValid = true;
                // Parse only after verifying the raw detached signature.
                string text = System.Text.Encoding.UTF8.GetString(bytes);
                if (!UpdateManifest.TryParse(text, out UpdateManifest parsed, out error))
                    return false;
                if (parsed.UpdateFormat != 2 || parsed.SignatureAlgorithm != "ED25519")
                {
                    error = "Odrzucono niepodpisany format legacy HMAC.";
                    return false;
                }

                string kernelRelative = parsed.KernelImage;
                string payloadRelative = parsed.PayloadDirectory;
                if (!UpdateManifest.IsSafeRelativePath(kernelRelative) ||
                    !UpdateManifest.IsSafeRelativePath(payloadRelative) ||
                    !IsSafeUpdateFilePath(baseDir, kernelRelative) ||
                    !IsSafeUpdateFilePath(baseDir, payloadRelative))
                {
                    error = "Niebezpieczna sciezka kernela/payload.";
                    return false;
                }
                string expectedKernel = Path.Combine(baseDir, kernelRelative);
                string expectedPayload = Path.Combine(baseDir, payloadRelative);
                if (pkg.KernelImagePath != expectedKernel ||
                    (pkg.PayloadPath != expectedPayload &&
                     !(string.IsNullOrEmpty(pkg.PayloadPath) && parsed.Files.Count == 0)) ||
                    pkg.Version != parsed.Version ||
                    pkg.BuildNumber != parsed.BuildNumber ||
                    pkg.Architecture != parsed.Architecture ||
                    pkg.Title != parsed.Title ||
                    pkg.KernelSha256 != parsed.KernelSha256 ||
                    pkg.TargetTheme != parsed.TargetTheme ||
                    pkg.TargetWallpaper != parsed.TargetWallpaper ||
                    pkg.Files == null || pkg.Files.Count != parsed.Files.Count)
                {
                    error = "Dane pakietu nie sa identyczne z podpisanym manifestem.";
                    return false;
                }
                for (int i = 0; i < parsed.Files.Count; i++)
                {
                    UpdateFileEntry given = pkg.Files[i], signed = parsed.Files[i];
                    if (given == null || given.RelativePath != signed.RelativePath ||
                        !string.Equals(given.ExpectedSha256, signed.ExpectedSha256,
                            StringComparison.OrdinalIgnoreCase) ||
                        given.SizeBytes != signed.SizeBytes)
                    {
                        error = "Zmieniono liste plikow po weryfikacji podpisu.";
                        return false;
                    }
                }

                // Re-hash all declared files on every installer stage, not just
                // once at step 0. Staging writes are separately read back.
                if (!VerifyPackageIntegrity(pkg, null, out error))
                    return false;
                return true;
            }
            catch (Exception ex)
            {
                error = "Nie mozna zweryfikowac podpisanego pakietu: " + ex.Message;
                return false;
            }
        }

        public static bool ApplyUpdateStep(UpdatePackageInfo pkg, ref int step, Action<int, string> progress, out string error)
        {
            // Authenticate the entire exact manifest and every declared file
            // before EACH step, including resumed step > 0 calls.
            if (!TryVerifyTrustedPackage(pkg, out error))
            {
                Status = UpdateStatus.Error;
                try
                {
                    SecurityLogger.LogEvent("SECURITY_ERROR",
                        "Updater rejected unverified Ed25519 package: " + error);
                }
                catch { }
                return false;
            }

            Initialize();
            error = string.Empty;
            if (pkg == null)
            {
                error = "Brak wybranego pakietu aktualizacji.";
                Status = UpdateStatus.Error;
                return false;
            }

            // Never replace the only trusted recovery snapshot while an
            // earlier new kernel is still awaiting boot-health confirmation.
            // This runs before case 1 deletes any old backup directory.
            if (step >= 0 && step <= 4 &&
                !string.IsNullOrEmpty(BootSlotManager.PendingSlot))
            {
                error = "Poprzednia aktualizacja A/B nadal oczekuje na potwierdzenie rozruchu; migawka rollback jest chroniona.";
                Status = UpdateStatus.Error;
                return false;
            }

            try
            {
                switch (step)
                {
                    case 0:
                        // Step 1: Pre-installation integrity verification
                        Status = UpdateStatus.Verifying;
                        progress?.Invoke(15, "Weryfikacja sum kontrolnych pakietu aktualizacji...");
                        if (!VerifyPackageIntegrity(pkg, progress, out error))
                        {
                            Status = UpdateStatus.Error;
                            SecurityLogger.LogEvent("SECURITY_ERROR", "Aktualizacja przerwana: niepowodzenie weryfikacji sumy kontrolnej: " + error);
                            return false;
                        }
                        step = 1;
                        return true;

                    case 1:
                        // Step 2: Create full system rollback snapshot
                        Status = UpdateStatus.Installing;
                        progress?.Invoke(35, "Tworzenie bezpiecznej migawki systemu (/var/updates/backup)...");
                        CreateRollbackSnapshot();
                        step = 2;
                        return true;

                    case 2:
                        // Step 3: Install kernel into inactive boot slot (A/B layout)
                        string kernelSrc = FindKernelSource(pkg);
                        string inactiveSlot = BootSlotManager.GetInactiveSlot();
                        progress?.Invoke(55, $"Kopiowanie kernela do bezpiecznego slotu {inactiveSlot}...");

                        if (!BootSlotManager.StageKernelToInactiveSlot(kernelSrc, pkg.KernelSha256, pkg.Version, pkg.BuildNumber, progress, out error))
                        {
                            Status = UpdateStatus.Error;
                            return false;
                        }
                        step = 3;
                        return true;

                    case 3:
                        // Step 4: Deploy payload files if present
                        string payloadDir = FindPayloadDirectory(pkg);
                        if (pkg.Files != null && pkg.Files.Count > 0)
                        {
                            if (string.IsNullOrEmpty(payloadDir) || !Directory.Exists(payloadDir))
                            {
                                Status = UpdateStatus.Error;
                                error = "Brak wymaganego katalogu payload; instalacja przerwana.";
                                return false;
                            }
                            progress?.Invoke(70, "Instalowanie tylko plikow zadeklarowanych w manifeście...");
                            string backupPayload = Path.Combine(BackupDir, "payload_snapshot");
                            try
                            {
                                DeployManifestFiles(pkg, payloadDir, backupPayload);
                            }
                            catch (Exception deployError)
                            {
                                // No slot activation occurred yet. Undo all
                                // payload writes before reporting failure.
                                try
                                {
                                    RestoreManifestFiles(backupPayload);
                                    error = "Instalacja payload przerwana i wycofana: " + deployError.Message;
                                }
                                catch (Exception restoreError)
                                {
                                    error = "KRYTYCZNE: instalacja payload nieudana: " +
                                        deployError.Message + "; rollback tez nieudany: " +
                                        restoreError.Message;
                                }
                                Status = UpdateStatus.Error;
                                return false;
                            }
                        }
                        step = 4;
                        return true;

                    case 4:
                        // Step 5: Activate new boot slot in Limine
                        string newActiveSlot = BootSlotManager.GetInactiveSlot();
                        progress?.Invoke(85, $"Przelaczanie aktywnego slotu rozruchowego na slot {newActiveSlot}...");
                        if (!BootSlotManager.ActivateSlot(newActiveSlot, out error))
                        {
                            string activationError = error;
                            if (!RestoreSharedFilesAfterBootFailure(BootSlotManager.ActiveSlot,
                                    out string restoreError))
                                error = activationError + " KRYTYCZNE: nie udalo sie odtworzyc plikow: " + restoreError;
                            Status = UpdateStatus.Error;
                            return false;
                        }
                        step = 5;
                        return true;

                    case 5:
                        // Step 6: Identity, reports, integrity baseline and themes
                        progress?.Invoke(95, "Aktualizacja konfiguracji tozsamosci i bazy integralnosci...");
                        string newOsRelease = "NAME=\"ZonderqOS\"\n" +
                                              "ID=zonderqos\n" +
                                              "VERSION_ID=\"" + pkg.Version + "\"\n" +
                                              "BUILD_ID=\"" + pkg.BuildNumber + "\"\n" +
                                              "PRETTY_NAME=\"ZonderqOS " + pkg.Version + " (" + pkg.Title + ")\"\n" +
                                              "UPDATE_DATE=\"" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\"\n";
                        try { if (File.Exists(OsReleasePath)) File.Delete(OsReleasePath); } catch { }
                        File.WriteAllText(OsReleasePath, newOsRelease);

                        try { if (File.Exists("/etc/issue")) File.Delete("/etc/issue"); } catch { }
                        File.WriteAllText("/etc/issue", "ZonderqOS " + pkg.Version + " (Build " + pkg.BuildNumber + ")\n");

                        // Archive applied manifest
                        try
                        {
                            string installedDir = "/var/updates/installed";
                            if (!Directory.Exists(installedDir)) Directory.CreateDirectory(installedDir);

                            if (!string.IsNullOrEmpty(pkg.ManifestPath) && File.Exists(pkg.ManifestPath))
                            {
                                // Preserve original manifest and .sig for diagnostics
                                // and retry until the running slot is confirmed healthy.
                                File.Copy(pkg.ManifestPath, Path.Combine(installedDir, "update-" + pkg.Version + ".manifest"), true);
                            }
                        }
                        catch { }

                        IntegrityGuard.UpdateBaseline();

                        try
                        {
                            global::ZonderqOS.SystemSettings.SetAccentTheme(pkg.TargetTheme);
                            global::ZonderqOS.SystemSettings.SetDesktopBackground(pkg.TargetWallpaper);
                        }
                        catch { }

                        WriteUpdateReport(pkg);

                        progress?.Invoke(100, $"Aktualizacja do wersji {pkg.Version} zainstalowana w slocie {BootSlotManager.ActiveSlot}! Wymagany restart.");
                        InstalledVersion = pkg.Version;
                        InstalledBuild = pkg.BuildNumber.ToString();
                        SaveConfig();
                        IsRebootRequired = true;
                        Status = UpdateStatus.NeedsReboot;
                        AvailablePackage = null;

                        string logEntry = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] ZAINSTALOWANO: " +
                                          pkg.Version + " (Build " + pkg.BuildNumber + ") w slocie " + BootSlotManager.ActiveSlot +
                                          " | SHA256: " + (pkg.ExpectedSha256.Length >= 16 ? pkg.ExpectedSha256.Substring(0, 16) : pkg.ExpectedSha256) + "...\n";
                        File.AppendAllText(HistoryLogPath, logEntry);

                        SecurityLogger.LogEvent("SECURITY", $"System zaktualizowany do {pkg.Version} w slocie {BootSlotManager.ActiveSlot}");
                        SystemLogger.Log(SystemLogLevel.Info, "UPDATE", $"Zaktualizowano system do {pkg.Version} w slocie {BootSlotManager.ActiveSlot}");
                        step = -1; // Done!
                        return true;

                    default:
                        step = -1;
                        return true;
                }
            }
            catch (Exception ex)
            {
                Status = UpdateStatus.Error;
                error = "Wyjatek podczas instalacji aktualizacji: " + ex.Message;
                SecurityLogger.LogEvent("ERROR", "Update installation failed: " + ex.Message);
                return false;
            }
        }

        public static bool ApplyUpdate(UpdatePackageInfo pkg, Action<int, string> progress, out string error)
        {
            int step = 0;
            while (step != -1)
            {
                if (!ApplyUpdateStep(pkg, ref step, progress, out error))
                    return false;
            }
            error = string.Empty;
            return true;
        }

        private static void WriteUpdateReport(UpdatePackageInfo pkg)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("============================================================");
                sb.AppendLine("         ZONDERQOS - RAPORT AKTUALIZACJI SYSTEMU");
                sb.AppendLine("============================================================");
                sb.AppendLine("Wersja zainstalowana: " + pkg.Version + " (Build " + pkg.BuildNumber + ")");
                sb.AppendLine("Tytul wydania:        " + pkg.Title);
                sb.AppendLine("Data instalacji:      " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("Aktywny slot boot:    Slot " + BootSlotManager.ActiveSlot);
                sb.AppendLine("Suma SHA-256 kernela: " + pkg.ExpectedSha256);
                sb.AppendLine("Podpis cyfrowy:       " + (pkg.IsSigned ? (pkg.IsSignatureValid ? "ZAUFANY" : "NIEPOPRAWNY") : "BRAK"));
                sb.AppendLine();
                sb.AppendLine("DZIENNIK ZMIAN (CHANGELOG):");
                for (int i = 0; i < pkg.Changelog.Length; i++)
                {
                    sb.AppendLine("  * " + pkg.Changelog[i]);
                }
                sb.AppendLine();
                sb.AppendLine("KOMPONENTY:");
                for (int i = 0; i < pkg.Components.Length; i++)
                {
                    sb.AppendLine("  + " + pkg.Components[i]);
                }
                sb.AppendLine("============================================================");

                File.WriteAllText("/root/Co_Nowego_W_Aktualizacji.txt", sb.ToString());
            }
            catch { }
        }

        // Stage only explicitly declared, allowlisted payload files. Root-wide
        // recursive copying is never valid for an untrusted update archive.
        // Preserve snapshots of pre-existing files before any destination write.
        private static void DeployManifestFiles(UpdatePackageInfo pkg, string payloadDir, string backupRoot)
        {
            if (pkg == null || pkg.Files == null)
                throw new IOException("Brak listy plikow w manifeście.");
            if (!Directory.Exists(payloadDir))
                throw new IOException("Nie istnieje katalog payload.");

            // Preflight the entire manifest before creating files.
            var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < pkg.Files.Count; i++)
            {
                UpdateFileEntry entry = pkg.Files[i];
                if (entry == null || !IsAllowedUpdatePayloadPath(entry.RelativePath) ||
                    !declared.Add(entry.RelativePath) ||
                    !IsSafeUpdateFilePath(payloadDir, entry.RelativePath) ||
                    !IsSafeUpdateFilePath("/", entry.RelativePath))
                    throw new IOException("Niebezpieczny lub powtorzony plik aktualizacji.");
                string source = Path.Combine(payloadDir, entry.RelativePath);
                if (!File.Exists(source))
                    throw new IOException("Brak pliku payload: " + entry.RelativePath);
                byte[] data = File.ReadAllBytes(source);
                if (entry.SizeBytes > 0 && data.LongLength != entry.SizeBytes)
                    throw new IOException("Niepoprawny rozmiar payload: " + entry.RelativePath);
                if (!Crypto.FixedTimeEquals(
                    Crypto.ComputeSha256(data).ToLowerInvariant(),
                    entry.ExpectedSha256.ToLowerInvariant()))
                    throw new IOException("Bledny hash payload: " + entry.RelativePath);
            }

            // A signed list must cover every real file on disk, including files
            // in unexpected subdirectories.
            string[] actualFiles = Directory.GetFiles(payloadDir, "*", SearchOption.AllDirectories);
            for (int i = 0; i < actualFiles.Length; i++)
            {
                string relative = actualFiles[i].Substring(payloadDir.TrimEnd('/').Length).TrimStart('/', '\\');
                relative = relative.Replace('\\', '/');
                if (!declared.Contains(relative) || !IsSafeUpdateFilePath(payloadDir, relative))
                    throw new IOException("Niezadeklarowany plik w payload: " + relative);
            }

            if (!Directory.Exists(backupRoot))
                Directory.CreateDirectory(backupRoot);

            // Keep a rollback record for files absent before this transaction.
            // It is written before any install write; a failed record is fatal.
            var created = new List<string>();
            for (int i = 0; i < pkg.Files.Count; i++)
            {
                string relative = pkg.Files[i].RelativePath;
                string destination = Path.Combine("/", relative);
                string backup = Path.Combine(backupRoot, relative);
                if (!File.Exists(destination))
                    created.Add(relative);
                else
                {
                    string backupParent = Path.GetDirectoryName(backup);
                    if (!Directory.Exists(backupParent)) Directory.CreateDirectory(backupParent);
                    File.Copy(destination, backup, true);
                }
            }

            string createdRecord = Path.Combine(BackupDir, "payload_created.list");
            File.WriteAllText(createdRecord, string.Join("\n", created));

            // Apply one file at a time, verifying the exact bytes copied.
            // Exception stops activation and is returned to the UI.
            for (int i = 0; i < pkg.Files.Count; i++)
            {
                UpdateFileEntry entry = pkg.Files[i];
                string source = Path.Combine(payloadDir, entry.RelativePath);
                string dest = Path.Combine("/", entry.RelativePath);
                string parent = Path.GetDirectoryName(dest);
                if (!IsSafeUpdateFilePath("/", entry.RelativePath))
                    throw new IOException("Zmiana sciezki docelowej podczas instalacji.");
                if (!Directory.Exists(parent)) Directory.CreateDirectory(parent);
                string temp = dest + ".update-part";
                if (File.Exists(temp)) File.Delete(temp);
                File.Copy(source, temp);
                if (!Crypto.FixedTimeEquals(
                    Crypto.ComputeSha256(File.ReadAllBytes(temp)).ToLowerInvariant(),
                    entry.ExpectedSha256.ToLowerInvariant()))
                    throw new IOException("Blad weryfikacji kopii: " + entry.RelativePath);
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(temp, dest);
            }
        }

        private static void RestoreManifestFiles(string backupRoot)
        {
            // Only restore system-managed destinations; do not recursively
            // trust arbitrary backup paths under a writable disk directory.
            if (Directory.Exists(backupRoot))
            {
                string[] files = Directory.GetFiles(backupRoot, "*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    string relative = files[i].Substring(backupRoot.TrimEnd('/').Length).TrimStart('/', '\\');
                    relative = relative.Replace('\\', '/');
                    if (!IsAllowedUpdatePayloadPath(relative) ||
                        !IsSafeUpdateFilePath(backupRoot, relative) ||
                        !IsSafeUpdateFilePath("/", relative))
                        throw new IOException("Niebezpieczny plik w kopii rollback: " + relative);
                    string dst = Path.Combine("/", relative);
                    string parent = Path.GetDirectoryName(dst);
                    if (!Directory.Exists(parent)) Directory.CreateDirectory(parent);
                    File.Copy(files[i], dst, true);
                }
            }

            string createdRecord = Path.Combine(BackupDir, "payload_created.list");
            if (File.Exists(createdRecord))
            {
                string[] created = File.ReadAllLines(createdRecord);
                for (int i = 0; i < created.Length; i++)
                {
                    string relative = created[i].Trim();
                    if (relative.Length == 0) continue;
                    if (!IsAllowedUpdatePayloadPath(relative) ||
                        !IsSafeUpdateFilePath("/", relative))
                        throw new IOException("Niebezpieczna sciezka usuwania w rollback: " + relative);
                    string destination = Path.Combine("/", relative);
                    if (File.Exists(destination)) File.Delete(destination);
                }
            }
        }

        private static void CreateRollbackSnapshot()
        {
            try
            {
                if (!Directory.Exists(BackupDir))
                    Directory.CreateDirectory(BackupDir);
                // A new transaction must not inherit files or a deletion list
                // from an older update, or rollback could restore stale data.
                string previousPayload = Path.Combine(BackupDir, "payload_snapshot");
                if (Directory.Exists(previousPayload))
                    Directory.Delete(previousPayload, true);
                string previousCreated = Path.Combine(BackupDir, "payload_created.list");
                if (File.Exists(previousCreated))
                    File.Delete(previousCreated);

                if (File.Exists(OsReleasePath))
                    File.Copy(OsReleasePath, Path.Combine(BackupDir, "os-release.snapshot"), true);

                if (File.Exists(ConfigPath))
                    File.Copy(ConfigPath, Path.Combine(BackupDir, "update.cfg.snapshot"), true);

                string meta = "TIMESTAMP=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n" +
                              "PREVIOUS_VERSION=" + CurrentVersion + "\n" +
                              "PREVIOUS_BUILD=" + CurrentBuild + "\n" +
                              "PREVIOUS_SLOT=" + BootSlotManager.ActiveSlot + "\n" +
                              "PREVIOUS_OS_RELEASE_PRESENT=" + (File.Exists(OsReleasePath) ? "1" : "0") + "\n" +
                              "PREVIOUS_UPDATE_CONFIG_PRESENT=" + (File.Exists(ConfigPath) ? "1" : "0") + "\n" +
                              "PREVIOUS_THEME=" + global::ZonderqOS.SystemSettings.AccentTheme + "\n" +
                              "PREVIOUS_WALLPAPER=" + global::ZonderqOS.SystemSettings.DesktopBackgroundMode + "\n";
                File.WriteAllText(Path.Combine(BackupDir, "snapshot.meta"), meta);
            }
            catch (Exception ex)
            {
                throw new IOException("Nie udalo sie utworzyc kopii rollback: " + ex.Message);
            }
        }

        /// <summary>
        /// Called during EARLY boot after an EFI BootGuard selects the old
        /// kernel, BEFORE application/service initialization. An A/B kernel
        /// rollback must also restore shared /system and /usr payload data.
        /// Do not update the integrity baseline here: startup does it later.
        /// </summary>
        internal static bool RestoreSharedFilesAfterBootFailure(string fallbackSlot, out string error)
        {
            error = string.Empty;
            try
            {
                string metadataPath = Path.Combine(BackupDir, "snapshot.meta");
                if (!File.Exists(metadataPath))
                {
                    error = "Brak snapshot.meta do odtworzenia wspolnych plikow systemowych.";
                    return false;
                }
                string[] metadata = File.ReadAllLines(metadataPath);
                bool matchesFallback = false;
                bool haveOsPresence = false, haveConfigPresence = false;
                bool osExisted = false, configExisted = false;
                for (int i = 0; i < metadata.Length; i++)
                {
                    if (metadata[i] == "PREVIOUS_SLOT=" + fallbackSlot)
                        matchesFallback = true;
                    if (metadata[i] == "PREVIOUS_OS_RELEASE_PRESENT=1")
                    { haveOsPresence = true; osExisted = true; }
                    else if (metadata[i] == "PREVIOUS_OS_RELEASE_PRESENT=0")
                        haveOsPresence = true;
                    if (metadata[i] == "PREVIOUS_UPDATE_CONFIG_PRESENT=1")
                    { haveConfigPresence = true; configExisted = true; }
                    else if (metadata[i] == "PREVIOUS_UPDATE_CONFIG_PRESENT=0")
                        haveConfigPresence = true;
                }
                if (!haveOsPresence || !haveConfigPresence)
                {
                    error = "Migawka nie zapisala informacji o obecnosci plikow konfiguracyjnych.";
                    return false;
                }
                if (!matchesFallback)
                {
                    error = "Slot awaryjny nie zgadza sie z migawka poprzedniego systemu.";
                    return false;
                }
                RestoreManifestFiles(Path.Combine(BackupDir, "payload_snapshot"));
                string oldRelease = Path.Combine(BackupDir, "os-release.snapshot");
                string oldConfig = Path.Combine(BackupDir, "update.cfg.snapshot");
                if ((osExisted && !File.Exists(oldRelease)) ||
                    (configExisted && !File.Exists(oldConfig)))
                    throw new IOException("Brak wymaganej kopii pierwotnego pliku w snapshot.");
                if (osExisted) File.Copy(oldRelease, OsReleasePath, true);
                else if (File.Exists(OsReleasePath)) File.Delete(OsReleasePath);
                if (configExisted) File.Copy(oldConfig, ConfigPath, true);
                else if (File.Exists(ConfigPath)) File.Delete(ConfigPath);
                return true;
            }
            catch (Exception ex)
            {
                error = "Rollback plikow po nieudanym boot: " + ex.Message;
                return false;
            }
        }

        public static bool HasRollbackSnapshot()
        {
            try
            {
                return File.Exists(Path.Combine(BackupDir, "snapshot.meta"));
            }
            catch
            {
                return false;
            }
        }

        public static bool RollbackStep(ref int step, Action<int, string> progress, out string error)
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
                switch (step)
                {
                    case 0:
                        progress?.Invoke(25, "Odczytywanie danych migawki systemu...");
                        step = 1;
                        return true;

                    case 1:
                        progress?.Invoke(50, "Wycofywanie aktywnego slotu rozruchowego (Rollback A/B)...");
                        if (!BootSlotManager.ManualRollback(out error))
                            return false;
                        step = 2;
                        return true;

                    case 2:
                        progress?.Invoke(75, "Przywracanie poprzedniej konfiguracji /etc/os-release...");
                        if (!RestoreSharedFilesAfterBootFailure(BootSlotManager.ActiveSlot,
                                out error))
                        {
                            Status = UpdateStatus.Error;
                            return false;
                        }
                        IntegrityGuard.UpdateBaseline();
                        step = 3;
                        return true;

                    case 3:
                        progress?.Invoke(100, "Przywracanie zakonczone pomyslnie! Wymagany restart.");
                        string metaFile = Path.Combine(BackupDir, "snapshot.meta");
                        string prevVersion = KernelBuildInfo.KernelVersion;
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

                        try
                        {
                            global::ZonderqOS.SystemSettings.SetAccentTheme(prevTheme);
                            global::ZonderqOS.SystemSettings.SetDesktopBackground(prevWallpaper);
                        }
                        catch { }

                        CurrentVersion = prevVersion;
                        InstalledVersion = prevVersion;
                        SaveConfig();
                        IsRebootRequired = true;
                        Status = UpdateStatus.RolledBack;

                        string logEntry = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] ROLLBACK: Przywrocono wersje " + prevVersion +
                                          " w slocie " + BootSlotManager.ActiveSlot + "\n";
                        File.AppendAllText(HistoryLogPath, logEntry);

                        SecurityLogger.LogEvent("WARN", "System rolled back to previous snapshot: " + prevVersion);
                        step = -1; // Done!
                        return true;

                    default:
                        step = -1;
                        return true;
                }
            }
            catch (Exception ex)
            {
                error = "Blad podczas rollbacku: " + ex.Message;
                return false;
            }
        }

        public static bool Rollback(Action<int, string> progress, out string error)
        {
            int step = 0;
            while (step != -1)
            {
                if (!RollbackStep(ref step, progress, out error))
                    return false;
            }
            error = string.Empty;
            return true;
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
