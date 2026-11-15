using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using ZonderqOS.Security;

namespace ZonderqOS.Security
{
    public static class SecurityContext
    {
        public static string CurrentUser => "root";
    }

    public static class SecurityLogger
    {
        public static readonly List<string> Events = new List<string>();
        public static void LogEvent(string level, string message) =>
            Events.Add(level + ": " + message);
    }

    public sealed class FileAcl
    {
        public int Perms { get; set; }
    }

    public static class PermissionManager
    {
        public static readonly Dictionary<string, int> Perms =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public static FileAcl GetPermission(string path)
        {
            if (Perms.TryGetValue(path, out int p))
                return new FileAcl { Perms = p };
            return new FileAcl { Perms = 0 };
        }
    }

    public static class Crypto
    {
        public static string ComputeSha256(byte[] data)
        {
            byte[] hash = SHA256.HashData(data);
            return Convert.ToHexString(hash);
        }

        public static bool FixedTimeEquals(string a, string b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class Program
{
    private static void Assert(bool cond, string msg)
    {
        if (!cond) throw new InvalidOperationException("ASSERT FAILED: " + msg);
    }

    private static void SeedRequired(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "etc", "zonderq"));
        File.WriteAllText(Path.Combine(root, "etc", "passwd"), "root:x:0:0::/root:/bin/sh\n");
        File.WriteAllText(Path.Combine(root, "etc", "shadow"), "root:hash\n");
        File.WriteAllText(Path.Combine(root, "etc", "acl.map"), "root\n");
        File.WriteAllText(Path.Combine(root, "etc", "hostname"), "zonderq\n");
        File.WriteAllText(Path.Combine(root, "etc", "firewall.cfg"), "allow=none\n");
        PermissionManager.Perms.Clear();
        PermissionManager.Perms["/etc/passwd"] = 644;
        PermissionManager.Perms["/etc/shadow"] = 600;
        PermissionManager.Perms["/etc/acl.map"] = 600;
        PermissionManager.Perms["/etc/hostname"] = 644;
        PermissionManager.Perms["/etc/firewall.cfg"] = 600;
    }

    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "zq-ig-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("ZIG_TEST_ROOT", root);
            SeedRequired(root);

            // 1. Fresh install: settings.conf absent -> 0 violations, NotCreated status
            SecurityLogger.Events.Clear();
            IntegrityGuard.ResetForTests();
            IntegrityGuard.Initialize();
            IntegrityGuard.GetReportSummary(out int total, out int passed, out int modified, out int missing, out int permIssues);
            Assert(total == 6 && passed == 5 && modified == 0 && missing == 0 && permIssues == 0,
                $"fresh install summary unexpected: total={total} passed={passed} mod={modified} miss={missing} perm={permIssues}");
            Assert(!SecurityLogger.Events.Exists(e => e.Contains("violation")),
                "fresh install emitted false violation warning");
            var recs = IntegrityGuard.GetRecords();
            var settingsRec = recs.Find(r => r.Path == "/etc/zonderq/settings.conf");
            Assert(settingsRec != null && settingsRec.Status == IntegrityStatus.NotCreated,
                "settings.conf should be NotCreated on fresh install");

            // 2. Later settings write: settings.conf created with chmod 600 -> NotifyManagedFileSaved -> Match (0 violations)
            string settingsDisk = Path.Combine(root, "etc", "zonderq", "settings.conf");
            File.WriteAllText(settingsDisk, "schema=1\nauto_lock_minutes=5\n");
            PermissionManager.Perms["/etc/zonderq/settings.conf"] = 600;
            IntegrityGuard.NotifyManagedFileSaved("/etc/zonderq/settings.conf");
            SecurityLogger.Events.Clear();
            IntegrityGuard.ResetForTests();
            IntegrityGuard.Initialize(); // reload persisted integrity.db
            IntegrityGuard.GetReportSummary(out total, out passed, out modified, out missing, out permIssues);
            Assert(passed == 6 && modified == 0 && missing == 0 && permIssues == 0,
                $"post-save reboot summary unexpected: passed={passed} mod={modified} miss={missing} perm={permIssues}");

            // 3. Real tampering of saved settings.conf -> Modified detected
            File.WriteAllText(settingsDisk, "schema=1\nauto_lock_minutes=0\n");
            IntegrityGuard.ScanAll();
            IntegrityGuard.GetReportSummary(out total, out passed, out modified, out missing, out permIssues);
            Assert(modified == 1 && missing == 0, $"tampered settings.conf not detected (mod={modified})");

            // 4. Real deletion of previously saved settings.conf -> Missing detected
            File.Delete(settingsDisk);
            IntegrityGuard.ScanAll();
            IntegrityGuard.GetReportSummary(out total, out passed, out modified, out missing, out permIssues);
            Assert(missing == 1, $"deleted saved settings.conf not detected (miss={missing})");

            // 5. Real deletion of required file (/etc/passwd) -> Missing detected
            File.Delete(Path.Combine(root, "etc", "passwd"));
            IntegrityGuard.ScanAll();
            IntegrityGuard.GetReportSummary(out total, out passed, out modified, out missing, out permIssues);
            Assert(missing == 2, $"deleted /etc/passwd not detected (miss={missing})");

            // 6. Unauthorized rogue creation of settings.conf when fresh (never saved via SystemSettings) -> Modified detected
            IntegrityGuard.ResetForTests();
            File.Delete(Path.Combine(root, "etc", "integrity.db"));
            File.Delete(Path.Combine(root, "etc", "integrity.db.bak"));
            SeedRequired(root);
            IntegrityGuard.Initialize();
            File.WriteAllText(settingsDisk, "rogue=1\n");
            IntegrityGuard.ScanAll();
            IntegrityGuard.GetReportSummary(out total, out passed, out modified, out missing, out permIssues);
            Assert(modified == 1, $"rogue settings.conf creation not detected (mod={modified})");

            Console.WriteLine("[INTEGRITY-GUARD] PASS (6/6 fresh-install, post-save, tamper, deletion scenarios)");
            return 0;
        }
        finally
        {
            Environment.SetEnvironmentVariable("ZIG_TEST_ROOT", null);
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }
}
