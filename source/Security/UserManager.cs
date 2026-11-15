using System;
using System.IO;
using ZonderqOS.Security;

namespace ZonderqOS
{
    public static class UserManager
    {
        private const string CredentialVersion = "v2";
        private static readonly string PasswdPath = @"/etc/passwd";
        private static readonly string PasswdTempPath = @"/etc/passwd.new";
        private static readonly string PasswdBackupPath = @"/etc/passwd.bak";
        private static readonly string ShadowPath = @"/etc/shadow";
        private static readonly string ShadowTempPath = @"/etc/shadow.new";
        private static readonly string ShadowBackupPath = @"/etc/shadow.bak";
        private const string IntegrityDbPath = "/etc/integrity.db";
        private const string IntegrityDbBackupPath = "/etc/integrity.db.bak";

        private static readonly object _accountLock = new object();

        private static string ToDiskPath(string logicalPath)
        {
#if ZIG_HOST_TESTS
            string root = Environment.GetEnvironmentVariable("ZIG_TEST_ROOT");
            if (!string.IsNullOrEmpty(root))
                return Path.Combine(root, logicalPath.TrimStart('/'));
#endif
            return logicalPath;
        }

        public static void Initialize()
        {
            lock (_accountLock)
            {
                try
                {
                    string etcDisk = ToDiskPath("/etc");
                    if (!Directory.Exists(etcDisk))
                        Directory.CreateDirectory(etcDisk);

                    string passwdDisk = ToDiskPath(PasswdPath);
                    string passwdTempDisk = ToDiskPath(PasswdTempPath);
                    string passwdBakDisk = ToDiskPath(PasswdBackupPath);
                    string shadowDisk = ToDiskPath(ShadowPath);
                    string shadowTempDisk = ToDiskPath(ShadowTempPath);
                    string shadowBakDisk = ToDiskPath(ShadowBackupPath);

                    // Recover complete account files if power was lost during an atomic swap.
                    RecoverInterruptedSwap(passwdDisk, passwdTempDisk, passwdBakDisk);
                    RecoverInterruptedSwap(shadowDisk, shadowTempDisk, shadowBakDisk);

                    bool integrityInitialized =
                        File.Exists(ToDiskPath(IntegrityDbPath)) ||
                        File.Exists(ToDiskPath(IntegrityDbBackupPath));

                    // Never overwrite an existing account database just because its companion
                    // file is missing, and never regenerate factory root credentials on an
                    // already-provisioned system where integrity baselines exist.
                    if (!File.Exists(passwdDisk))
                    {
                        if (!integrityInitialized)
                            File.WriteAllText(passwdDisk, "root:x:0:/root\n");
                        else
                            SecurityLogger.LogEvent("CRITICAL",
                                "Missing /etc/passwd on initialized system; refusing to recreate default account database.");
                    }

                    if (!File.Exists(shadowDisk))
                    {
                        if (!integrityInitialized)
                            File.WriteAllText(shadowDisk, "root:" + BuildCredential("root") + "\n");
                        else
                            SecurityLogger.LogEvent("CRITICAL",
                                "Missing /etc/shadow on initialized system; refusing to regenerate factory root credential.");
                    }
                }
                catch (Exception ex)
                {
                    WriteMessage.WriteError("UserManager init failed: " + ex.Message, "AUTH");
                }
            }
        }

        private static void RecoverInterruptedSwap(string activeDisk, string tempDisk, string bakDisk)
        {
            if ((!File.Exists(activeDisk) || new FileInfo(activeDisk).Length == 0) &&
                File.Exists(bakDisk) && new FileInfo(bakDisk).Length > 0)
            {
                File.Copy(bakDisk, activeDisk, true);
                File.Delete(bakDisk);
            }

            // Partial temporary files are never authoritative across boots.
            if (File.Exists(tempDisk))
                File.Delete(tempDisk);

            if (File.Exists(activeDisk) && new FileInfo(activeDisk).Length > 0 && File.Exists(bakDisk))
                File.Delete(bakDisk);
        }

        public static bool RequiresInitialRootPasswordSetup()
        {
            lock (_accountLock)
            {
                try
                {
                    return UserExists("root") && VerifyCredentialsLocked("root", "root", allowLegacyUpgrade: false);
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Replaces the factory root/root credential before any interactive session exists.
        /// This path is intentionally valid only while the default credential is still active.
        /// </summary>
        public static bool CompleteInitialRootPasswordSetup(string newPassword)
        {
            string reason;
            if (!PasswordPolicy.Validate(newPassword, out reason))
                return false;

            lock (_accountLock)
            {
                if (!AreAccountDatabasesPresentLocked())
                {
                    SecurityLogger.LogEvent("ERR", "Initial root setup aborted: /etc/passwd or /etc/shadow is missing.");
                    return false;
                }

                if (!RequiresInitialRootPasswordSetup())
                    return false;

                if (!RewritePasswordEntryLocked("root", newPassword, ManagedOperationKind.InitialRootPasswordSetup))
                    return false;

                AuthenticationGuard.Reset("root");
                SecurityLogger.LogEvent("INFO", "Initial root credential was replaced during secure setup.");
                return true;
            }
        }

        private static bool IsValidUsername(string username)
        {
            if (string.IsNullOrEmpty(username) || username.Length > 32)
                return false;

            for (int i = 0; i < username.Length; i++)
            {
                char c = username[i];
                if (!((c >= 'a' && c <= 'z') ||
                      (c >= 'A' && c <= 'Z') ||
                      (c >= '0' && c <= '9') ||
                      c == '_' || c == '-'))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AreAccountDatabasesPresentLocked() =>
            File.Exists(ToDiskPath(PasswdPath)) && File.Exists(ToDiskPath(ShadowPath));

        public static bool UserExists(string username)
        {
            if (!IsValidUsername(username))
                return false;

            lock (_accountLock)
            {
                try
                {
                    string passwdDisk = ToDiskPath(PasswdPath);
                    if (!File.Exists(passwdDisk))
                        return false;

                    string[] lines = File.ReadAllLines(passwdDisk);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i];
                        int separator = string.IsNullOrEmpty(line) ? -1 : line.IndexOf(':');
                        if (separator > 0 && line.Substring(0, separator) == username)
                            return true;
                    }
                }
                catch (Exception ex)
                {
                    SecurityLogger.LogEvent("ERR", "Passwd file read error: " + ex.Message);
                }

                return false;
            }
        }

        public static bool ValidateCredentials(string username, string password)
        {
            if (!IsValidUsername(username) || password == null)
                return false;

            lock (_accountLock)
            {
                return VerifyCredentialsLocked(username, password, allowLegacyUpgrade: true);
            }
        }

        private static bool VerifyCredentialsLocked(string username, string password, bool allowLegacyUpgrade)
        {
            try
            {
                string shadowDisk = ToDiskPath(ShadowPath);
                if (!File.Exists(shadowDisk))
                    return false;

                string[] lines = File.ReadAllLines(shadowDisk);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    int separator = string.IsNullOrEmpty(line) ? -1 : line.IndexOf(':');
                    if (separator <= 0 || line.Substring(0, separator) != username)
                        continue;

                    string credential = line.Substring(separator + 1);
                    bool legacy;
                    bool valid = VerifyCredential(password, credential, out legacy);
                    if (!valid)
                        return false;

                    // Existing installations used salt$FNV. A successful authentication is
                    // the only moment we know the original password, so transparently replace
                    // that legacy entry with the versioned PBKDF2-HMAC-SHA256 representation.
                    if (legacy && allowLegacyUpgrade)
                        TryUpgradeLegacyCredentialLocked(username, password);
                    return true;
                }
            }
            catch (Exception ex)
            {
                SecurityLogger.LogEvent("ERR", "Shadow file read error: " + ex.Message);
            }

            return false;
        }

        public static bool TryStartSession(string username, string password)
        {
            if (!ValidateCredentials(username, password))
            {
                SecurityLogger.LogEvent("WARN", "Failed login attempt for '" + (username ?? "?") + "'.");
                return false;
            }

            int uid;
            string home;
            if (!TryGetUserInfo(username, out uid, out home))
            {
                SecurityLogger.LogEvent("ERR", "Authenticated user '" + username + "' is missing from passwd database.");
                return false;
            }

            SecurityContext.SetAuthenticated(username, home, uid);
            EnvironmentManager.Set("USER", username);
            EnvironmentManager.Set("HOME", home);
            UserProfileManager.EnsureProfile(username, home);
            UserProfileManager.RememberLastUser(username);
            SessionManager.BeginSession();
            SecurityLogger.LogEvent("INFO", "User '" + username + "' logged in.");
            return true;
        }

        internal static bool ActivateSession(string username)
        {
            int uid;
            string home;
            if (!TryGetUserInfo(username, out uid, out home))
                return false;

            SecurityContext.SetAuthenticated(username, home, uid);
            EnvironmentManager.Set("USER", username);
            EnvironmentManager.Set("HOME", home);
            UserProfileManager.EnsureProfile(username, home);
            UserProfileManager.RememberLastUser(username);
            SessionManager.BeginSession();
            return true;
        }

        public static void PrepareLogin()
        {
            SessionManager.EndSession();
            SecurityContext.EnterLoginState();
            EnvironmentManager.Set("USER", string.Empty);
            EnvironmentManager.Set("HOME", "/");
        }

        public static void EndSession()
        {
            string user = SecurityContext.CurrentUser;
            if (SecurityContext.IsAuthenticated)
                SecurityLogger.LogEvent("INFO", "User '" + user + "' logged out.");

            PrepareLogin();
        }

        public static bool TryGetUserInfo(string username, out int uid, out string home)
        {
            uid = -1;
            home = "/";
            if (!IsValidUsername(username))
                return false;

            lock (_accountLock)
            {
                try
                {
                    string passwdDisk = ToDiskPath(PasswdPath);
                    if (!File.Exists(passwdDisk))
                        return false;

                    string[] lines = File.ReadAllLines(passwdDisk);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string[] parts = lines[i].Split(':');
                        if (parts.Length < 4 || parts[0] != username)
                            continue;

                        int parsedUid;
                        if (!Int32.TryParse(parts[2], out parsedUid))
                            return false;

                        uid = parsedUid;
                        home = string.IsNullOrEmpty(parts[3]) ? "/" : parts[3];
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    SecurityLogger.LogEvent("ERR", "User info lookup failed: " + ex.Message);
                }

                return false;
            }
        }

        public static bool CreateUser(string username, string password)
        {
            string passwordError;
            if (!SecurityContext.IsAuthenticated ||
                SecurityContext.CurrentUid != 0 ||
                !string.Equals(SecurityContext.CurrentUser, "root", StringComparison.Ordinal) ||
                !IsValidUsername(username) ||
                !PasswordPolicy.Validate(password, out passwordError))
            {
                return false;
            }

            lock (_accountLock)
            {
                ManagedFileUpdateLease lease = null;
                string passwdDisk = ToDiskPath(PasswdPath);
                string passwdTempDisk = ToDiskPath(PasswdTempPath);
                string passwdBakDisk = ToDiskPath(PasswdBackupPath);
                string shadowDisk = ToDiskPath(ShadowPath);
                string shadowTempDisk = ToDiskPath(ShadowTempPath);
                string shadowBakDisk = ToDiskPath(ShadowBackupPath);

                try
                {
                    if (!AreAccountDatabasesPresentLocked())
                    {
                        SecurityLogger.LogEvent("ERR", "CreateUser aborted: missing /etc/passwd or /etc/shadow.");
                        return false;
                    }

                    if (UserExists(username))
                        return false;

                    int uid = FindNextUidLocked();
                    if (uid < 1000)
                        return false;

                    string homeDir = "/home/" + username;
                    string homeDisk = ToDiskPath(homeDir);
                    string credential = BuildCredential(password);
                    if (string.IsNullOrEmpty(credential))
                        return false;

                    if (IntegrityGuard.IsInitialized &&
                        !IntegrityGuard.TryBeginManagedFilesUpdate(
                            ManagedOperationKind.AccountCreation,
                            new[] { PasswdPath, ShadowPath },
                            out lease))
                    {
                        SecurityLogger.LogEvent("CRITICAL", "CreateUser aborted: account databases failed pre-write integrity verification.");
                        return false;
                    }

                    string oldPasswd = File.ReadAllText(passwdDisk);
                    string oldShadow = File.ReadAllText(shadowDisk);
                    string passwdEntry = username + ":x:" + uid + ":" + homeDir + "\n";
                    string shadowEntry = username + ":" + credential + "\n";

#if ZIG_HOST_TESTS
                    if (string.Equals(IntegrityGuard.SimulatedWriteFailurePath, PasswdPath, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(IntegrityGuard.SimulatedWriteFailurePath, PasswdTempPath, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException("Simulated /etc/passwd write failure.");
                    }
#endif
                    if (File.Exists(passwdTempDisk))
                        File.Delete(passwdTempDisk);
                    File.WriteAllText(passwdTempDisk, AppendLine(oldPasswd, passwdEntry));

#if ZIG_HOST_TESTS
                    if (string.Equals(IntegrityGuard.SimulatedWriteFailurePath, ShadowPath, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(IntegrityGuard.SimulatedWriteFailurePath, ShadowTempPath, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException("Simulated /etc/shadow write failure during CreateUser.");
                    }
#endif
                    if (File.Exists(shadowTempDisk))
                        File.Delete(shadowTempDisk);
                    File.WriteAllText(shadowTempDisk, AppendLine(oldShadow, shadowEntry));

                    if (File.Exists(passwdBakDisk)) File.Delete(passwdBakDisk);
                    if (File.Exists(shadowBakDisk)) File.Delete(shadowBakDisk);

                    bool passwdSwapped = false;
                    bool shadowSwapped = false;
                    try
                    {
                        File.Move(passwdDisk, passwdBakDisk);
                        File.Move(passwdTempDisk, passwdDisk);
                        passwdSwapped = true;

#if ZIG_HOST_TESTS
                        if (string.Equals(IntegrityGuard.SimulatedWriteFailurePath, ShadowPath + ".swap", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new IOException("Simulated /etc/shadow swap failure during CreateUser.");
                        }
#endif
                        File.Move(shadowDisk, shadowBakDisk);
                        File.Move(shadowTempDisk, shadowDisk);
                        shadowSwapped = true;
                    }
                    catch
                    {
                        if (!File.Exists(shadowDisk) && File.Exists(shadowBakDisk))
                            File.Move(shadowBakDisk, shadowDisk);
                        if (shadowSwapped && File.Exists(shadowBakDisk))
                        {
                            if (File.Exists(shadowDisk)) File.Delete(shadowDisk);
                            File.Move(shadowBakDisk, shadowDisk);
                        }
                        if (!File.Exists(passwdDisk) && File.Exists(passwdBakDisk))
                            File.Move(passwdBakDisk, passwdDisk);
                        if (passwdSwapped && File.Exists(passwdBakDisk))
                        {
                            if (File.Exists(passwdDisk)) File.Delete(passwdDisk);
                            File.Move(passwdBakDisk, passwdDisk);
                        }
                        throw;
                    }

                    if (lease != null && !IntegrityGuard.CommitManagedFilesUpdate(lease))
                    {
                        RestoreFromBackup(passwdDisk, passwdBakDisk);
                        RestoreFromBackup(shadowDisk, shadowBakDisk);
                        SecurityLogger.LogEvent("ERR", "CreateUser rolled back: integrity baseline commit failed.");
                        return false;
                    }

                    bool homeExisted = Directory.Exists(homeDisk);
                    if (!homeExisted)
                        Directory.CreateDirectory(homeDisk);

                    if (!UserProfileManager.EnsureProfile(username, homeDir))
                    {
                        RollbackCommittedAccountCreationLocked(oldPasswd, oldShadow, homeDisk, homeExisted);
                        SecurityLogger.LogEvent("ERR", "CreateUser rolled back: profile/ACL setup failed for '" + username + "'.");
                        return false;
                    }

                    if (File.Exists(passwdBakDisk)) File.Delete(passwdBakDisk);
                    if (File.Exists(shadowBakDisk)) File.Delete(shadowBakDisk);

                    SecurityLogger.LogEvent("INFO", "Local user '" + username + "' created by '" +
                        SecurityContext.CurrentUser + "'.");
                    return true;
                }
                catch (Exception ex)
                {
                    if (lease != null)
                        IntegrityGuard.CancelManagedFilesUpdate(lease);

                    try
                    {
                        if (File.Exists(passwdTempDisk)) File.Delete(passwdTempDisk);
                        if (File.Exists(shadowTempDisk)) File.Delete(shadowTempDisk);
                    }
                    catch { }

                    WriteMessage.WriteError("CreateUser exception: " + ex.Message, "AUTH");
                    SecurityLogger.LogEvent("ERR", "CreateUser failed for '" + username + "': " + ex.Message);
                    return false;
                }
            }
        }

        private static void RollbackCommittedAccountCreationLocked(
            string oldPasswd,
            string oldShadow,
            string homeDisk,
            bool homeExisted)
        {
            string passwdDisk = ToDiskPath(PasswdPath);
            string shadowDisk = ToDiskPath(ShadowPath);
            string passwdBakDisk = ToDiskPath(PasswdBackupPath);
            string shadowBakDisk = ToDiskPath(ShadowBackupPath);
            ManagedFileUpdateLease rollbackLease = null;

            try
            {
                if (!IntegrityGuard.IsInitialized ||
                    IntegrityGuard.TryBeginManagedFilesUpdate(
                        ManagedOperationKind.AccountCreation,
                        new[] { PasswdPath, ShadowPath },
                        out rollbackLease))
                {
                    File.WriteAllText(passwdDisk, oldPasswd);
                    File.WriteAllText(shadowDisk, oldShadow);
                    if (rollbackLease != null)
                        IntegrityGuard.CommitManagedFilesUpdate(rollbackLease);
                }
            }
            catch
            {
                if (rollbackLease != null)
                    IntegrityGuard.CancelManagedFilesUpdate(rollbackLease);
            }

            try
            {
                if (File.Exists(passwdBakDisk)) File.Delete(passwdBakDisk);
                if (File.Exists(shadowBakDisk)) File.Delete(shadowBakDisk);
                if (!homeExisted && Directory.Exists(homeDisk))
                    Directory.Delete(homeDisk, true);
            }
            catch { }
        }

        /// <summary>
        /// Changes a local account password only after authenticating the current session.
        /// A normal user may change only their own password. Root may reset another account,
        /// but must still confirm the root password before /etc/shadow is rewritten.
        /// </summary>
        public static bool ChangePassword(string username, string authorizationPassword, string newPassword)
        {
            string passwordError;
            if (!SecurityContext.IsAuthenticated || !IsValidUsername(username) ||
                authorizationPassword == null || !PasswordPolicy.Validate(newPassword, out passwordError))
            {
                return false;
            }

            string actor = SecurityContext.CurrentUser;
            if (!IsValidUsername(actor))
                return false;

            bool ownAccount = actor == username;
            bool rootReset = SecurityContext.CurrentUid == 0 && actor == "root";
            if (!ownAccount && !rootReset)
            {
                SecurityLogger.LogEvent("WARN", "User '" + actor + "' attempted to change password for '" +
                    username + "'.");
                return false;
            }

            lock (_accountLock)
            {
                if (!AreAccountDatabasesPresentLocked())
                {
                    SecurityLogger.LogEvent("ERR", "Password change aborted: /etc/passwd or /etc/shadow is missing.");
                    return false;
                }

                if (!UserExists(username))
                    return false;

                if (!VerifyCredentialsLocked(actor, authorizationPassword, allowLegacyUpgrade: true))
                {
                    SecurityLogger.LogEvent("WARN", "Password change authorization failed for '" + actor + "'.");
                    return false;
                }

                if (!RewritePasswordEntryLocked(username, newPassword, ManagedOperationKind.PasswordChange))
                    return false;

                AuthenticationGuard.Reset(username);
                SecurityLogger.LogEvent("INFO", "Password changed for '" + username + "' by '" + actor + "'.");
                return true;
            }
        }

        public static string GetHomeDirectory(string username)
        {
            int uid;
            string home;
            return TryGetUserInfo(username, out uid, out home) ? home : "/root";
        }

        private static bool VerifyCredential(string password, string credential, out bool legacy)
        {
            legacy = false;
            if (string.IsNullOrEmpty(credential))
                return false;

            if (credential.StartsWith(CredentialVersion + "$", StringComparison.Ordinal))
            {
                string[] parts = credential.Split('$');
                if (parts.Length != 4 || parts[0] != CredentialVersion ||
                    string.IsNullOrEmpty(parts[2]) || string.IsNullOrEmpty(parts[3]))
                {
                    return false;
                }

                int iterations;
                if (!Int32.TryParse(parts[1], out iterations) || iterations < 1 || iterations > 1000000)
                    return false;

                string computed = Crypto.HashPasswordV2(password, parts[2], iterations);
                bool ok = Crypto.FixedTimeEquals(parts[3], computed);
                computed = null;
                return ok;
            }

            int saltSeparator = credential.IndexOf('$');
            if (saltSeparator <= 0 || saltSeparator >= credential.Length - 1 ||
                credential.IndexOf('$', saltSeparator + 1) >= 0)
            {
                return false;
            }

            string salt = credential.Substring(0, saltSeparator);
            string storedHash = credential.Substring(saltSeparator + 1);
            string legacyHash = Crypto.HashPassword(password, salt);
            bool valid = Crypto.FixedTimeEquals(storedHash, legacyHash);
            legacyHash = null;
            legacy = valid;
            return valid;
        }

        private static string BuildCredential(string password)
        {
            string salt = Crypto.GenerateSalt();
            string hash = Crypto.HashPasswordV2(password, salt, Crypto.PasswordHashIterations);
            if (string.IsNullOrEmpty(hash))
                return null;

            return CredentialVersion + "$" + Crypto.PasswordHashIterations + "$" + salt + "$" + hash;
        }

        private static void TryUpgradeLegacyCredentialLocked(string username, string password)
        {
            try
            {
                if (!AreAccountDatabasesPresentLocked())
                {
                    SecurityLogger.LogEvent("WARN", "Credential upgrade skipped for '" + username + "': incomplete account database.");
                    return;
                }

                string credential = BuildCredential(password);
                if (!string.IsNullOrEmpty(credential) &&
                    ReplaceShadowCredentialLocked(username, credential, ManagedOperationKind.LegacyCredentialUpgrade))
                {
                    SecurityLogger.LogEvent("INFO", "Credential hash upgraded for local user '" + username + "'.");
                }
                else
                {
                    SecurityLogger.LogEvent("WARN", "Credential upgrade aborted for '" + username + "': shadow or integrity sync failed.");
                }
            }
            catch (Exception ex)
            {
                // Authentication remains valid even if migration cannot be persisted. The
                // next successful login can retry the upgrade instead of locking the user out.
                SecurityLogger.LogEvent("WARN", "Credential upgrade failed for '" + username + "': " + ex.Message);
            }
        }

        private static bool RewritePasswordEntryLocked(
            string username,
            string newPassword,
            ManagedOperationKind operation)
        {
            try
            {
                string credential = BuildCredential(newPassword);
                if (string.IsNullOrEmpty(credential))
                    return false;
                return ReplaceShadowCredentialLocked(username, credential, operation);
            }
            catch (Exception ex)
            {
                SecurityLogger.LogEvent("ERR", "Password database update failed for '" + username + "': " + ex.Message);
                return false;
            }
        }

        private static bool ReplaceShadowCredentialLocked(
            string username,
            string credential,
            ManagedOperationKind operation)
        {
            string shadowDisk = ToDiskPath(ShadowPath);
            string shadowTempDisk = ToDiskPath(ShadowTempPath);
            string shadowBakDisk = ToDiskPath(ShadowBackupPath);
            ManagedFileUpdateLease lease = null;

            try
            {
                if (!AreAccountDatabasesPresentLocked())
                    return false;

                if (IntegrityGuard.IsInitialized &&
                    !IntegrityGuard.TryBeginManagedFilesUpdate(operation, new[] { ShadowPath }, out lease))
                {
                    SecurityLogger.LogEvent("CRITICAL", "Refused /etc/shadow update: pre-write integrity verification failed.");
                    return false;
                }

                string[] lines = File.ReadAllLines(shadowDisk);
                int entryIndex = -1;
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    int separator = string.IsNullOrEmpty(line) ? -1 : line.IndexOf(':');
                    if (separator <= 0)
                        continue;

                    if (line.Substring(0, separator) == username)
                    {
                        entryIndex = i;
                        break;
                    }
                }

                if (entryIndex < 0)
                {
                    if (lease != null)
                        IntegrityGuard.CancelManagedFilesUpdate(lease);
                    return false;
                }

                lines[entryIndex] = username + ":" + credential;

#if ZIG_HOST_TESTS
                if (string.Equals(IntegrityGuard.SimulatedWriteFailurePath, ShadowPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(IntegrityGuard.SimulatedWriteFailurePath, ShadowTempPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("Simulated /etc/shadow write failure.");
                }
#endif
                if (File.Exists(shadowTempDisk))
                    File.Delete(shadowTempDisk);

                File.WriteAllLines(shadowTempDisk, lines);

                if (File.Exists(shadowBakDisk))
                    File.Delete(shadowBakDisk);

                File.Move(shadowDisk, shadowBakDisk);
                try
                {
                    File.Move(shadowTempDisk, shadowDisk);
                }
                catch
                {
                    if (!File.Exists(shadowDisk) && File.Exists(shadowBakDisk))
                        File.Move(shadowBakDisk, shadowDisk);
                    throw;
                }

                if (lease != null && !IntegrityGuard.CommitManagedFilesUpdate(lease))
                {
                    RestoreFromBackup(shadowDisk, shadowBakDisk);
                    SecurityLogger.LogEvent("ERR", "Aborted /etc/shadow update: integrity baseline update failed.");
                    return false;
                }

                if (File.Exists(shadowBakDisk))
                    File.Delete(shadowBakDisk);

                return true;
            }
            catch (Exception ex)
            {
                if (lease != null)
                    IntegrityGuard.CancelManagedFilesUpdate(lease);

                try
                {
                    if (File.Exists(shadowTempDisk))
                        File.Delete(shadowTempDisk);
                }
                catch { }

                SecurityLogger.LogEvent("ERR", "Failed to replace shadow entry for '" + username + "': " + ex.Message);
                return false;
            }
        }

        private static void RestoreFromBackup(string activeDisk, string bakDisk)
        {
            if (!File.Exists(bakDisk))
                return;

            if (File.Exists(activeDisk))
                File.Delete(activeDisk);
            File.Move(bakDisk, activeDisk);
        }

        private static string AppendLine(string existing, string entry)
        {
            if (string.IsNullOrEmpty(existing))
                return entry;
            if (existing.EndsWith("\n", StringComparison.Ordinal))
                return existing + entry;
            return existing + "\n" + entry;
        }

        private static int FindNextUidLocked()
        {
            int nextUid = 1000;
            try
            {
                string passwdDisk = ToDiskPath(PasswdPath);
                if (!File.Exists(passwdDisk))
                    return -1;

                string[] lines = File.ReadAllLines(passwdDisk);
                for (int i = 0; i < lines.Length; i++)
                {
                    string[] parts = lines[i].Split(':');
                    if (parts.Length < 3)
                        continue;

                    int uid;
                    if (!Int32.TryParse(parts[2], out uid) || uid < 1000)
                        continue;

                    if (uid >= nextUid)
                    {
                        if (uid == Int32.MaxValue)
                            return -1;
                        nextUid = uid + 1;
                    }
                }
            }
            catch
            {
                return -1;
            }

            return nextUid;
        }
    }
}
