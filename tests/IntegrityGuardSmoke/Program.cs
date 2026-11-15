using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ZonderqOS;
using ZonderqOS.Security;

namespace ZonderqOS
{
    public enum SystemLogLevel
    {
        Info,
        Warning,
        Error
    }

    public static class SystemLogger
    {
        public static readonly List<string> Events = new List<string>();

        public static void Log(SystemLogLevel level, string category, string message)
        {
            Events.Add($"[{level}] {category}: {message}");
        }
    }

    public static class WriteMessage
    {
        public static readonly List<string> Errors = new List<string>();

        public static void WriteError(string message, string source = "SYS")
        {
            Errors.Add($"[{source}] {message}");
        }
    }

    public static class EnvironmentManager
    {
        public static readonly Dictionary<string, string> Vars =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public static void Set(string key, string value)
        {
            Vars[key] = value ?? string.Empty;
        }

        public static string Get(string key) =>
            Vars.TryGetValue(key, out string v) ? v : string.Empty;
    }

    public static class SecurityLogger
    {
        public static readonly List<string> Events = new List<string>();

        public static void LogEvent(string level, string message)
        {
            Events.Add(level + ": " + message);
        }
    }
}

internal static class Program
{
    private const string RootPass1 = "AlphaRoot1!";
    private const string RootPass2 = "AlphaRoot2!";
    private const string AlicePass1 = "AliceUser1!";
    private const string AlicePass2 = "AliceUser2!";
    private const string AlicePass3 = "AliceUser3!";
    private const string LegacyPass = "LegacyUser1!";
    private const string BadPass = "WrongSecret9!";
    private const string AttackPass = "EvilSecret9!";

    private static readonly string[] AllSecrets =
    {
        RootPass1, RootPass2, AlicePass1, AlicePass2, AlicePass3, LegacyPass, BadPass, AttackPass
    };

    private static void Assert(bool cond, string msg)
    {
        if (!cond)
            throw new InvalidOperationException("ASSERT FAILED: " + msg);
    }

    private static void AssertZeroViolations(string phase)
    {
        IntegrityGuard.ScanAll();
        IntegrityGuard.GetReportSummary(out int total, out int passed, out int modified, out int missing, out int permIssues);
        Assert(modified == 0 && missing == 0 && permIssues == 0,
            $"{phase}: expected 0 violations, got total={total} passed={passed} mod={modified} miss={missing} perm={permIssues}");
        Assert(!SecurityLogger.Events.Exists(e => e.Contains("Integrity scan detected", StringComparison.OrdinalIgnoreCase)),
            $"{phase}: unexpected violation log emitted");
    }

    private static void AssertNoSecretLeaks(string phase)
    {
        foreach (string ev in SecurityLogger.Events)
        {
            foreach (string secret in AllSecrets)
                Assert(!ev.Contains(secret, StringComparison.Ordinal), $"{phase}: plaintext secret leaked in SecurityLogger: {ev}");
        }

        foreach (string ev in SystemLogger.Events)
        {
            foreach (string secret in AllSecrets)
                Assert(!ev.Contains(secret, StringComparison.Ordinal), $"{phase}: plaintext secret leaked in SystemLogger: {ev}");
        }

        foreach (string err in WriteMessage.Errors)
        {
            foreach (string secret in AllSecrets)
                Assert(!err.Contains(secret, StringComparison.Ordinal), $"{phase}: plaintext secret leaked in WriteMessage: {err}");
        }
    }

    private static void ClearLogs()
    {
        AssertNoSecretLeaks("pre-clear");
        SecurityLogger.Events.Clear();
        SystemLogger.Events.Clear();
        WriteMessage.Errors.Clear();
    }

    private static void SimulateBoot(bool prepareLogin = true)
    {
        SecurityContext.ResetToBootStateForTests();
        IntegrityGuard.ResetForTests();
        SystemSettings.ResetForTests();
        UserManager.Initialize();
        PermissionManager.Initialize();
        IntegrityGuard.Initialize();
        if (prepareLogin)
            UserManager.PrepareLogin();
    }

    private static string Disk(string root, string logicalPath) =>
        Path.Combine(root, logicalPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

    private static void SeedSystemFiles(string root)
    {
        Directory.CreateDirectory(Disk(root, "/etc/zonderq"));
        Directory.CreateDirectory(Disk(root, "/home"));
        Directory.CreateDirectory(Disk(root, "/tmp"));
        File.WriteAllText(Disk(root, "/etc/hostname"), "zonderq\n");
        File.WriteAllText(Disk(root, "/etc/firewall.cfg"), "allow=none\n");
    }

    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "zq-ig-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("ZIG_TEST_ROOT", root);
            SeedSystemFiles(root);

            // 1. Fresh install boot (before settings.conf or initial root password change): 0 violations, settings.conf = NotCreated
            ClearLogs();
            SimulateBoot();
            Assert(UserManager.RequiresInitialRootPasswordSetup(), "fresh install should require initial root setup");
            IntegrityGuard.GetReportSummary(out int total, out int passed, out int modified, out int missing, out int permIssues);
            Assert(total == 6 && passed == 5 && modified == 0 && missing == 0 && permIssues == 0,
                $"fresh install summary unexpected: total={total} passed={passed} mod={modified} miss={missing} perm={permIssues}");
            AssertZeroViolations("fresh-install");
            var settingsRec = IntegrityGuard.GetRecords().Find(r => r.Path == "/etc/zonderq/settings.conf");
            Assert(settingsRec != null && settingsRec.Status == IntegrityStatus.NotCreated,
                "settings.conf should be NotCreated on fresh install");

            // 2. Weak initial root password rejected, then valid initial root setup completes and survives reboot + first login profile ACL sync
            Assert(!UserManager.CompleteInitialRootPasswordSetup("weak"), "weak initial root password must fail");
            Assert(UserManager.RequiresInitialRootPasswordSetup(), "failed weak setup must leave setup pending");
            Assert(UserManager.CompleteInitialRootPasswordSetup(RootPass1), "initial root password setup should succeed");
            Assert(!UserManager.RequiresInitialRootPasswordSetup(), "initial root setup flag should clear after setup");
            AssertZeroViolations("post-initial-root-setup");

            // Setup cannot be re-run after completion
            Assert(!UserManager.CompleteInitialRootPasswordSetup(AttackPass), "repeating initial root setup must fail");

            // Reboot and authenticate with configured root password (triggers EnsureProfile ACL sync)
            ClearLogs();
            SimulateBoot();
            AssertZeroViolations("reboot-after-initial-setup");
            Assert(!UserManager.RequiresInitialRootPasswordSetup(), "reboot must remember configured root credential");
            Assert(UserManager.TryStartSession("root", RootPass1), "root login after initial setup must succeed");
            AssertZeroViolations("post-root-first-login-acl");

            // 3. Standard user password change (wrong auth rejected, valid succeeds, reboot preserves)
            Assert(!UserManager.ChangePassword("root", BadPass, RootPass2), "ChangePassword with wrong old password must fail");
            Assert(UserManager.ChangePassword("root", RootPass1, RootPass2), "root standard password change must succeed");
            AssertZeroViolations("post-root-password-change");
            Assert(!UserManager.ValidateCredentials("root", RootPass1), "old root password must no longer work");
            Assert(UserManager.ValidateCredentials("root", RootPass2), "new root password must validate");

            ClearLogs();
            SimulateBoot();
            AssertZeroViolations("reboot-after-root-password-change");
            Assert(UserManager.TryStartSession("root", RootPass2), "root login with new password after reboot must succeed");

            // 4. Create account + first login profile ACL + standard user password change + least-privilege denials
            Assert(UserManager.CreateUser("alice", AlicePass1), "CreateUser(alice) by root must succeed");
            Assert(UserManager.UserExists("alice"), "alice should exist in /etc/passwd");
            Assert(Directory.Exists(Disk(root, "/home/alice")), "/home/alice should be created");
            AssertZeroViolations("post-create-alice");

            UserManager.EndSession();
            UserManager.PrepareLogin();
            Assert(UserManager.TryStartSession("alice", AlicePass1), "alice first login must succeed");
            Assert(SecurityContext.CurrentUid == 1000 && SecurityContext.CurrentUser == "alice", "alice context unexpected");
            AssertZeroViolations("post-alice-first-login");

            // Non-root denials
            Assert(!UserManager.CreateUser("bob", AlicePass1), "non-root CreateUser must fail");
            Assert(!UserManager.ChangePassword("root", AlicePass1, AttackPass), "non-root reset of root password must fail");
            Assert(!IntegrityGuard.UpdateBaseline(), "non-root UpdateBaseline must fail");

            // Self password change as alice
            Assert(!UserManager.ChangePassword("alice", BadPass, AlicePass2), "alice wrong old password must fail");
            Assert(UserManager.ChangePassword("alice", AlicePass1, AlicePass2), "alice own password change must succeed");
            AssertZeroViolations("post-alice-self-password-change");

            // 5. Admin reset of another user's password (requires confirming root password)
            UserManager.EndSession();
            UserManager.PrepareLogin();
            Assert(UserManager.TryStartSession("root", RootPass2), "root re-login must succeed");
            Assert(!UserManager.ChangePassword("alice", BadPass, AlicePass3), "root admin reset without valid root confirmation must fail");
            Assert(UserManager.ChangePassword("alice", RootPass2, AlicePass3), "root admin reset for alice must succeed");
            AssertZeroViolations("post-admin-reset-alice");
            Assert(!UserManager.ValidateCredentials("alice", AlicePass2), "alice old password must fail after admin reset");
            Assert(UserManager.ValidateCredentials("alice", AlicePass3), "alice new password must work after admin reset");

            // Reboot confirms all changes persisted and 0 integrity violations
            ClearLogs();
            SimulateBoot();
            AssertZeroViolations("reboot-after-user-and-reset");
            Assert(UserManager.TryStartSession("alice", AlicePass3), "alice login after reboot must succeed");
            UserManager.EndSession();
            UserManager.PrepareLogin();
            Assert(UserManager.TryStartSession("root", RootPass2), "root re-login after reboot must succeed");

            // 6. Legacy credential migration (salt$FNV -> v2$...) while root updates baseline for pre-seeded legacy user
            string legacySalt = "legacysalt";
            string legacyCred = legacySalt + "$" + Crypto.HashPassword(LegacyPass, legacySalt);
            File.AppendAllText(Disk(root, "/etc/passwd"), "legacyuser:x:1001:/home/legacyuser\n");
            File.AppendAllText(Disk(root, "/etc/shadow"), "legacyuser:" + legacyCred + "\n");
            Assert(IntegrityGuard.UpdateBaseline(), "authenticated root baseline update for legacy fixture should succeed");
            AssertZeroViolations("pre-legacy-migration");

            UserManager.EndSession();
            UserManager.PrepareLogin();
            Assert(UserManager.TryStartSession("legacyuser", LegacyPass), "legacyuser login must succeed");
            string shadowAfterUpgrade = File.ReadAllText(Disk(root, "/etc/shadow"));
            Assert(!shadowAfterUpgrade.Contains(legacyCred, StringComparison.Ordinal) &&
                   shadowAfterUpgrade.Contains("legacyuser:v2$", StringComparison.Ordinal),
                "legacy credential must upgrade to v2$ in /etc/shadow");
            AssertZeroViolations("post-legacy-migration");
            SimulateBoot();
            AssertZeroViolations("reboot-after-legacy-migration");
            Assert(UserManager.TryStartSession("legacyuser", LegacyPass), "upgraded legacyuser login after reboot must succeed");

            // 7. SystemSettings fresh save + tamper + delete detection
            UserManager.EndSession();
            UserManager.PrepareLogin();
            Assert(UserManager.TryStartSession("root", RootPass2), "root login before settings test must succeed");
            SystemSettings.CycleAutoLockTimeout(); // 5 -> 15
            Assert(SystemSettings.Save(), "authenticated root SystemSettings.Save must succeed");
            AssertZeroViolations("post-settings-save");
            SimulateBoot();
            AssertZeroViolations("reboot-after-settings-save");
            Assert(UserManager.TryStartSession("root", RootPass2), "root login after settings reboot must succeed");

            string settingsDisk = Disk(root, "/etc/zonderq/settings.conf");
            string savedSettings = File.ReadAllText(settingsDisk);
            File.WriteAllText(settingsDisk, "schema=1\nauto_lock_minutes=0\n");
            IntegrityGuard.ScanAll();
            IntegrityGuard.GetReportSummary(out total, out passed, out modified, out missing, out permIssues);
            Assert(modified == 1 && missing == 0, $"tampered settings.conf not detected (mod={modified})");

            // Restore settings and re-verify before deletion test
            ClearLogs();
            File.WriteAllText(settingsDisk, savedSettings);
            AssertZeroViolations("restored-settings");
            File.Delete(settingsDisk);
            IntegrityGuard.ScanAll();
            IntegrityGuard.GetReportSummary(out total, out passed, out modified, out missing, out permIssues);
            Assert(missing == 1, $"deleted saved settings.conf not detected (miss={missing})");
            ClearLogs();
            File.WriteAllText(settingsDisk, savedSettings);
            AssertZeroViolations("restored-settings-after-delete");

            // 8. Unauthorized tampering detection & refusal to launder compromised protected files
            string cleanShadow = File.ReadAllText(Disk(root, "/etc/shadow"));
            File.WriteAllText(Disk(root, "/etc/shadow"), cleanShadow + "# tampered\n");
            IntegrityGuard.ScanAll();
            IntegrityGuard.GetReportSummary(out total, out passed, out modified, out missing, out permIssues);
            Assert(modified == 1, $"tampered /etc/shadow not detected (mod={modified})");
            Assert(!UserManager.ChangePassword("root", RootPass2, AttackPass),
                "ChangePassword must REFUSE to update/launder tampered /etc/shadow");
            Assert(!UserManager.CreateUser("mallory", AttackPass),
                "CreateUser must REFUSE to proceed when /etc/shadow is tampered");
            IntegrityGuard.ScanAll();
            IntegrityGuard.GetReportSummary(out total, out passed, out modified, out missing, out permIssues);
            Assert(modified == 1, "tampered /etc/shadow must remain flagged after refused mutations");
            ClearLogs();
            File.WriteAllText(Disk(root, "/etc/shadow"), cleanShadow);
            AssertZeroViolations("shadow-restored");

            string cleanPasswd = File.ReadAllText(Disk(root, "/etc/passwd"));
            File.WriteAllText(Disk(root, "/etc/passwd"), cleanPasswd + "rogue:x:0:0::/root:/bin/sh\n");
            Assert(!UserManager.CreateUser("mallory", AttackPass),
                "CreateUser must REFUSE when /etc/passwd was tampered beforehand");
            IntegrityGuard.ScanAll();
            IntegrityGuard.GetReportSummary(out total, out passed, out modified, out missing, out permIssues);
            Assert(modified == 1, "tampered /etc/passwd must be flagged");
            ClearLogs();
            File.WriteAllText(Disk(root, "/etc/passwd"), cleanPasswd);
            AssertZeroViolations("passwd-restored");

            string cleanAcl = File.ReadAllText(Disk(root, "/etc/acl.map"));
            File.WriteAllText(Disk(root, "/etc/acl.map"), cleanAcl + "/etc/shadow|alice|777\n");
            Assert(!PermissionManager.SetPermission("/tmp/acl-tamper-test", "root", 600),
                "SetPermission must REFUSE to overwrite tampered /etc/acl.map");
            ClearLogs();
            File.WriteAllText(Disk(root, "/etc/acl.map"), cleanAcl);
            AssertZeroViolations("acl-restored");

            // 9. NotifyManagedFileSaved & unauthenticated UpdateBaseline hardening
            Assert(!IntegrityGuard.NotifyManagedFileSaved("/etc/shadow"), "NotifyManagedFileSaved must reject /etc/shadow");
            Assert(!IntegrityGuard.NotifyManagedFileSaved("/etc/passwd"), "NotifyManagedFileSaved must reject /etc/passwd");
            Assert(!IntegrityGuard.NotifyManagedFileSaved("/etc/acl.map"), "NotifyManagedFileSaved must reject /etc/acl.map");
            Assert(!IntegrityGuard.NotifyManagedFileSaved("/etc/hostname"), "NotifyManagedFileSaved must reject /etc/hostname");
            Assert(!IntegrityGuard.NotifyManagedFileSaved("/etc/firewall.cfg"), "NotifyManagedFileSaved must reject /etc/firewall.cfg");

            UserManager.EndSession();
            Assert(!IntegrityGuard.UpdateBaseline(), "unauthenticated UpdateBaseline must fail");
            UserManager.PrepareLogin();
            Assert(UserManager.TryStartSession("root", RootPass2), "root re-login failed");

            // 10. Simulated write & integrity.db failure rollback
            IntegrityGuard.SimulateDatabaseWriteFailure = true;
            Assert(!UserManager.ChangePassword("root", RootPass2, AttackPass),
                "ChangePassword must fail and rollback when integrity.db write fails");
            IntegrityGuard.SimulateDatabaseWriteFailure = false;
            Assert(UserManager.ValidateCredentials("root", RootPass2), "rolled-back root password must still work");
            Assert(!UserManager.ValidateCredentials("root", AttackPass), "failed password must not take effect");
            AssertZeroViolations("after-db-fail-password-rollback");

            IntegrityGuard.SimulateDatabaseWriteFailure = true;
            Assert(!UserManager.CreateUser("bob", AlicePass1),
                "CreateUser must fail and rollback when integrity.db / ACL sync fails");
            IntegrityGuard.SimulateDatabaseWriteFailure = false;
            Assert(!UserManager.UserExists("bob"), "rolled-back CreateUser must remove bob from /etc/passwd");
            AssertZeroViolations("after-db-fail-createuser-rollback");

            IntegrityGuard.SimulatedWriteFailurePath = "/etc/shadow.swap";
            Assert(!UserManager.CreateUser("charlie", AlicePass1),
                "CreateUser must rollback /etc/passwd if /etc/shadow swap fails");
            IntegrityGuard.SimulatedWriteFailurePath = null;
            Assert(!UserManager.UserExists("charlie"), "charlie must not remain in /etc/passwd after shadow swap failure");
            AssertZeroViolations("after-shadow-swap-fail-rollback");

            IntegrityGuard.SimulateDatabaseWriteFailure = true;
            SystemSettings.CycleAutoLockTimeout(); // cycles past 15
            Assert(!SystemSettings.Save(), "SystemSettings.Save must fail and rollback if integrity sync fails");
            IntegrityGuard.SimulateDatabaseWriteFailure = false;
            Assert(File.ReadAllText(settingsDisk).Contains("auto_lock_minutes=15", StringComparison.Ordinal),
                "settings.conf must retain previous contents after rollback");
            AssertZeroViolations("after-settings-rollback");

            // 11. Crash recovery of .bak + .new files across reboot (both shadow and integrity.db)
            File.Move(Disk(root, "/etc/shadow"), Disk(root, "/etc/shadow.bak"), overwrite: true);
            File.WriteAllText(Disk(root, "/etc/shadow.new"), "corrupted-partial-temp\n");
            File.Move(Disk(root, "/etc/integrity.db"), Disk(root, "/etc/integrity.db.bak"), overwrite: true);
            File.WriteAllText(Disk(root, "/etc/integrity.db"), string.Empty); // 0-byte truncated active DB
            File.WriteAllText(Disk(root, "/etc/integrity.db.new"), "half-written-db\n");
            SimulateBoot();
            AssertZeroViolations("after-crash-recovery-reboot");
            Assert(UserManager.TryStartSession("root", RootPass2), "root login after crash recovery must succeed");

            // 12. Missing companion /etc/shadow on initialized system MUST NOT regenerate factory root:root
            string backupShadow = File.ReadAllText(Disk(root, "/etc/shadow"));
            File.Delete(Disk(root, "/etc/shadow"));
            SimulateBoot();
            Assert(File.Exists(Disk(root, "/etc/shadow")) == false,
                "UserManager.Initialize must NOT recreate factory /etc/shadow when integrity.db already exists");
            Assert(!UserManager.RequiresInitialRootPasswordSetup(),
                "missing /etc/shadow on initialized system must NOT re-arm unauthenticated initial root setup");
            Assert(!UserManager.CompleteInitialRootPasswordSetup(AttackPass),
                "CompleteInitialRootPasswordSetup must refuse when /etc/shadow is missing");
            IntegrityGuard.ScanAll();
            IntegrityGuard.GetReportSummary(out total, out passed, out modified, out missing, out permIssues);
            Assert(missing == 1, $"missing /etc/shadow must be detected (miss={missing})");
            ClearLogs();
            File.WriteAllText(Disk(root, "/etc/shadow"), backupShadow);
            AssertZeroViolations("after-missing-shadow-restored");

            // 13. Verify zero plaintext passwords anywhere in logs across the whole suite
            AssertNoSecretLeaks("end-of-suite");

            Console.WriteLine("[INTEGRITY-GUARD] PASS (13/13 end-to-end account, ACL, settings, tamper, rollback, recovery scenarios)");
            return 0;
        }
        finally
        {
            Environment.SetEnvironmentVariable("ZIG_TEST_ROOT", null);
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch { }
        }
    }
}
