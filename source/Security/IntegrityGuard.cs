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
        PermissionMismatch = 4
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
        private const string DatabasePath = "/etc/integrity.db";
        private const string DatabaseBackupPath = "/etc/integrity.db.bak";

        private static readonly string[] MonitoredPaths = new string[]
        {
            "/etc/passwd",
            "/etc/shadow",
            "/etc/acl.map",
            "/etc/hostname",
            "/etc/resolv.conf",
            "/etc/firewall.cfg",
            "/etc/settings.dat"
        };

        private static readonly Dictionary<string, FileIntegrityRecord> records =
            new Dictionary<string, FileIntegrityRecord>(StringComparer.OrdinalIgnoreCase);

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
                    if (!Directory.Exists("/etc"))
                        Directory.CreateDirectory("/etc");

                    if (!File.Exists(DatabasePath) && File.Exists(DatabaseBackupPath))
                    {
                        File.Copy(DatabaseBackupPath, DatabasePath, true);
                    }

                    if (!File.Exists(DatabasePath))
                    {
                        CreateDefaultBaseline();
                    }
                    else
                    {
                        LoadBaseline();
                    }

                    isInitialized = true;
                    ScanAll();
                }
                catch (Exception ex)
                {
                    SecurityLogger.LogEvent("WARN", "IntegrityGuard init warning: " + ex.Message);
                }
            }
        }

        private static void CreateDefaultBaseline()
        {
            records.Clear();
            for (int i = 0; i < MonitoredPaths.Length; i++)
            {
                string path = MonitoredPaths[i];
                int expectedPerms = (path == "/etc/shadow" || path == "/etc/acl.map") ? 600 : 644;
                string hash = string.Empty;
                long size = 0;

                if (File.Exists(path))
                {
                    try
                    {
                        byte[] data = File.ReadAllBytes(path);
                        hash = Crypto.ComputeSha256(data);
                        size = data.Length;
                    }
                    catch { }
                }

                records[path] = new FileIntegrityRecord
                {
                    Path = path,
                    ExpectedHash = hash,
                    CurrentHash = hash,
                    ExpectedPerms = expectedPerms,
                    CurrentPerms = expectedPerms,
                    FileSize = size,
                    Status = string.IsNullOrEmpty(hash) ? IntegrityStatus.Missing : IntegrityStatus.Match
                };
            }

            SaveBaselineLocked();
            SecurityLogger.LogEvent("INFO", "Host integrity baseline created with " + records.Count + " entries.");
        }

        private static void LoadBaseline()
        {
            records.Clear();
            if (!File.Exists(DatabasePath))
                return;

            string[] lines = File.ReadAllLines(DatabasePath);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                string[] parts = line.Split('|');
                if (parts.Length >= 3)
                {
                    string path = parts[0].Trim();
                    string expectedHash = parts[1].Trim();
                    int perms = 644;
                    int.TryParse(parts[2].Trim(), out perms);

                    records[path] = new FileIntegrityRecord
                    {
                        Path = path,
                        ExpectedHash = expectedHash,
                        ExpectedPerms = perms,
                        Status = IntegrityStatus.Unknown
                    };
                }
            }

            // Ensure all known monitored paths exist in dictionary
            for (int i = 0; i < MonitoredPaths.Length; i++)
            {
                string path = MonitoredPaths[i];
                if (!records.ContainsKey(path))
                {
                    int perms = (path == "/etc/shadow" || path == "/etc/acl.map") ? 600 : 644;
                    records[path] = new FileIntegrityRecord
                    {
                        Path = path,
                        ExpectedHash = string.Empty,
                        ExpectedPerms = perms,
                        Status = IntegrityStatus.Unknown
                    };
                }
            }
        }

        private static void SaveBaselineLocked()
        {
            try
            {
                using (var sw = new StreamWriter(DatabasePath, false))
                {
                    sw.WriteLine("# ZOnderqOS File Integrity Baseline Database");
                    sw.WriteLine("# Format: Path|ExpectedSha256|ExpectedChmod");
                    foreach (var kvp in records)
                    {
                        sw.WriteLine(kvp.Key + "|" + kvp.Value.ExpectedHash + "|" + kvp.Value.ExpectedPerms);
                    }
                }
                File.Copy(DatabasePath, DatabaseBackupPath, true);
            }
            catch (Exception ex)
            {
                SecurityLogger.LogEvent("ERROR", "Failed to save integrity baseline: " + ex.Message);
            }
        }

        public static void ScanAll()
        {
            lock (_lock)
            {
                int violations = 0;

                foreach (var kvp in records)
                {
                    FileIntegrityRecord rec = kvp.Value;
                    string path = rec.Path;

                    if (!File.Exists(path))
                    {
                        rec.Status = IntegrityStatus.Missing;
                        rec.CurrentHash = string.Empty;
                        rec.FileSize = 0;
                        violations++;
                        continue;
                    }

                    try
                    {
                        byte[] data = File.ReadAllBytes(path);
                        rec.FileSize = data.Length;
                        rec.CurrentHash = Crypto.ComputeSha256(data);

                        // Check permissions
                        var acl = PermissionManager.GetPermission(path);
                        int actualPerms = acl.Perms;
                        if (actualPerms == 0) actualPerms = 644;
                        rec.CurrentPerms = actualPerms;

                        if (rec.ExpectedPerms != 0 && actualPerms != rec.ExpectedPerms)
                        {
                            rec.Status = IntegrityStatus.PermissionMismatch;
                            violations++;
                            SecurityLogger.LogEvent("WARN", "Permission mismatch on " + path + ": expected " + rec.ExpectedPerms + ", found " + actualPerms);
                        }
                        else if (!string.IsNullOrEmpty(rec.ExpectedHash) && !Crypto.FixedTimeEquals(rec.ExpectedHash, rec.CurrentHash))
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
                        SecurityLogger.LogEvent("WARN", "Integrity check failed for " + path + ": " + ex.Message);
                    }
                }

                if (violations > 0)
                {
                    SecurityLogger.LogEvent("WARN", "Integrity scan detected " + violations + " file status violation(s).");
                }
            }
        }

        public static void UpdateBaseline()
        {
            lock (_lock)
            {
                foreach (var kvp in records)
                {
                    FileIntegrityRecord rec = kvp.Value;
                    string path = rec.Path;

                    if (File.Exists(path))
                    {
                        try
                        {
                            byte[] data = File.ReadAllBytes(path);
                            rec.ExpectedHash = Crypto.ComputeSha256(data);
                            rec.CurrentHash = rec.ExpectedHash;
                            rec.FileSize = data.Length;
                            var acl = PermissionManager.GetPermission(path);
                            int perms = acl.Perms;
                            if (perms != 0) rec.ExpectedPerms = perms;
                            rec.CurrentPerms = rec.ExpectedPerms;
                            rec.Status = IntegrityStatus.Match;
                        }
                        catch { }
                    }
                    else
                    {
                        rec.ExpectedHash = string.Empty;
                        rec.CurrentHash = string.Empty;
                        rec.Status = IntegrityStatus.Missing;
                    }
                }

                SaveBaselineLocked();
                SecurityLogger.LogEvent("INFO", "Integrity baseline re-calculated by user " + SecurityContext.CurrentUser);
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
                return true; // No hash verification required

            return Crypto.FixedTimeEquals(actualSha256, expectedSha256);
        }
    }
}
