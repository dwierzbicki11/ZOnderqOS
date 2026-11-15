using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        private static readonly object Sync = new object();

        public static void Log(SystemLogLevel level, string category, string message)
        {
            lock (Sync)
                Events.Add($"[{level}] {category}: {message}");
        }
    }

    public static class WriteMessage
    {
        public static readonly List<string> Errors = new List<string>();
        private static readonly object Sync = new object();

        public static void WriteError(string message, string source = "SYS")
        {
            lock (Sync)
                Errors.Add($"[{source}] {message}");
        }
    }

    public static class EnvironmentManager
    {
        public static readonly Dictionary<string, string> Vars =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly object Sync = new object();

        public static void Set(string key, string value)
        {
            lock (Sync)
                Vars[key] = value ?? string.Empty;
        }

        public static string Get(string key)
        {
            lock (Sync)
                return Vars.TryGetValue(key, out string v) ? v : string.Empty;
        }
    }

    public static class SecurityLogger
    {
        public static readonly List<string> Events = new List<string>();
        private static readonly object Sync = new object();

        public static void LogEvent(string level, string message)
        {
            lock (Sync)
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
    private const string WorkerPass1 = "WorkerPass1!";
    private const string LegacyPass = "LegacyUser1!";
    private const string BadPass = "WrongSecret9!";
    private const string AttackPass = "EvilSecret9!";

    private static readonly string[] AllSecrets =
    {
        RootPass1, RootPass2, AlicePass1, AlicePass2, AlicePass3, WorkerPass1, LegacyPass, BadPass, AttackPass
    };

    private static void Assert(bool cond, string msg)
    {
        if (!cond)
            throw new InvalidOperationException("ASSERT FAILED: " + msg);
    }

    private static void AssertZeroViolations(string phase)
    {
        Assert(IntegrityGuard.IsInitialized && !IntegrityGuard.HasInitializationError,
            $"{phase}: IntegrityGuard expected initialized without error, got state={IntegrityGuard.InitializationState} err='{IntegrityGuard.InitializationError}'");
        IntegrityGuard.ScanAll();
        IntegrityGuard.GetReportSummary(out int total, out int passed, out int modified, out int missing, out int permIssues);
        Assert(modified == 0 && missing == 0 && permIssues == 0,
            $"{phase}: expected 0 violations, got total={total} passed={passed} mod={modified} miss={missing} perm={permIssues}");
        lock (SecurityLogger.Events)
        {
            Assert(!SecurityLogger.Events.Exists(e => e.Contains("Integrity scan detected", StringComparison.OrdinalIgnoreCase)),
                $"{phase}: unexpected violation log emitted");
        }
    }

    private static void AssertNoSecretLeaks(string phase)
    {
        foreach (string ev in SecurityLogger.Events.ToArray())
        {
            foreach (string secret in AllSecrets)
                Assert(!ev.Contains(secret, StringComparison.Ordinal), $"{phase}: plaintext secret leaked in SecurityLogger: {ev}");
        }

        foreach (string ev in SystemLogger.Events.ToArray())
        {
            foreach (string secret in AllSecrets)
                Assert(!ev.Contains(secret, StringComparison.Ordinal), $"{phase}: plaintext secret leaked in SystemLogger: {ev}");
        }

        foreach (string err in WriteMessage.Errors.ToArray())
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
        IntegrityGuard.SimulateDatabaseWriteFailure = false;
        IntegrityGuard.SimulatedWriteFailurePath = null;
        IntegrityGuard.ResetForTests();
        SystemSettings.ResetForTests();
        UserManager.Initialize();
        PermissionManager.Initialize();
        SystemSettings.Load();
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
        Directory.CreateDirectory(Disk(root, "/var/log"));
        File.WriteAllText(Disk(root, "/etc/hostname"), "zonderq\n");
        File.WriteAllText(Disk(root, "/etc/firewall.cfg"), "allow=none\n");
    }

    private static string CreateIsolatedRoot(string prefix)
    {
        string isolated = Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));
        SeedSystemFiles(isolated);
        return isolated;
    }

    private static void RunIsolated(string prefix, Action<string> action, string restoreRoot)
    {
        string isolated = CreateIsolatedRoot(prefix);
        try
        {
            Environment.SetEnvironmentVariable("ZIG_TEST_ROOT", isolated);
            action(isolated);
            AssertNoSecretLeaks(prefix);
        }
        finally
        {
            IntegrityGuard.SimulateDatabaseWriteFailure = false;
            IntegrityGuard.SimulatedWriteFailurePath = null;
            Environment.SetEnvironmentVariable("ZIG_TEST_ROOT", restoreRoot);
            try
            {
                if (Directory.Exists(isolated))
                    Directory.Delete(isolated, true);
            }
            catch { }
        }
    }

    private static int Main()
    {
        string root = CreateIsolatedRoot("zq-ig-smoke");
        try
        {
            Environment.SetEnvironmentVariable("ZIG_TEST_ROOT", root);

            // Scenario 1: Fresh install + Case 4 (internal boot ACL writes during initialization)
            ClearLogs();
            SimulateBoot(prepareLogin: false);
            Assert(SecurityContext.IsBootInitializationActive, "boot init should be active before PrepareLogin");
            Assert(PermissionManager.SetBootSystemPermissionInternal("/etc/hostname", 644),
                "internal boot ACL write for allowlisted /etc/hostname must succeed before PrepareLogin");
            Assert(!PermissionManager.SetBootSystemPermissionInternal("/etc/shadow", 644),
                "internal boot ACL helper must reject non-allowlisted /etc/shadow");
            Assert(!PermissionManager.SetPermission("/home/alice/early.txt", "alice", 644),
                "unauthenticated public SetPermission must fail even during boot init");
            UserManager.PrepareLogin();
            Assert(!SecurityContext.IsBootInitializationActive, "PrepareLogin must seal boot initialization");
            Assert(!PermissionManager.SetBootSystemPermissionInternal("/etc/hostname", 644),
                "SetBootSystemPermissionInternal must be blocked once login state is entered");
            Assert(PermissionManager.SetKernelTelemetryAclInternal("/var/log/system.log", 600),
                "internal telemetry ACL helper must manage allowlisted telemetry log paths");
            Assert(!PermissionManager.SetKernelTelemetryAclInternal("/etc/shadow", 600),
                "internal telemetry ACL helper must reject /etc/shadow");

            Assert(UserManager.RequiresInitialRootPasswordSetup(), "fresh install should require initial root setup");
            IntegrityGuard.GetReportSummary(out int total, out int passed, out int modified, out int missing, out int permIssues);
            Assert(total == 6 && passed == 5 && modified == 0 && missing == 0 && permIssues == 0,
                $"fresh install summary unexpected: total={total} passed={passed} mod={modified} miss={missing} perm={permIssues}");
            AssertZeroViolations("fresh-install");
            var settingsRec = IntegrityGuard.GetRecords().Find(r => r.Path == "/etc/zonderq/settings.conf");
            Assert(settingsRec != null && settingsRec.Status == IntegrityStatus.NotCreated,
                "settings.conf should be NotCreated on fresh install");

            // Scenario 2: Initial root setup (including baseline write failure rollback before setup)
            Assert(!UserManager.CompleteInitialRootPasswordSetup("weak"), "weak initial root password must fail");
            Assert(UserManager.RequiresInitialRootPasswordSetup(), "failed weak setup must leave setup pending");

            IntegrityGuard.SimulateDatabaseWriteFailure = true;
            Assert(!UserManager.CompleteInitialRootPasswordSetup(RootPass1),
                "initial root setup must rollback if integrity.db save fails");
            IntegrityGuard.SimulateDatabaseWriteFailure = false;
            Assert(UserManager.RequiresInitialRootPasswordSetup(),
                "failed initial root setup must preserve pending initial setup state");
            AssertZeroViolations("initial-root-rollback");

            Assert(UserManager.CompleteInitialRootPasswordSetup(RootPass1), "initial root password setup should succeed");
            Assert(!UserManager.RequiresInitialRootPasswordSetup(), "initial root setup flag should clear after setup");
            AssertZeroViolations("post-initial-root-setup");
            Assert(!UserManager.CompleteInitialRootPasswordSetup(AttackPass), "repeating initial root setup must fail");

            ClearLogs();
            SimulateBoot();
            AssertZeroViolations("reboot-after-initial-setup");
            Assert(!UserManager.RequiresInitialRootPasswordSetup(), "reboot must remember configured root credential");
            Assert(UserManager.TryStartSession("root", RootPass1), "root login after initial setup must succeed");
            AssertZeroViolations("post-root-first-login-acl");

            // Case 3: Authorized admin operation (root ACL, account, settings, baseline)
            Assert(PermissionManager.SetPermission("/tmp/rootonly.txt", "root", 600),
                "root SetPermission on /tmp/rootonly.txt must succeed");
            Assert(UserManager.CreateUser("alice", AlicePass1), "CreateUser(alice) by root must succeed");
            Assert(UserManager.UserExists("alice"), "alice should exist in /etc/passwd");
            Assert(Directory.Exists(Disk(root, "/home/alice")), "/home/alice should be created");
            AssertZeroViolations("post-create-alice");

            // Password change + wrong password check
            Assert(!UserManager.ChangePassword("root", BadPass, RootPass2), "ChangePassword with wrong old password must fail");
            Assert(UserManager.ChangePassword("root", RootPass1, RootPass2), "root standard password change must succeed");
            AssertZeroViolations("post-root-password-change");
            Assert(!UserManager.ValidateCredentials("root", RootPass1), "old root password must no longer work");
            Assert(UserManager.ValidateCredentials("root", RootPass2), "new root password must validate");

            ClearLogs();
            SimulateBoot();
            AssertZeroViolations("reboot-after-root-password-change");
            Assert(UserManager.TryStartSession("root", RootPass2), "root login with new password after reboot must succeed");

            // Even root must not weaken core security database ACL modes or ownership
            Assert(!PermissionManager.SetPermission("/etc/shadow", "root", 777),
                "root must be prevented from setting /etc/shadow to 777");
            Assert(!PermissionManager.SetPermission("/etc/shadow", "alice", 600),
                "root must be prevented from transferring /etc/shadow ownership to alice");
            Assert(!PermissionManager.SetPermission("/etc/passwd", "root", 666),
                "root must be prevented from making /etc/passwd world-writable");
            Assert(!PermissionManager.RemovePermission("/etc/shadow"),
                "root must be prevented from deleting /etc/shadow ACL entry");
            Assert(!PermissionManager.RemovePermissionsUnder("/etc"),
                "root must be prevented from mass-wiping /etc ACL entries");
            AssertZeroViolations("post-core-db-guard");

            // Cases 1 & 2 & 13: Unauthorized ACL changes, escalation to root, unauthorized re-baseline
            UserManager.EndSession();
            UserManager.PrepareLogin();
            Assert(!PermissionManager.SetPermission("/tmp/unauth.txt", "root", 644), "unauthenticated SetPermission must fail");
            Assert(!PermissionManager.RemovePermission("/tmp/rootonly.txt"), "unauthenticated RemovePermission must fail");
            Assert(!PermissionManager.RemovePermissionsUnder("/home"), "unauthenticated RemovePermissionsUnder must fail");
            Assert(!PermissionManager.CopyPermissionsUnder("/tmp/rootonly.txt", "/tmp/copy", "root"), "unauthenticated CopyPermissionsUnder must fail");
            Assert(!PermissionManager.MovePermissionsUnder("/tmp/rootonly.txt", "/tmp/moved"), "unauthenticated MovePermissionsUnder must fail");
            Assert(!PermissionManager.SaveAcl(), "unauthenticated SaveAcl must fail");
            int logsBeforeUnauthLoad = SecurityLogger.Events.Count;
            PermissionManager.LoadAcl();
            Assert(SecurityLogger.Events.Skip(logsBeforeUnauthLoad).Any(e => e.Contains("Blocked unauthorized PermissionManager.LoadAcl()")),
                "unauthenticated LoadAcl must be blocked");
            Assert(!SystemSettings.Save(), "unauthenticated SystemSettings.Save must fail");
            Assert(!IntegrityGuard.UpdateBaseline(), "unauthenticated UpdateBaseline must fail");
            Assert(!IntegrityGuard.RecoverBaseline(out _), "unauthenticated RecoverBaseline must fail");

            Assert(UserManager.TryStartSession("alice", AlicePass1), "alice first login must succeed");
            Assert(SecurityContext.CurrentUid == 1000 && SecurityContext.CurrentUser == "alice", "alice context unexpected");
            AssertZeroViolations("post-alice-first-login");

            // Valid user ACL actions inside own home and /tmp
            Assert(PermissionManager.SetPermission("/home/alice/notes.txt", "alice", 600),
                "alice must be allowed to set ACL on own file under /home/alice");
            Assert(PermissionManager.CopyPermissionsUnder("/home/alice/notes.txt", "/home/alice/notes-copy.txt", "alice"),
                "alice must be allowed to copy ACL within /home/alice");
            Assert(PermissionManager.MovePermissionsUnder("/home/alice/notes-copy.txt", "/tmp/alice-moved.txt"),
                "alice must be allowed to move own ACL into /tmp");
            Assert(PermissionManager.RemovePermission("/tmp/alice-moved.txt"),
                "alice must be allowed to remove own /tmp ACL");
            Assert(PermissionManager.RemovePermission("/home/alice/notes.txt"),
                "alice must be allowed to remove own file ACL");

            // Disallowed ACL mutations & path traversal by non-root alice
            Assert(!PermissionManager.SetPermission("/etc/hostname", "alice", 644),
                "non-root must not alter /etc/hostname ACL");
            Assert(!PermissionManager.SetPermission("/home/alice/../../etc/shadow", "alice", 777),
                "path traversal ACL mutation to /etc/shadow must be rejected");
            Assert(!PermissionManager.SetPermission("/home/bob/secret.txt", "alice", 600),
                "alice must not set ACL inside another user's home");
            Assert(!PermissionManager.SetPermission("/tmp/rootonly.txt", "alice", 644),
                "alice must not hijack root-owned ACL in /tmp");
            Assert(!PermissionManager.SetPermission("/home/alice/suid.sh", "root", 755),
                "alice must not assign root owner to her file");
            Assert(!PermissionManager.RemovePermission("/etc/shadow"),
                "alice must not remove /etc/shadow ACL");
            Assert(!PermissionManager.RemovePermissionsUnder("/home"),
                "alice must not wipe /home ACLs");
            Assert(!PermissionManager.CopyPermissionsUnder("/etc/shadow", "/tmp/alice-shadow", "alice"),
                "alice must not copy protected unreadable /etc/shadow ACL");
            Assert(!PermissionManager.CopyPermissionsUnder("/etc/passwd", "/tmp/alice-passwd-root", "root"),
                "alice must not copy ACL while assigning root owner");
            Assert(!PermissionManager.CopyPermissionsUnder("/etc/passwd", "/etc/alice-passwd", "alice"),
                "alice must not write copied ACL under /etc");
            Assert(!PermissionManager.MovePermissionsUnder("/etc/hostname", "/tmp/alice-host"),
                "alice must not move protected system ACL");
            Assert(!PermissionManager.SaveAcl(), "non-root SaveAcl must fail");
            int logsBeforeAliceLoad = SecurityLogger.Events.Count;
            PermissionManager.LoadAcl();
            Assert(SecurityLogger.Events.Skip(logsBeforeAliceLoad).Any(e => e.Contains("Blocked unauthorized PermissionManager.LoadAcl()")),
                "non-root LoadAcl must be blocked");

            // Privilege escalation / settings / baseline / forged integrity lease denials as alice
            Assert(!UserManager.CreateUser("bob", AlicePass1), "non-root CreateUser must fail");
            Assert(!UserManager.ChangePassword("root", AlicePass1, AttackPass), "non-root reset of root password must fail");
            Assert(!IntegrityGuard.UpdateBaseline(), "non-root UpdateBaseline must fail");
            Assert(!IntegrityGuard.RecoverBaseline(out _), "non-root RecoverBaseline must fail");
            Assert(!IntegrityGuard.NotifyManagedFileSaved("/etc/zonderq/settings.conf"),
                "non-root NotifyManagedFileSaved must fail");

            int origAutoLock = SystemSettings.AutoLockMinutes;
            bool origBootToGui = SystemSettings.BootToGui;
            SystemSettings.SetBootToGui(!origBootToGui);
            Assert(SystemSettings.BootToGui == origBootToGui, "failed non-root SetBootToGui must not mutate state");
            SystemSettings.CycleAutoLockTimeout();
            Assert(SystemSettings.AutoLockMinutes == origAutoLock, "failed non-root CycleAutoLockTimeout must not mutate state");
            Assert(!SystemSettings.Save(), "non-root SystemSettings.Save must fail");

            Assert(!IntegrityGuard.TryBeginManagedFilesUpdate(
                    ManagedOperationKind.PasswordChange,
                    new[] { "/etc/shadow" },
                    out _),
                "direct PasswordChange lease outside UserManager context must fail");
            Assert(!IntegrityGuard.TryBeginManagedFilesUpdate(
                    ManagedOperationKind.AccountCreation,
                    new[] { "/etc/passwd", "/etc/shadow" },
                    out _),
                "direct AccountCreation lease outside UserManager context must fail");
            Assert(!IntegrityGuard.TryBeginManagedFilesUpdate(
                    ManagedOperationKind.AclSave,
                    new[] { "/etc/acl.map" },
                    out _),
                "direct AclSave lease outside PermissionManager context must fail");

            // Self password change & admin reset
            Assert(!UserManager.ChangePassword("alice", BadPass, AlicePass2), "alice wrong old password must fail");
            Assert(UserManager.ChangePassword("alice", AlicePass1, AlicePass2), "alice own password change must succeed");
            AssertZeroViolations("post-alice-self-password-change");

            UserManager.EndSession();
            UserManager.PrepareLogin();
            Assert(UserManager.TryStartSession("root", RootPass2), "root re-login must succeed");
            Assert(!UserManager.ChangePassword("alice", BadPass, AlicePass3), "root admin reset without valid root confirmation must fail");
            Assert(UserManager.ChangePassword("alice", RootPass2, AlicePass3), "root admin reset for alice must succeed");
            AssertZeroViolations("post-admin-reset-alice");
            Assert(!UserManager.ValidateCredentials("alice", AlicePass2), "alice old password must fail after admin reset");
            Assert(UserManager.ValidateCredentials("alice", AlicePass3), "alice new password must work after admin reset");

            ClearLogs();
            SimulateBoot();
            AssertZeroViolations("reboot-after-user-and-reset");
            Assert(UserManager.TryStartSession("alice", AlicePass3), "alice login after reboot must succeed");
            UserManager.EndSession();
            UserManager.PrepareLogin();
            Assert(UserManager.TryStartSession("root", RootPass2), "root re-login after reboot must succeed");

            // Legacy credential migration
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

            // SystemSettings fresh save + tamper + delete detection
            UserManager.EndSession();
            UserManager.PrepareLogin();
            Assert(UserManager.TryStartSession("root", RootPass2), "root login before settings test must succeed");
            SystemSettings.CycleAutoLockTimeout(); // 5 -> 15
            Assert(SystemSettings.AutoLockMinutes == 15, "root CycleAutoLockTimeout must advance to 15");
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

            // Case 12: Unauthorized modification of shadow / passwd / acl.map
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

            // Case 10: Baseline write failure after password change, CreateUser, swap failure, Settings.Save
            Assert(!IntegrityGuard.NotifyManagedFileSaved("/etc/shadow"), "NotifyManagedFileSaved must reject /etc/shadow");
            Assert(!IntegrityGuard.NotifyManagedFileSaved("/etc/passwd"), "NotifyManagedFileSaved must reject /etc/passwd");
            Assert(!IntegrityGuard.NotifyManagedFileSaved("/etc/acl.map"), "NotifyManagedFileSaved must reject /etc/acl.map");
            Assert(!IntegrityGuard.NotifyManagedFileSaved("/etc/hostname"), "NotifyManagedFileSaved must reject /etc/hostname");
            Assert(!IntegrityGuard.NotifyManagedFileSaved("/etc/firewall.cfg"), "NotifyManagedFileSaved must reject /etc/firewall.cfg");

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
            SystemSettings.CycleAutoLockTimeout(); // 15 -> 30 in memory before failed save
            Assert(!SystemSettings.Save(), "SystemSettings.Save must fail and rollback if integrity sync fails");
            IntegrityGuard.SimulateDatabaseWriteFailure = false;
            Assert(File.ReadAllText(settingsDisk).Contains("auto_lock_minutes=15", StringComparison.Ordinal),
                "settings.conf must retain previous contents after rollback");
            AssertZeroViolations("after-settings-rollback");

            // Case 11: Crash recovery of .bak + .new AND pre-commit active-vs-.bak divergence across reboot
            string validPasswd = File.ReadAllText(Disk(root, "/etc/passwd"));
            string validShadow = File.ReadAllText(Disk(root, "/etc/shadow"));
            string validAcl = File.ReadAllText(Disk(root, "/etc/acl.map"));
            string validSettings = File.ReadAllText(Disk(root, "/etc/zonderq/settings.conf"));
            string validDb = File.ReadAllText(Disk(root, "/etc/integrity.db"));

            // 11a: Interrupted post-move / pre-commit divergence where BOTH active (uncommitted) and .bak (committed) exist
            File.WriteAllText(Disk(root, "/etc/shadow.bak"), validShadow);
            File.WriteAllText(Disk(root, "/etc/shadow"), validShadow + "uncommitted:x\n");
            File.WriteAllText(Disk(root, "/etc/shadow.new"), "corrupted-partial-shadow\n");

            File.WriteAllText(Disk(root, "/etc/passwd.bak"), validPasswd);
            File.WriteAllText(Disk(root, "/etc/passwd"), validPasswd + "uncommitted:x:1999:/home/uncommitted\n");
            File.WriteAllText(Disk(root, "/etc/passwd.new"), "partial-passwd\n");

            File.WriteAllText(Disk(root, "/etc/acl.map.bak"), validAcl);
            File.WriteAllText(Disk(root, "/etc/acl.map"), validAcl + "/tmp/uncommitted|alice|700\n");
            File.WriteAllText(Disk(root, "/etc/acl.map.new"), "partial-acl\n");

            File.WriteAllText(Disk(root, "/etc/zonderq/settings.conf.bak"), validSettings);
            File.WriteAllText(settingsDisk, "schema=1\nauto_lock_minutes=0\n");
            File.WriteAllText(Disk(root, "/etc/zonderq/settings.conf.new"), "partial-settings\n");

            File.WriteAllText(Disk(root, "/etc/integrity.db.bak"), validDb);
            File.WriteAllText(Disk(root, "/etc/integrity.db"), string.Empty); // 0-byte truncated active DB
            File.WriteAllText(Disk(root, "/etc/integrity.db.new"), "half-written-db\n");

            SimulateBoot();
            AssertZeroViolations("after-interrupted-swap-divergence-recovery");
            Assert(SystemSettings.AutoLockMinutes == 15, "settings swap rollback should restore auto_lock_minutes=15");
            Assert(!File.Exists(Disk(root, "/etc/integrity.db.new")) && !File.Exists(Disk(root, "/etc/shadow.new")) &&
                   !File.Exists(Disk(root, "/etc/passwd.new")) && !File.Exists(Disk(root, "/etc/acl.map.new")) &&
                   !File.Exists(Disk(root, "/etc/zonderq/settings.conf.new")),
                "orphaned .new files must be cleaned up during reboot recovery");
            Assert(UserManager.TryStartSession("root", RootPass2), "root login after divergence crash recovery must succeed");

            // Missing companion /etc/shadow on initialized system MUST NOT regenerate factory root:root
            string backupShadow = File.ReadAllText(Disk(root, "/etc/shadow"));
            File.Delete(Disk(root, "/etc/shadow"));
            SimulateBoot();
            Assert(!File.Exists(Disk(root, "/etc/shadow")),
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

            // Cases 5, 6, 7, 8, 9: IntegrityGuard.Initialize() failure, deletion, backup, corruption & orphaned .new states
            // Case 5: First write failure of integrity.db
            RunIsolated("zq-ig-case5-firstwrite", iso =>
            {
                ClearLogs();
                SecurityContext.ResetToBootStateForTests();
                IntegrityGuard.ResetForTests();
                SystemSettings.ResetForTests();
                UserManager.Initialize();
                PermissionManager.Initialize();
                IntegrityGuard.SimulateDatabaseWriteFailure = true;
                IntegrityGuard.Initialize();
                IntegrityGuard.SimulateDatabaseWriteFailure = false;

                Assert(!IntegrityGuard.IsInitialized, "first DB write failure must NOT mark IntegrityGuard initialized");
                Assert(IntegrityGuard.InitializationState == IntegrityInitializationState.Failed,
                    "first DB write failure must set InitializationState == Failed");
                Assert(IntegrityGuard.HasInitializationError && !string.IsNullOrEmpty(IntegrityGuard.InitializationError),
                    "first DB write failure must expose non-empty InitializationError");
                Assert(!File.Exists(Disk(iso, "/etc/zonderq/.integrity-sealed")),
                    "sealed marker must not exist when initial integrity.db creation failed");

                UserManager.PrepareLogin();
                Assert(!UserManager.CompleteInitialRootPasswordSetup(RootPass1),
                    "initial root setup must fail closed when IntegrityGuard failed to initialize");
                Assert(!PermissionManager.SetPermission("/tmp/denied.txt", "root", 644),
                    "SetPermission must fail closed when IntegrityGuard failed to initialize");

                // Next clean boot before installation was sealed can create initial baseline cleanly
                ClearLogs();
                SimulateBoot();
                AssertZeroViolations("case5-clean-boot-after-failed-first-write");
            }, root);

            // Case 6, 7, 8, 9 on isolated sealed installed systems
            RunIsolated("zq-ig-case6-9-db-states", iso =>
            {
                ClearLogs();
                SimulateBoot();
                Assert(UserManager.CompleteInitialRootPasswordSetup(RootPass1), "seed initial root setup");
                Assert(UserManager.TryStartSession("root", RootPass1), "seed root login");
                Assert(UserManager.CreateUser("alice", AlicePass1), "seed alice");
                AssertZeroViolations("seed-case6-9");

                string goldenDb = File.ReadAllText(Disk(iso, "/etc/integrity.db"));
                string goldenShadow = File.ReadAllText(Disk(iso, "/etc/shadow"));

                // Case 6: Deleted integrity.db & .bak after installation MUST NOT auto-create baseline
                File.Delete(Disk(iso, "/etc/integrity.db"));
                if (File.Exists(Disk(iso, "/etc/integrity.db.bak")))
                    File.Delete(Disk(iso, "/etc/integrity.db.bak"));
                File.AppendAllText(Disk(iso, "/etc/shadow"), "evil:v2$fake\n");
                SimulateBoot();
                Assert(!IntegrityGuard.IsInitialized && IntegrityGuard.InitializationState == IntegrityInitializationState.Failed,
                    "deleted integrity.db on installed system MUST enter Failed state");
                Assert(IntegrityGuard.HasInitializationError, "deleted integrity.db must flag HasInitializationError");
                Assert(!File.Exists(Disk(iso, "/etc/integrity.db")),
                    "deleted integrity.db MUST NOT be auto-recreated on existing system");
                Assert(UserManager.TryStartSession("root", RootPass1), "root authentication against valid entry still works for recovery");
                Assert(!UserManager.CreateUser("mallory", AttackPass),
                    "CreateUser must fail closed while IntegrityGuard is Failed");
                Assert(!IntegrityGuard.UpdateBaseline(),
                    "UpdateBaseline must refuse when IntegrityGuard failed to initialize");
                Assert(!PermissionManager.SetPermission("/tmp/failclosed.txt", "root", 600),
                    "SetPermission must fail closed while IntegrityGuard is Failed");

                // Non-root cannot call RecoverBaseline; root can ONLY after admin checks/fixes files
                UserManager.EndSession();
                UserManager.PrepareLogin();
                Assert(UserManager.TryStartSession("alice", AlicePass1), "alice login");
                Assert(!IntegrityGuard.RecoverBaseline(out _), "non-root RecoverBaseline must fail");
                UserManager.EndSession();
                UserManager.PrepareLogin();
                Assert(UserManager.TryStartSession("root", RootPass1), "root re-login");

                // Restore golden shadow and test Case 7 & Case 9b together:
                // Case 7 + 9b: Corrupted active DB + valid .bak + poisoned .new + tampered shadow
                File.WriteAllText(Disk(iso, "/etc/shadow"), goldenShadow);
                File.WriteAllText(Disk(iso, "/etc/integrity.db.bak"), goldenDb);
                File.WriteAllText(Disk(iso, "/etc/integrity.db"), "TRUNCATED_CORRUPT_HEADER");
                File.WriteAllText(Disk(iso, "/etc/integrity.db.new"),
                    "# poisoned .new\n/etc/shadow|" + new string('0', 64) + "|600\n");
                File.WriteAllText(Disk(iso, "/etc/shadow"), goldenShadow + "rogue2:x\n"); // tampered AFTER .bak!

                SimulateBoot();
                Assert(IntegrityGuard.IsInitialized && !IntegrityGuard.HasInitializationError,
                    "corrupted primary DB must recover from valid .bak");
                Assert(!File.Exists(Disk(iso, "/etc/integrity.db.new")),
                    "orphaned .new must be deleted rather than trusted");
                IntegrityGuard.ScanAll();
                IntegrityGuard.GetReportSummary(out _, out _, out int mod7, out _, out _);
                Assert(mod7 == 1, $"recovering from .bak must NOT auto-accept tampered /etc/shadow (mod={mod7})");

                // Case 9a: Valid DB + poisoned .new -> .new discarded, valid baseline preserved
                ClearLogs();
                File.WriteAllText(Disk(iso, "/etc/shadow"), goldenShadow);
                File.WriteAllText(Disk(iso, "/etc/integrity.db"), goldenDb);
                File.WriteAllText(Disk(iso, "/etc/integrity.db.new"), "invalid\n");
                SimulateBoot();
                Assert(!File.Exists(Disk(iso, "/etc/integrity.db.new")), "orphaned .new alongside valid DB must be deleted");
                AssertZeroViolations("case9a-valid-db-plus-orphan-new");

                // Case 9c: Missing DB & .bak + orphaned poisoned .new on installed system -> .new deleted & Failed
                File.Delete(Disk(iso, "/etc/integrity.db"));
                if (File.Exists(Disk(iso, "/etc/integrity.db.bak")))
                    File.Delete(Disk(iso, "/etc/integrity.db.bak"));
                File.WriteAllText(Disk(iso, "/etc/integrity.db.new"), goldenDb);
                SimulateBoot();
                Assert(!File.Exists(Disk(iso, "/etc/integrity.db.new")) && !File.Exists(Disk(iso, "/etc/integrity.db")),
                    "orphaned .new without primary/.bak must be deleted and NEVER promoted to integrity.db");
                Assert(IntegrityGuard.InitializationState == IntegrityInitializationState.Failed && !IntegrityGuard.IsInitialized,
                    "missing DB with only orphaned .new must fail closed");

                // Case 8: Corrupted primary DB + corrupted .bak -> Failed state until root RecoverBaseline
                File.WriteAllText(Disk(iso, "/etc/integrity.db"), "corrupt-primary-123\n");
                File.WriteAllText(Disk(iso, "/etc/integrity.db.bak"), "/etc/shadow|nothex|999\n");
                SimulateBoot();
                Assert(IntegrityGuard.InitializationState == IntegrityInitializationState.Failed && !IntegrityGuard.IsInitialized,
                    "corrupt DB + corrupt .bak must enter Failed state");
                Assert(IntegrityGuard.HasInitializationError, "Failed state must set HasInitializationError");
                Assert(UserManager.TryStartSession("root", RootPass1), "root login for recovery");
                Assert(!UserManager.CreateUser("blockeduser", AlicePass1),
                    "CreateUser must fail closed when DB and backup are both corrupt");
                Assert(IntegrityGuard.RecoverBaseline(out string recErr),
                    $"root RecoverBaseline should succeed after restoring system state: {recErr}");
                AssertZeroViolations("case8-after-root-recover");
            }, root);

            // Case 14: Concurrent multi-threaded account, ACL, settings, and integrity scan operations
            ClearLogs();
            SimulateBoot();
            Assert(UserManager.TryStartSession("root", RootPass2), "root login before concurrency stress must succeed");
            const int ThreadCount = 4;
            Task[] tasks = new Task[ThreadCount];
            for (int t = 0; t < ThreadCount; t++)
            {
                int workerId = t;
                tasks[t] = Task.Run(() =>
                {
                    for (int iter = 0; iter < 4; iter++)
                    {
                        string uname = $"w{workerId}_{iter}";
                        Assert(UserManager.CreateUser(uname, WorkerPass1), $"concurrent CreateUser({uname}) failed");
                        string aclTarget = $"/tmp/concurrent-{workerId}-{iter}.txt";
                        Assert(PermissionManager.SetPermission(aclTarget, "root", 644),
                            $"concurrent SetPermission({aclTarget}) failed");
                        var got = PermissionManager.GetPermission(aclTarget);
                        Assert(got.Owner == "root" && got.Perms == 644, $"concurrent GetPermission mismatch on {aclTarget}");
                        IntegrityGuard.ScanAll();
                        if (workerId == 0 && iter == 1)
                        {
                            SystemSettings.CycleAutoLockTimeout();
                            Assert(SystemSettings.Save(), "concurrent root SystemSettings.Save failed");
                        }
                    }
                });
            }

            Assert(Task.WaitAll(tasks, TimeSpan.FromSeconds(25)),
                "Concurrent account/ACL/integrity tasks timed out (possible deadlock!)");
            AssertZeroViolations("post-concurrency-stress");
            SimulateBoot();
            AssertZeroViolations("reboot-after-concurrency-stress");
            Assert(UserManager.TryStartSession("w3_3", WorkerPass1), "concurrently created user login after reboot must succeed");

            // Case 15: Verify zero plaintext passwords anywhere in logs across the entire suite
            AssertNoSecretLeaks("end-of-suite");

            Console.WriteLine("[INTEGRITY-GUARD] PASS (15/15 P0 security, ACL, init, crash-recovery, concurrency scenarios)");
            return 0;
        }
        finally
        {
            IntegrityGuard.SimulateDatabaseWriteFailure = false;
            IntegrityGuard.SimulatedWriteFailurePath = null;
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
