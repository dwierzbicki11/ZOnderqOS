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
        private static bool isInitialized = false;

        internal static bool IsInitialized
        {
            get
            {
                lock (_lock)
                {
                    return isInitialized;
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
            string normalized = path.Replace('\\', '/').Trim();
            while (normalized.Length > 1 && normalized.EndsWith("/", StringComparison.Ordinal))
                normalized = normalized.Substring(0, normalized.Length - 1);
            return normalized;
        }

        private static bool IsOptionalUntilCreated(string path) =>
            string.Equals(path, SettingsConfigPath, StringComparison.OrdinalIgnoreCase);

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
                isInitialized = false;
                SimulatedWriteFailurePath = null;
                SimulateDatabaseWriteFailure = false;
            }
        }
#endif

        public static void Initialize()
        {
            Dictionary<string, int> initialPerms = SnapshotMonitoredPerms();
            lock (_lock)
            {
                if (isInitialized)
                    return;

                try
                {
                    string etcDir = ToDiskPath("/etc");
                    if (!Directory.Exists(etcDir))
                        Directory.CreateDirectory(etcDir);

                    string dbDisk = ToDiskPath(DatabasePath);
                    string dbTempDisk = ToDiskPath(DatabaseTempPath);
                    string bakDisk = ToDiskPath(DatabaseBackupPath);

                    // Incomplete temporary baseline writes are never authoritative across boots.
                    if (File.Exists(dbTempDisk))
                        File.Delete(dbTempDisk);

                    if ((!File.Exists(dbDisk) || new FileInfo(dbDisk).Length == 0) &&
                        File.Exists(bakDisk) && new FileInfo(bakDisk).Length > 0)
                    {
                        File.Copy(bakDisk, dbDisk, true);
                    }

                    if (!File.Exists(dbDisk))
                    {
                        CreateDefaultBaselineLocked();
                    }
                    else
                    {
                        LoadBaselineLocked();
                    }

                    isInitialized = true;
                    ScanAllLocked(initialPerms);
                }
                catch (Exception ex)
                {
                    SecurityLogger.LogEvent("WARN", "IntegrityGuard init warning: " + ex.Message);
                }
            }
        }

        private static void CreateDefaultBaselineLocked()
        {
            records.Clear();
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
                    catch
                    {
                        status = IntegrityStatus.Unknown;
                    }
                }

                records[path] = new FileIntegrityRecord
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

            if (SaveBaselineLocked())
                SecurityLogger.LogEvent("INFO", "Host integrity baseline created with " + records.Count + " entries.");
        }

        private static void LoadBaselineLocked()
        {
            records.Clear();
            string dbDisk = ToDiskPath(DatabasePath);
            if (!File.Exists(dbDisk))
                return;

            string[] lines = File.ReadAllLines(dbDisk);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                string[] parts = line.Split('|');
                if (parts.Length >= 3)
                {
                    string path = NormalizePath(parts[0]);
                    string expectedHash = parts[1].Trim();
                    int perms = DefaultExpectedPerms(path);
                    int parsedPerms;
                    if (int.TryParse(parts[2].Trim(), out parsedPerms) && parsedPerms > 0)
                        perms = parsedPerms;

                    records[path] = new FileIntegrityRecord
                    {
                        Path = path,
                        ExpectedHash = expectedHash,
                        ExpectedPerms = perms,
                        Status = IntegrityStatus.Unknown
                    };
                }
            }

            for (int i = 0; i < MonitoredPaths.Length; i++)
            {
                string path = MonitoredPaths[i];
                if (!records.ContainsKey(path))
                {
                    records[path] = new FileIntegrityRecord
                    {
                        Path = path,
                        ExpectedHash = string.Empty,
                        ExpectedPerms = DefaultExpectedPerms(path),
                        Status = IntegrityStatus.Unknown
                    };
                }
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
                    File.Move(dbTempDisk, dbDisk);
                }
                catch
                {
                    if (!File.Exists(dbDisk) && File.Exists(bakDisk))
                        File.Move(bakDisk, dbDisk);
                    throw;
                }

                // Retain a synchronized backup copy for power-loss recovery across boots.
                File.Copy(dbDisk, bakDisk, true);
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
                    return IsAuthenticatedRoot();

                case ManagedOperationKind.PasswordChange:
                    return SecurityContext.IsAuthenticated;

                case ManagedOperationKind.InitialRootPasswordSetup:
                case ManagedOperationKind.LegacyCredentialUpgrade:
                case ManagedOperationKind.AclSave:
                    return true;

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
                if (!isInitialized)
                    return true;

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
                return !IsInitialized;

            int[] newPerms = new int[lease.Paths.Length];
            for (int i = 0; i < lease.Paths.Length; i++)
                newPerms[i] = ReadActualPerms(lease.Paths[i]);

            lock (_lock)
            {
                if (!isInitialized)
                    return false;

                if (lease.Consumed)
                    return false;

                if (!IsOperationAuthorized(lease.Operation))
                {
                    lease.Consumed = true;
                    SecurityLogger.LogEvent("CRIT",
                        "Unauthorized managed integrity commit blocked (" + lease.Operation + ").");
                    return false;
                }

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

            if (!IsInitialized)
                return true;

            if (!SecurityContext.IsAuthenticated)
            {
                SecurityLogger.LogEvent("CRIT",
                    "Blocked unauthenticated NotifyManagedFileSaved for " + normalized + ".");
                return false;
            }

            int actualPerms = ReadActualPerms(normalized);

            lock (_lock)
            {
                if (!isInitialized)
                    return true;

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
                    if (actualPerms != 0)
                        rec.ExpectedPerms = actualPerms;
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
            if (!IsAuthenticatedRoot())
            {
                SecurityLogger.LogEvent("CRIT",
                    "Unauthorized integrity baseline update blocked for user '" +
                    (SecurityContext.CurrentUser ?? "unknown") + "'.");
                return false;
            }

            Dictionary<string, int> permsSnapshot = SnapshotMonitoredPerms();
            lock (_lock)
            {
                if (!isInitialized)
                    return false;

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
                    foreach (var kvp in records)
                    {
                        FileIntegrityRecord rec = kvp.Value;
                        string p = rec.Path;
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
                            rec.CurrentPerms = rec.ExpectedPerms;
                            rec.Status = IntegrityStatus.Match;
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
                        return false;
                    }

                    SecurityLogger.LogEvent("INFO", "Integrity baseline re-calculated by user " + SecurityContext.CurrentUser);
                    return true;
                }
                catch (Exception ex)
                {
                    RestoreRecordSnapshotsLocked(backups.ToArray());
                    SecurityLogger.LogEvent("ERROR", "Integrity baseline update failed: " + ex.Message);
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
