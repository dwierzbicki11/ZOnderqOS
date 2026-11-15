using System;
using System.Collections.Generic;
using System.IO;

namespace ZonderqOS.Security
{
    public enum IntegrityStatus
    {
        Unknown = 0,
        Match = 1,
        Modified = 2,
        Missing = 3,
        PermissionMismatch = 4,
        NotCreated = 5
    }

    public enum IntegrityInitializationState
    {
        Uninitialized = 0,
        Initialized = 1,
        Failed = 2
    }

    internal enum ManagedOperationKind
    {
        InitialRootPasswordSetup = 1,
        PasswordChange = 2,
        LegacyCredentialUpgrade = 3,
        AccountCreation = 4,
        AclSave = 5
    }

    internal sealed class ManagedFileUpdateLease
    {
        internal ManagedFileUpdateLease(
            ManagedOperationKind operation,
            string[] paths,
            string[] beforeHashes,
            int[] beforePerms)
        {
            Operation = operation;
            Paths = paths;
            BeforeHashes = beforeHashes;
            BeforePerms = beforePerms;
        }

        internal ManagedOperationKind Operation { get; }
        internal string[] Paths { get; }
        internal string[] BeforeHashes { get; }
        internal int[] BeforePerms { get; }
        internal bool Consumed { get; set; }
    }

    public sealed class FileIntegrityRecord
    {
        public string Path { get; set; } = string.Empty;
        public string ExpectedHash { get; set; } = string.Empty;
        public string CurrentHash { get; set; } = string.Empty;
        public int ExpectedPerms { get; set; } = 644;
        public int CurrentPerms { get; set; } = 644;
        public IntegrityStatus Status { get; set; } = IntegrityStatus.Unknown;
        public long FileSize { get; set; } = 0;
    }

    public static class IntegrityGuard
    {
        private const string PasswdPath = "/etc/passwd";
        private const string ShadowPath = "/etc/shadow";
        private const string AclMapPath = "/etc/acl.map";
        private const string HostnamePath = "/etc/hostname";
        private const string FirewallConfigPath = "/etc/firewall.cfg";
        private const string SettingsConfigPath = "/etc/zonderq/settings.conf";
        private const string DatabasePath = "/etc/integrity.db";
        private const string DatabaseTempPath = "/etc/integrity.db.new";
        private const string DatabaseBackupPath = "/etc/integrity.db.bak";
        private const string ProvisionedMarkerPath = "/etc/zonderq/.integrity-sealed";
        private const string LastUserMarkerPath = "/etc/zonderq/last-user";
        private const long MaxDatabaseBytes = 256 * 1024;

        private static readonly string[] MonitoredPaths = new string[]
        {
            PasswdPath,
            ShadowPath,
            AclMapPath,
            HostnamePath,
            FirewallConfigPath,
            SettingsConfigPath
        };

        private static readonly Dictionary<string, FileIntegrityRecord> records =
            new Dictionary<string, FileIntegrityRecord>(StringComparer.OrdinalIgnoreCase);

        private static readonly object _lock = new object();
        private static IntegrityInitializationState initializationState = IntegrityInitializationState.Uninitialized;
        private static string initializationError = string.Empty;

        internal static bool IsInitialized
        {
            get
            {
                lock (_lock)
                {
                    return initializationState == IntegrityInitializationState.Initialized;
                }
            }
        }

        public static IntegrityInitializationState InitializationState
        {
            get
            {
                lock (_lock)
                {
                    return initializationState;
                }
            }
        }

        public static bool HasInitializationError
        {
            get
            {
                lock (_lock)
                {
                    return initializationState == IntegrityInitializationState.Failed;
                }
            }
        }

        public static string InitializationError
        {
            get
            {
                lock (_lock)
                {
                    return initializationError;
                }
            }
        }

        private static string ToDiskPath(string logicalPath)
        {
#if ZIG_HOST_TESTS
            string root = Environment.GetEnvironmentVariable("ZIG_TEST_ROOT");
            if (!string.IsNullOrEmpty(root))
                return Path.Combine(root, logicalPath.TrimStart('/'));
#endif
            return logicalPath;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            string raw = path.Replace('\\', '/').Trim();
            if (raw.Length == 0 || raw[0] != '/')
                return string.Empty;

            string[] segments = raw.Split('/');
            var canonical = new List<string>(segments.Length);
            for (int i = 0; i < segments.Length; i++)
            {
                string seg = segments[i];
                if (string.IsNullOrEmpty(seg) || seg == ".")
                    continue;
                if (seg == "..")
                {
                    if (canonical.Count > 0)
                        canonical.RemoveAt(canonical.Count - 1);
                    continue;
                }
                canonical.Add(seg);
            }

            return canonical.Count == 0 ? "/" : "/" + string.Join("/", canonical);
        }

        private static bool IsOptionalUntilCreated(string path) =>
            string.Equals(path, SettingsConfigPath, StringComparison.OrdinalIgnoreCase);

        private static bool IsMonitoredPath(string path)
        {
            for (int i = 0; i < MonitoredPaths.Length; i++)
            {
                if (string.Equals(MonitoredPaths[i], path, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool IsValidSha256Hex(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64)
                return false;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!isHex)
                    return false;
            }
            return true;
        }

        private static bool IsValidPermissionMode(int perms)
        {
            if (perms <= 0 || perms > 777)
                return false;

            int owner = (perms / 100) % 10;
            int group = (perms / 10) % 10;
            int others = perms % 10;
            return owner <= 7 && group <= 7 && others <= 7;
        }

        private static int DefaultExpectedPerms(string path) =>
            (string.Equals(path, ShadowPath, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(path, AclMapPath, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(path, FirewallConfigPath, StringComparison.OrdinalIgnoreCase))
                ? 600
                : 644;

        private static bool IsAuthenticatedRoot() =>
            SecurityContext.IsAuthenticated &&
            SecurityContext.CurrentUid == 0 &&
            string.Equals(SecurityContext.CurrentUser, "root", StringComparison.Ordinal);

        // Lock-ordering safety: PermissionManager._aclLock may invoke IntegrityGuard._lock,
        // so IntegrityGuard must always read PermissionManager outside _lock.
        private static int ReadActualPerms(string path)
        {
            var acl = PermissionManager.GetPermission(path);
            return acl.Perms == 0 ? 644 : acl.Perms;
        }

        private static Dictionary<string, int> SnapshotMonitoredPerms()
        {
            var map = new Dictionary<string, int>(MonitoredPaths.Length, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < MonitoredPaths.Length; i++)
            {
                string path = MonitoredPaths[i];
                map[path] = ReadActualPerms(path);
            }
            return map;
        }

#if ZIG_HOST_TESTS
        public static string SimulatedWriteFailurePath { get; set; }
        public static bool SimulateDatabaseWriteFailure { get; set; }

        public static void ResetForTests()
        {
            lock (_lock)
            {
                records.Clear();
                initializationState = IntegrityInitializationState.Uninitialized;
                initializationError = string.Empty;
                SimulatedWriteFailurePath = null;
                SimulateDatabaseWriteFailure = false;
            }
        }
#endif

        internal static bool HasSeededProvisioningMarker()
        {
            return File.Exists(ToDiskPath(DatabasePath)) ||
                   File.Exists(ToDiskPath(DatabaseBackupPath)) ||
                   File.Exists(ToDiskPath(DatabaseTempPath)) ||
                   File.Exists(ToDiskPath(ProvisionedMarkerPath)) ||
                   File.Exists(ToDiskPath(SettingsConfigPath)) ||
                   File.Exists(ToDiskPath(LastUserMarkerPath));
        }

        private static bool DetectPreExistingSystemOutsideLock(out string evidence)
        {
            evidence = string.Empty;

            if (File.Exists(ToDiskPath(ProvisionedMarkerPath)))
            {
                evidence = "provisioned integrity marker exists";
                return true;
            }

            if (File.Exists(ToDiskPath(SettingsConfigPath)))
            {
                evidence = "existing settings.conf";
                return true;
            }

            if (File.Exists(ToDiskPath(LastUserMarkerPath)))
            {
                evidence = "existing last-user marker";
                return true;
            }

            if (UserManager.HasExistingInstallationAccountsInternal(out string accountEvidence))
            {
                evidence = accountEvidence;
                return true;
            }

            try
            {
                string homeDisk = ToDiskPath("/home");
                if (Directory.Exists(homeDisk))
                {
                    string[] dirs = Directory.GetDirectories(homeDisk);
                    if (dirs != null && dirs.Length > 0)
                    {
                        evidence = "existing user home directories";
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        internal static bool TryReadCommittedHashForRecovery(string logicalPath, out string expectedHash)
        {
            expectedHash = string.Empty;
            string normalized = NormalizePath(logicalPath);
            if (string.IsNullOrEmpty(normalized))
                return false;

            Dictionary<string, FileIntegrityRecord> loaded;
            string err;
            string dbDisk = ToDiskPath(DatabasePath);
            string bakDisk = ToDiskPath(DatabaseBackupPath);

            if (File.Exists(dbDisk) && TryLoadAndValidateBaseline(dbDisk, out loaded, out err) &&
                loaded.TryGetValue(normalized, out FileIntegrityRecord dbRec) &&
                !string.IsNullOrEmpty(dbRec.ExpectedHash))
            {
                expectedHash = dbRec.ExpectedHash;
                return true;
            }

            if (File.Exists(bakDisk) && TryLoadAndValidateBaseline(bakDisk, out loaded, out err) &&
                loaded.TryGetValue(normalized, out FileIntegrityRecord bakRec) &&
                !string.IsNullOrEmpty(bakRec.ExpectedHash))
            {
                expectedHash = bakRec.ExpectedHash;
                return true;
            }

            return false;
        }

        public static void Initialize()
        {
            Dictionary<string, int> initialPerms = SnapshotMonitoredPerms();
            bool preExistingSystem = DetectPreExistingSystemOutsideLock(out string preExistingEvidence);

            lock (_lock)
            {
                if (initializationState == IntegrityInitializationState.Initialized)
                    return;

                initializationError = string.Empty;

                try
                {
                    string etcDir = ToDiskPath("/etc");
                    if (!Directory.Exists(etcDir))
                        Directory.CreateDirectory(etcDir);

                    string dbDisk = ToDiskPath(DatabasePath);
                    string dbTempDisk = ToDiskPath(DatabaseTempPath);
                    string bakDisk = ToDiskPath(DatabaseBackupPath);

                    bool hadOrphanTempDb = false;
                    // Incomplete temporary baseline writes are never authoritative across boots.
                    if (File.Exists(dbTempDisk))
                    {
                        hadOrphanTempDb = true;
                        File.Delete(dbTempDisk);
                        SecurityLogger.LogEvent("WARN", "Discarded orphaned temporary integrity database (/etc/integrity.db.new).");
                    }

                    bool dbExists = File.Exists(dbDisk);
                    bool bakExists = File.Exists(bakDisk);

                    if (dbExists || bakExists)
                    {
                        Dictionary<string, FileIntegrityRecord> loaded;
                        string dbErr = "primary database missing";
                        if (dbExists && TryLoadAndValidateBaseline(dbDisk, out loaded, out dbErr))
                        {
                            ApplyLoadedRecordsLocked(loaded);
                            if (!bakExists)
                            {
                                try { File.Copy(dbDisk, bakDisk, true); } catch { }
                            }
                        }
                        else
                        {
                            Dictionary<string, FileIntegrityRecord> bakLoaded;
                            string bakErr = "backup database missing";
                            if (bakExists && TryLoadAndValidateBaseline(bakDisk, out bakLoaded, out bakErr))
                            {
                                File.Copy(bakDisk, dbDisk, true);
                                ApplyLoadedRecordsLocked(bakLoaded);
                                SecurityLogger.LogEvent("WARN",
                                    "Recovered integrity baseline from verified /etc/integrity.db.bak (primary error: " + dbErr + ").");
                            }
                            else
                            {
                                MarkFailedLocked("Integrity baseline unusable (" + dbErr + "; backup: " + bakErr + ").");
                                return;
                            }
                        }
                    }
                    else
                    {
                        if (hadOrphanTempDb)
                        {
                            MarkFailedLocked("Missing integrity baseline while orphaned /etc/integrity.db.new existed; refusing unverified baseline creation.");
                            return;
                        }

                        if (preExistingSystem)
                        {
                            MarkFailedLocked("Integrity baseline missing on existing system (" + preExistingEvidence + "); refusing to auto-generate baseline.");
                            return;
                        }

                        if (!CreateDefaultBaselineLocked())
                        {
                            MarkFailedLocked("Failed to persist initial integrity baseline (/etc/integrity.db).");
                            return;
                        }
                    }

                    initializationState = IntegrityInitializationState.Initialized;
                    initializationError = string.Empty;
                    TryWriteProvisionedMarker();
                    ScanAllLocked(initialPerms);
                }
                catch (Exception ex)
                {
                    MarkFailedLocked("IntegrityGuard initialization failed: " + ex.Message);
                }
            }
        }

        private static void MarkFailedLocked(string reason)
        {
            initializationState = IntegrityInitializationState.Failed;
            initializationError = reason ?? "IntegrityGuard initialization failed.";
            PopulateUnknownRecordsLocked();
            SecurityLogger.LogEvent("CRITICAL", initializationError);
        }

        private static void PopulateUnknownRecordsLocked()
        {
            records.Clear();
            for (int i = 0; i < MonitoredPaths.Length; i++)
            {
                string path = MonitoredPaths[i];
                records[path] = new FileIntegrityRecord
                {
                    Path = path,
                    ExpectedHash = string.Empty,
                    CurrentHash = string.Empty,
                    ExpectedPerms = DefaultExpectedPerms(path),
                    CurrentPerms = 0,
                    Status = IntegrityStatus.Unknown,
                    FileSize = 0
                };
            }
        }

        private static void ApplyLoadedRecordsLocked(Dictionary<string, FileIntegrityRecord> loaded)
        {
            records.Clear();
            foreach (var kvp in loaded)
                records[kvp.Key] = kvp.Value;
        }

        private static void TryWriteProvisionedMarker()
        {
            try
            {
                string markerDisk = ToDiskPath(ProvisionedMarkerPath);
                if (File.Exists(markerDisk))
                    return;

                string dir = Path.GetDirectoryName(markerDisk);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(markerDisk, "sealed=1\n");
            }
            catch { }
        }

        private static bool BuildBaselineRecordsFromDiskLocked(out Dictionary<string, FileIntegrityRecord> candidate, out string error)
        {
            candidate = new Dictionary<string, FileIntegrityRecord>(MonitoredPaths.Length, StringComparer.OrdinalIgnoreCase);
            error = string.Empty;

            for (int i = 0; i < MonitoredPaths.Length; i++)
            {
                string path = MonitoredPaths[i];
                string diskPath = ToDiskPath(path);
                int expectedPerms = DefaultExpectedPerms(path);
                string hash = string.Empty;
                long size = 0;
                IntegrityStatus status = IsOptionalUntilCreated(path) ? IntegrityStatus.NotCreated : IntegrityStatus.Missing;

                if (File.Exists(diskPath))
                {
                    try
                    {
                        byte[] data = File.ReadAllBytes(diskPath);
                        hash = Crypto.ComputeSha256(data);
                        size = data.Length;
                        status = IntegrityStatus.Match;
                    }
                    catch (Exception ex)
                    {
                        error = "Cannot hash " + path + ": " + ex.Message;
                        return false;
                    }
                }
                else if (!IsOptionalUntilCreated(path))
                {
                    error = "Required monitored file is missing: " + path;
                    return false;
                }

                candidate[path] = new FileIntegrityRecord
                {
                    Path = path,
                    ExpectedHash = hash,
                    CurrentHash = hash,
                    ExpectedPerms = expectedPerms,
                    CurrentPerms = expectedPerms,
                    FileSize = size,
                    Status = status
                };
            }

            return true;
        }

        private static bool CreateDefaultBaselineLocked()
        {
            Dictionary<string, FileIntegrityRecord> candidate;
            string error;
            if (!BuildBaselineRecordsFromDiskLocked(out candidate, out error))
            {
                SecurityLogger.LogEvent("ERROR", "Initial baseline build failed: " + error);
                records.Clear();
                return false;
            }

            ApplyLoadedRecordsLocked(candidate);
            if (!SaveBaselineLocked())
            {
                records.Clear();
                return false;
            }

            SecurityLogger.LogEvent("INFO", "Host integrity baseline created with " + records.Count + " entries.");
            return true;
        }

        private static bool TryLoadAndValidateBaseline(
            string dbFileDisk,
            out Dictionary<string, FileIntegrityRecord> loaded,
            out string error)
        {
            loaded = null;
            error = string.Empty;

            try
            {
                if (!File.Exists(dbFileDisk))
                {
                    error = "file missing";
                    return false;
                }

                FileInfo info = new FileInfo(dbFileDisk);
                if (info.Length <= 0 || info.Length > MaxDatabaseBytes)
                {
                    error = info.Length <= 0 ? "file is empty" : "file exceeds maximum size";
                    return false;
                }

                string[] lines = File.ReadAllLines(dbFileDisk);
                var parsed = new Dictionary<string, FileIntegrityRecord>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;

                    string[] parts = line.Split('|');
                    if (parts.Length != 3)
                    {
                        error = "malformed record at line " + (i + 1);
                        return false;
                    }

                    string path = NormalizePath(parts[0]);
                    if (string.IsNullOrEmpty(path) || !IsMonitoredPath(path))
                    {
                        error = "invalid or unsupported monitored path at line " + (i + 1);
                        return false;
                    }

                    if (parsed.ContainsKey(path))
                    {
                        error = "duplicate entry for " + path;
                        return false;
                    }

                    string expectedHash = parts[1].Trim();
                    if (IsOptionalUntilCreated(path))
                    {
                        if (!string.IsNullOrEmpty(expectedHash) && !IsValidSha256Hex(expectedHash))
                        {
                            error = "invalid hash for " + path;
                            return false;
                        }
                    }
                    else if (!IsValidSha256Hex(expectedHash))
                    {
                        error = "invalid or missing SHA-256 hash for " + path;
                        return false;
                    }

                    int parsedPerms;
                    if (!int.TryParse(parts[2].Trim(), out parsedPerms) || !IsValidPermissionMode(parsedPerms))
                    {
                        error = "invalid mode for " + path;
                        return false;
                    }

                    parsed[path] = new FileIntegrityRecord
                    {
                        Path = path,
                        ExpectedHash = expectedHash,
                        ExpectedPerms = parsedPerms,
                        Status = IntegrityStatus.Unknown
                    };
                }

                for (int i = 0; i < MonitoredPaths.Length; i++)
                {
                    if (!parsed.ContainsKey(MonitoredPaths[i]))
                    {
                        error = "missing required baseline entry for " + MonitoredPaths[i];
                        return false;
                    }
                }

                loaded = parsed;
                return true;
            }
            catch (Exception ex)
            {
                error = "unreadable database: " + ex.Message;
                return false;
            }
        }

        private static bool SaveBaselineLocked()
        {
            string dbDisk = ToDiskPath(DatabasePath);
            string dbTempDisk = ToDiskPath(DatabaseTempPath);
            string bakDisk = ToDiskPath(DatabaseBackupPath);

            try
            {
#if ZIG_HOST_TESTS
                if (SimulateDatabaseWriteFailure ||
                    string.Equals(SimulatedWriteFailurePath, DatabasePath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(SimulatedWriteFailurePath, DatabaseTempPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("Simulated integrity database write failure.");
                }
#endif
                if (File.Exists(dbTempDisk))
                    File.Delete(dbTempDisk);

                using (var sw = new StreamWriter(dbTempDisk, false))
                {
                    sw.WriteLine("# ZOnderqOS File Integrity Baseline Database");
                    sw.WriteLine("# Format: Path|ExpectedSha256|ExpectedChmod");
                    foreach (var kvp in records)
                    {
                        sw.WriteLine(kvp.Key + "|" + kvp.Value.ExpectedHash + "|" + kvp.Value.ExpectedPerms);
                    }
                }

                if (File.Exists(bakDisk))
                    File.Delete(bakDisk);

                if (File.Exists(dbDisk))
                    File.Move(dbDisk, bakDisk);

                try
                {
#if ZIG_HOST_TESTS
                    if (string.Equals(SimulatedWriteFailurePath, DatabasePath + ".swap", StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Simulated integrity database swap failure.");
#endif
                    File.Move(dbTempDisk, dbDisk);
                }
                catch
                {
                    if (!File.Exists(dbDisk) && File.Exists(bakDisk))
                        File.Move(bakDisk, dbDisk);
                    throw;
                }

                // Retain a synchronized backup copy for power-loss recovery across boots.
                // Do not abort a committed primary DB swap if mirroring to .bak encounters an error.
                try
                {
                    File.Copy(dbDisk, bakDisk, true);
                }
                catch (Exception bakEx)
                {
                    SecurityLogger.LogEvent("WARN", "Failed to mirror integrity backup: " + bakEx.Message);
                }

                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    if (File.Exists(dbTempDisk))
                        File.Delete(dbTempDisk);
                }
                catch { }

                SecurityLogger.LogEvent("ERROR", "Failed to save integrity baseline: " + ex.Message);
                return false;
            }
        }

        public static void ScanAll()
        {
            Dictionary<string, int> permsSnapshot = SnapshotMonitoredPerms();
            lock (_lock)
            {
                ScanAllLocked(permsSnapshot);
            }
        }

        private static void ScanAllLocked(Dictionary<string, int> permsSnapshot)
        {
            if (initializationState != IntegrityInitializationState.Initialized)
            {
                if (records.Count == 0)
                    PopulateUnknownRecordsLocked();
                SecurityLogger.LogEvent("CRITICAL",
                    "Integrity scan cannot verify files: " +
                    (string.IsNullOrEmpty(initializationError) ? "IntegrityGuard is not initialized." : initializationError));
                return;
            }

            int violations = 0;

            foreach (var kvp in records)
            {
                FileIntegrityRecord rec = kvp.Value;
                string path = rec.Path;
                string diskPath = ToDiskPath(path);

                if (!File.Exists(diskPath))
                {
                    rec.CurrentHash = string.Empty;
                    rec.FileSize = 0;
                    if (IsOptionalUntilCreated(path) && string.IsNullOrEmpty(rec.ExpectedHash))
                    {
                        rec.Status = IntegrityStatus.NotCreated;
                    }
                    else
                    {
                        rec.Status = IntegrityStatus.Missing;
                        violations++;
                    }
                    continue;
                }

                try
                {
                    byte[] data = File.ReadAllBytes(diskPath);
                    rec.FileSize = data.Length;
                    rec.CurrentHash = Crypto.ComputeSha256(data);

                    int actualPerms;
                    if (!permsSnapshot.TryGetValue(path, out actualPerms) || actualPerms == 0)
                        actualPerms = 644;
                    rec.CurrentPerms = actualPerms;

                    if (rec.ExpectedPerms != 0 && actualPerms != rec.ExpectedPerms)
                    {
                        rec.Status = IntegrityStatus.PermissionMismatch;
                        violations++;
                        SecurityLogger.LogEvent("WARN", "Permission mismatch on " + path + ": expected " + rec.ExpectedPerms + ", found " + actualPerms);
                    }
                    else if (IsOptionalUntilCreated(path) && string.IsNullOrEmpty(rec.ExpectedHash))
                    {
                        rec.Status = IntegrityStatus.Modified;
                        violations++;
                        SecurityLogger.LogEvent("CRITICAL", "Host integrity alert: " + path + " appeared outside managed settings write.");
                    }
                    else if (string.IsNullOrEmpty(rec.ExpectedHash))
                    {
                        rec.Status = IntegrityStatus.Unknown;
                        violations++;
                        SecurityLogger.LogEvent("CRITICAL", "Host integrity alert: missing baseline hash for required file " + path + ".");
                    }
                    else if (!Crypto.FixedTimeEquals(rec.ExpectedHash, rec.CurrentHash))
                    {
                        rec.Status = IntegrityStatus.Modified;
                        violations++;
                        SecurityLogger.LogEvent("CRITICAL", "Host integrity alert: " + path + " has been modified! Hash mismatch.");
                    }
                    else
                    {
                        rec.Status = IntegrityStatus.Match;
                    }
                }
                catch (Exception ex)
                {
                    rec.Status = IntegrityStatus.Unknown;
                    violations++;
                    SecurityLogger.LogEvent("WARN", "Integrity check failed for " + path + ": " + ex.Message);
                }
            }

            if (violations > 0)
            {
                SecurityLogger.LogEvent("WARN", "Integrity scan detected " + violations + " file status violation(s).");
            }
        }

        private static bool IsPathAllowedForOperation(ManagedOperationKind operation, string path)
        {
            switch (operation)
            {
                case ManagedOperationKind.InitialRootPasswordSetup:
                case ManagedOperationKind.PasswordChange:
                case ManagedOperationKind.LegacyCredentialUpgrade:
                    return string.Equals(path, ShadowPath, StringComparison.OrdinalIgnoreCase);

                case ManagedOperationKind.AccountCreation:
                    return string.Equals(path, PasswdPath, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(path, ShadowPath, StringComparison.OrdinalIgnoreCase);

                case ManagedOperationKind.AclSave:
                    return string.Equals(path, AclMapPath, StringComparison.OrdinalIgnoreCase);

                default:
                    return false;
            }
        }

        private static bool IsOperationAuthorized(ManagedOperationKind operation)
        {
            switch (operation)
            {
                case ManagedOperationKind.AccountCreation:
                    return IsAuthenticatedRoot() && UserManager.IsAccountCreationInProgressOnThread;

                case ManagedOperationKind.PasswordChange:
                    return SecurityContext.IsAuthenticated &&
                           SecurityContext.CurrentUid >= 0 &&
                           UserManager.IsPasswordChangeInProgressOnThread;

                case ManagedOperationKind.InitialRootPasswordSetup:
                    return !SecurityContext.IsAuthenticated && UserManager.IsInitialRootSetupInProgressOnThread;

                case ManagedOperationKind.LegacyCredentialUpgrade:
                    return UserManager.IsLegacyUpgradeInProgressOnThread;

                case ManagedOperationKind.AclSave:
                    return PermissionManager.IsSavingAclOnThread &&
                           (SecurityContext.IsAuthenticated ||
                            SecurityContext.IsBootInitializationActive ||
                            PermissionManager.IsSavingKernelTelemetryAclOnThread);

                default:
                    return false;
            }
        }

        internal static bool TryBeginManagedFilesUpdate(
            ManagedOperationKind operation,
            string[] logicalPaths,
            out ManagedFileUpdateLease lease)
        {
            lease = null;
            if (logicalPaths == null || logicalPaths.Length == 0)
                return false;

            if (!IsOperationAuthorized(operation))
            {
                SecurityLogger.LogEvent("CRIT",
                    "Unauthorized managed integrity transaction blocked (" + operation +
                    ") for user '" + (SecurityContext.CurrentUser ?? "unknown") + "'.");
                return false;
            }

            string[] normalizedPaths = new string[logicalPaths.Length];
            int[] actualPerms = new int[logicalPaths.Length];
            for (int i = 0; i < logicalPaths.Length; i++)
            {
                normalizedPaths[i] = NormalizePath(logicalPaths[i]);
                if (string.IsNullOrEmpty(normalizedPaths[i]) ||
                    !IsPathAllowedForOperation(operation, normalizedPaths[i]))
                {
                    SecurityLogger.LogEvent("CRIT",
                        "Least-privilege policy blocked operation " + operation +
                        " from modifying path '" + (logicalPaths[i] ?? string.Empty) + "'.");
                    return false;
                }

                for (int j = 0; j < i; j++)
                {
                    if (string.Equals(normalizedPaths[i], normalizedPaths[j], StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                actualPerms[i] = ReadActualPerms(normalizedPaths[i]);
            }

            lock (_lock)
            {
                if (initializationState == IntegrityInitializationState.Failed)
                {
                    SecurityLogger.LogEvent("CRITICAL",
                        "Blocked managed update (" + operation + "): IntegrityGuard is in failed state.");
                    return false;
                }

                if (initializationState != IntegrityInitializationState.Initialized)
                {
                    if (SecurityContext.IsBootInitializationActive && !SecurityContext.IsAuthenticated)
                        return true;

                    SecurityLogger.LogEvent("CRITICAL",
                        "Blocked managed update (" + operation + "): IntegrityGuard is not initialized.");
                    return false;
                }

                string[] beforeHashes = new string[normalizedPaths.Length];
                for (int i = 0; i < normalizedPaths.Length; i++)
                {
                    string path = normalizedPaths[i];
                    FileIntegrityRecord rec;
                    if (!records.TryGetValue(path, out rec))
                        return false;

                    string diskPath = ToDiskPath(path);
                    if (!File.Exists(diskPath))
                    {
                        rec.CurrentHash = string.Empty;
                        rec.FileSize = 0;
                        rec.Status = IntegrityStatus.Missing;
                        SecurityLogger.LogEvent("CRITICAL",
                            "Refused managed update for " + path + ": protected file is missing before update.");
                        return false;
                    }

                    if (rec.ExpectedPerms != 0 && actualPerms[i] != rec.ExpectedPerms)
                    {
                        rec.CurrentPerms = actualPerms[i];
                        rec.Status = IntegrityStatus.PermissionMismatch;
                        SecurityLogger.LogEvent("CRITICAL",
                            "Refused managed update for " + path + ": permission mismatch before update.");
                        return false;
                    }

                    byte[] data = File.ReadAllBytes(diskPath);
                    string diskHash = Crypto.ComputeSha256(data);
                    rec.CurrentHash = diskHash;
                    rec.FileSize = data.Length;
                    rec.CurrentPerms = actualPerms[i];

                    if (string.IsNullOrEmpty(rec.ExpectedHash))
                    {
                        rec.Status = IntegrityStatus.Unknown;
                        SecurityLogger.LogEvent("CRITICAL",
                            "Refused managed update for " + path + ": baseline hash is empty.");
                        return false;
                    }

                    if (!Crypto.FixedTimeEquals(rec.ExpectedHash, diskHash))
                    {
                        rec.Status = IntegrityStatus.Modified;
                        SecurityLogger.LogEvent("CRITICAL",
                            "Refused managed update for " + path + ": pre-existing tampering detected.");
                        return false;
                    }

                    beforeHashes[i] = rec.ExpectedHash;
                }

                lease = new ManagedFileUpdateLease(operation, normalizedPaths, beforeHashes, actualPerms);
                return true;
            }
        }

        internal static void CancelManagedFilesUpdate(ManagedFileUpdateLease lease)
        {
            if (lease == null)
                return;

            lock (_lock)
            {
                lease.Consumed = true;
            }
        }

        internal static bool CommitManagedFilesUpdate(ManagedFileUpdateLease lease)
        {
            if (lease == null)
            {
                lock (_lock)
                {
                    return initializationState == IntegrityInitializationState.Uninitialized &&
                           SecurityContext.IsBootInitializationActive &&
                           !SecurityContext.IsAuthenticated;
                }
            }

            if (!IsOperationAuthorized(lease.Operation))
            {
                SecurityLogger.LogEvent("CRIT",
                    "Unauthorized managed integrity commit blocked (" + lease.Operation + ").");
                return false;
            }

            int[] newPerms = new int[lease.Paths.Length];
            for (int i = 0; i < lease.Paths.Length; i++)
                newPerms[i] = ReadActualPerms(lease.Paths[i]);

            lock (_lock)
            {
                if (initializationState != IntegrityInitializationState.Initialized)
                    return false;

                if (lease.Consumed)
                    return false;

                var backups = new FileIntegrityRecord[lease.Paths.Length];
                var stagedHashes = new string[lease.Paths.Length];
                var stagedSizes = new long[lease.Paths.Length];

                try
                {
                    for (int i = 0; i < lease.Paths.Length; i++)
                    {
                        string path = lease.Paths[i];
                        FileIntegrityRecord rec;
                        if (!records.TryGetValue(path, out rec))
                            return false;

                        if (!Crypto.FixedTimeEquals(rec.ExpectedHash, lease.BeforeHashes[i]))
                        {
                            SecurityLogger.LogEvent("CRITICAL",
                                "Integrity commit rejected for " + path + ": baseline changed concurrently.");
                            return false;
                        }

                        string diskPath = ToDiskPath(path);
                        if (!File.Exists(diskPath))
                            return false;

                        byte[] data = File.ReadAllBytes(diskPath);
                        stagedSizes[i] = data.Length;
                        stagedHashes[i] = Crypto.ComputeSha256(data);

                        backups[i] = new FileIntegrityRecord
                        {
                            Path = rec.Path,
                            ExpectedHash = rec.ExpectedHash,
                            CurrentHash = rec.CurrentHash,
                            ExpectedPerms = rec.ExpectedPerms,
                            CurrentPerms = rec.CurrentPerms,
                            Status = rec.Status,
                            FileSize = rec.FileSize
                        };
                    }

                    for (int i = 0; i < lease.Paths.Length; i++)
                    {
                        FileIntegrityRecord rec = records[lease.Paths[i]];
                        rec.ExpectedHash = stagedHashes[i];
                        rec.CurrentHash = stagedHashes[i];
                        rec.FileSize = stagedSizes[i];
                        if (newPerms[i] != 0)
                            rec.ExpectedPerms = newPerms[i];
                        rec.CurrentPerms = rec.ExpectedPerms;
                        rec.Status = IntegrityStatus.Match;
                    }

                    if (!SaveBaselineLocked())
                    {
                        RestoreRecordSnapshotsLocked(backups);
                        return false;
                    }

                    lease.Consumed = true;
                    return true;
                }
                catch (Exception ex)
                {
                    RestoreRecordSnapshotsLocked(backups);
                    SecurityLogger.LogEvent("ERROR", "Failed to commit managed integrity update: " + ex.Message);
                    return false;
                }
                finally
                {
                    lease.Consumed = true;
                }
            }
        }

        internal static bool NotifyManagedFileSaved(string path)
        {
            string normalized = NormalizePath(path);
            if (string.IsNullOrEmpty(normalized))
                return false;

            if (!string.Equals(normalized, SettingsConfigPath, StringComparison.OrdinalIgnoreCase))
            {
                SecurityLogger.LogEvent("CRIT",
                    "Blocked direct NotifyManagedFileSaved for protected path '" + normalized +
                    "' by user '" + (SecurityContext.CurrentUser ?? "unknown") + "'.");
                return false;
            }

            if (!IsAuthenticatedRoot())
            {
                SecurityLogger.LogEvent("CRIT",
                    "Blocked unauthorized NotifyManagedFileSaved for " + normalized +
                    " by user '" + (SecurityContext.CurrentUser ?? "unknown") + "'.");
                return false;
            }

            int actualPerms = ReadActualPerms(normalized);
            if (actualPerms != DefaultExpectedPerms(SettingsConfigPath))
            {
                SecurityLogger.LogEvent("CRITICAL",
                    "Refused NotifyManagedFileSaved for " + normalized + ": invalid ACL mode " + actualPerms + ".");
                return false;
            }

            lock (_lock)
            {
                if (initializationState != IntegrityInitializationState.Initialized)
                {
                    SecurityLogger.LogEvent("CRITICAL",
                        "Refused NotifyManagedFileSaved for " + normalized + ": IntegrityGuard is not initialized.");
                    return false;
                }

                FileIntegrityRecord rec;
                if (!records.TryGetValue(normalized, out rec))
                    return false;

                string diskPath = ToDiskPath(normalized);
                if (!File.Exists(diskPath))
                    return false;

                var backup = new FileIntegrityRecord
                {
                    Path = rec.Path,
                    ExpectedHash = rec.ExpectedHash,
                    CurrentHash = rec.CurrentHash,
                    ExpectedPerms = rec.ExpectedPerms,
                    CurrentPerms = rec.CurrentPerms,
                    Status = rec.Status,
                    FileSize = rec.FileSize
                };

                try
                {
                    byte[] data = File.ReadAllBytes(diskPath);
                    rec.ExpectedHash = Crypto.ComputeSha256(data);
                    rec.CurrentHash = rec.ExpectedHash;
                    rec.FileSize = data.Length;
                    rec.ExpectedPerms = DefaultExpectedPerms(SettingsConfigPath);
                    rec.CurrentPerms = rec.ExpectedPerms;
                    rec.Status = IntegrityStatus.Match;

                    if (!SaveBaselineLocked())
                    {
                        RestoreRecordSnapshotsLocked(new[] { backup });
                        return false;
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    RestoreRecordSnapshotsLocked(new[] { backup });
                    SecurityLogger.LogEvent("WARN", "Failed to update integrity entry for " + normalized + ": " + ex.Message);
                    return false;
                }
            }
        }

        public static bool UpdateBaseline()
        {
            return RebuildAndSaveBaseline(allowRecoveryFromFailedState: false, out _);
        }

        public static bool RecoverBaseline(out string error)
        {
            return RebuildAndSaveBaseline(allowRecoveryFromFailedState: true, out error);
        }

        private static bool RebuildAndSaveBaseline(bool allowRecoveryFromFailedState, out string error)
        {
            error = string.Empty;
            if (!IsAuthenticatedRoot())
            {
                error = "Root privileges are required to update integrity baseline.";
                SecurityLogger.LogEvent("CRIT",
                    "Unauthorized integrity baseline update blocked for user '" +
                    (SecurityContext.CurrentUser ?? "unknown") + "'.");
                return false;
            }

            Dictionary<string, int> permsSnapshot = SnapshotMonitoredPerms();
            lock (_lock)
            {
                if (initializationState != IntegrityInitializationState.Initialized &&
                    !(allowRecoveryFromFailedState && initializationState == IntegrityInitializationState.Failed))
                {
                    error = initializationState == IntegrityInitializationState.Failed
                        ? "IntegrityGuard is in failed state; explicit admin recovery is required."
                        : "IntegrityGuard is not initialized.";
                    return false;
                }

                var backups = new List<FileIntegrityRecord>(records.Count);
                foreach (var kvp in records)
                {
                    FileIntegrityRecord r = kvp.Value;
                    backups.Add(new FileIntegrityRecord
                    {
                        Path = r.Path,
                        ExpectedHash = r.ExpectedHash,
                        CurrentHash = r.CurrentHash,
                        ExpectedPerms = r.ExpectedPerms,
                        CurrentPerms = r.CurrentPerms,
                        Status = r.Status,
                        FileSize = r.FileSize
                    });
                }

                try
                {
                    for (int i = 0; i < MonitoredPaths.Length; i++)
                    {
                        string p = MonitoredPaths[i];
                        FileIntegrityRecord rec;
                        if (!records.TryGetValue(p, out rec))
                        {
                            rec = new FileIntegrityRecord
                            {
                                Path = p,
                                ExpectedPerms = DefaultExpectedPerms(p)
                            };
                            records[p] = rec;
                        }

                        string diskPath = ToDiskPath(p);
                        if (File.Exists(diskPath))
                        {
                            byte[] data = File.ReadAllBytes(diskPath);
                            rec.ExpectedHash = Crypto.ComputeSha256(data);
                            rec.CurrentHash = rec.ExpectedHash;
                            rec.FileSize = data.Length;
                            int perms;
                            if (permsSnapshot.TryGetValue(p, out perms) && perms != 0)
                                rec.ExpectedPerms = perms;
                            else
                                rec.ExpectedPerms = DefaultExpectedPerms(p);
                            rec.CurrentPerms = rec.ExpectedPerms;
                            rec.Status = IntegrityStatus.Match;
                        }
                        else if (!IsOptionalUntilCreated(p) && allowRecoveryFromFailedState)
                        {
                            RestoreRecordSnapshotsLocked(backups.ToArray());
                            error = "Cannot recover baseline: required file is missing (" + p + ").";
                            return false;
                        }
                        else
                        {
                            rec.ExpectedHash = string.Empty;
                            rec.CurrentHash = string.Empty;
                            rec.FileSize = 0;
                            rec.Status = IsOptionalUntilCreated(p) ? IntegrityStatus.NotCreated : IntegrityStatus.Missing;
                        }
                    }

                    if (!SaveBaselineLocked())
                    {
                        RestoreRecordSnapshotsLocked(backups.ToArray());
                        error = "Failed to save updated integrity baseline database.";
                        return false;
                    }

                    initializationState = IntegrityInitializationState.Initialized;
                    initializationError = string.Empty;
                    TryWriteProvisionedMarker();

                    SecurityLogger.LogEvent(
                        allowRecoveryFromFailedState ? "CRITICAL" : "INFO",
                        (allowRecoveryFromFailedState ? "Integrity baseline recovered by root user " : "Integrity baseline re-calculated by user ") +
                        SecurityContext.CurrentUser);
                    return true;
                }
                catch (Exception ex)
                {
                    RestoreRecordSnapshotsLocked(backups.ToArray());
                    error = "Integrity baseline update failed: " + ex.Message;
                    SecurityLogger.LogEvent("ERROR", error);
                    return false;
                }
            }
        }

        private static void RestoreRecordSnapshotsLocked(FileIntegrityRecord[] backups)
        {
            if (backups == null)
                return;

            for (int i = 0; i < backups.Length; i++)
            {
                FileIntegrityRecord b = backups[i];
                if (b == null)
                    continue;

                FileIntegrityRecord target;
                if (records.TryGetValue(b.Path, out target))
                {
                    target.ExpectedHash = b.ExpectedHash;
                    target.CurrentHash = b.CurrentHash;
                    target.ExpectedPerms = b.ExpectedPerms;
                    target.CurrentPerms = b.CurrentPerms;
                    target.Status = b.Status;
                    target.FileSize = b.FileSize;
                }
            }
        }

        public static List<FileIntegrityRecord> GetRecords()
        {
            lock (_lock)
            {
                var list = new List<FileIntegrityRecord>(records.Count);
                foreach (var r in records.Values)
                {
                    list.Add(new FileIntegrityRecord
                    {
                        Path = r.Path,
                        ExpectedHash = r.ExpectedHash,
                        CurrentHash = r.CurrentHash,
                        ExpectedPerms = r.ExpectedPerms,
                        CurrentPerms = r.CurrentPerms,
                        Status = r.Status,
                        FileSize = r.FileSize
                    });
                }
                return list;
            }
        }

        public static void GetReportSummary(out int total, out int passed, out int modified, out int missing, out int permIssues)
        {
            lock (_lock)
            {
                total = records.Count;
                passed = 0;
                modified = 0;
                missing = 0;
                permIssues = 0;

                foreach (var r in records.Values)
                {
                    switch (r.Status)
                    {
                        case IntegrityStatus.Match: passed++; break;
                        case IntegrityStatus.Modified: modified++; break;
                        case IntegrityStatus.Missing: missing++; break;
                        case IntegrityStatus.PermissionMismatch: permIssues++; break;
                        case IntegrityStatus.Unknown: modified++; break;
                    }
                }
            }
        }

        public static bool VerifyPackagePayload(byte[] packageBytes, string expectedSha256, out string actualSha256)
        {
            actualSha256 = string.Empty;
            if (packageBytes == null || packageBytes.Length == 0)
                return false;

            actualSha256 = Crypto.ComputeSha256(packageBytes);
            if (string.IsNullOrEmpty(expectedSha256))
                return true;

            return Crypto.FixedTimeEquals(actualSha256, expectedSha256);
        }
    }
}
