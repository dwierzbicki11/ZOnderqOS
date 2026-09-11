using System;
using System.IO;

namespace ZonderqOS
{
    public static class UserManager
    {
        private static readonly string PasswdPath = @"/etc/passwd";
        private static readonly string ShadowPath = @"/etc/shadow";

        public static void Initialize()
        {
            try
            {
                if (!Directory.Exists("/etc"))
                    Directory.CreateDirectory("/etc");

                // Never overwrite an existing account database just because its companion
                // file is missing. Each database is initialized independently.
                if (!File.Exists(PasswdPath))
                    File.WriteAllText(PasswdPath, "root:x:0:/root\n");

                if (!File.Exists(ShadowPath))
                {
                    string salt = Crypto.GenerateSalt();
                    string hash = Crypto.HashPassword("root", salt);
                    File.WriteAllText(ShadowPath, "root:" + salt + "$" + hash + "\n");
                }
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError("UserManager init failed: " + ex.Message, "AUTH");
            }
        }

        public static bool RequiresInitialRootPasswordSetup()
        {
            try
            {
                return UserExists("root") && ValidateCredentials("root", "root");
            }
            catch
            {
                return false;
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

            if (!RequiresInitialRootPasswordSetup())
                return false;

            if (!RewritePasswordEntry("root", newPassword))
                return false;

            AuthenticationGuard.Reset("root");
            SecurityLogger.LogEvent("INFO", "Initial root credential was replaced during secure setup.");
            return true;
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

        public static bool UserExists(string username)
        {
            if (!IsValidUsername(username))
                return false;

            try
            {
                if (!File.Exists(PasswdPath))
                    return false;

                string[] lines = File.ReadAllLines(PasswdPath);
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

        public static bool ValidateCredentials(string username, string password)
        {
            if (!IsValidUsername(username) || password == null)
                return false;

            try
            {
                if (!File.Exists(ShadowPath))
                    return false;

                string[] lines = File.ReadAllLines(ShadowPath);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    int separator = string.IsNullOrEmpty(line) ? -1 : line.IndexOf(':');
                    if (separator <= 0 || line.Substring(0, separator) != username)
                        continue;

                    string credential = line.Substring(separator + 1);
                    int saltSeparator = credential.IndexOf('$');
                    if (saltSeparator <= 0 || saltSeparator >= credential.Length - 1)
                        return false;

                    string salt = credential.Substring(0, saltSeparator);
                    string storedHash = credential.Substring(saltSeparator + 1);
                    string computedHash = Crypto.HashPassword(password, salt);
                    return Crypto.FixedTimeEquals(storedHash, computedHash);
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
            UserProfileManager.EnsureProfile(home);
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
            UserProfileManager.EnsureProfile(home);
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

            try
            {
                if (!File.Exists(PasswdPath))
                    return false;

                string[] lines = File.ReadAllLines(PasswdPath);
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

        public static bool CreateUser(string username, string password)
        {
            string passwordError;
            if (!SecurityContext.IsAuthenticated || SecurityContext.CurrentUid != 0 ||
                !IsValidUsername(username) || !PasswordPolicy.Validate(password, out passwordError))
            {
                return false;
            }

            try
            {
                if (UserExists(username))
                    return false;

                int uid = FindNextUid();
                if (uid < 1000)
                    return false;

                string homeDir = "/home/" + username;
                if (!Directory.Exists(homeDir))
                    Directory.CreateDirectory(homeDir);

                string salt = Crypto.GenerateSalt();
                string hash = Crypto.HashPassword(password, salt);
                string passwdEntry = username + ":x:" + uid + ":" + homeDir + "\n";
                string shadowEntry = username + ":" + salt + "$" + hash + "\n";

                File.AppendAllText(PasswdPath, passwdEntry);
                File.AppendAllText(ShadowPath, shadowEntry);
                UserProfileManager.EnsureProfile(homeDir);
                SecurityLogger.LogEvent("INFO", "Local user '" + username + "' created by '" +
                    SecurityContext.CurrentUser + "'.");
                return true;
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError("CreateUser exception: " + ex.Message, "AUTH");
                return false;
            }
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

            if (!ValidateCredentials(actor, authorizationPassword))
            {
                SecurityLogger.LogEvent("WARN", "Password change authorization failed for '" + actor + "'.");
                return false;
            }

            if (!RewritePasswordEntry(username, newPassword))
                return false;

            AuthenticationGuard.Reset(username);
            SecurityLogger.LogEvent("INFO", "Password changed for '" + username + "' by '" + actor + "'.");
            return true;
        }

        public static string GetHomeDirectory(string username)
        {
            int uid;
            string home;
            return TryGetUserInfo(username, out uid, out home) ? home : "/root";
        }

        private static bool RewritePasswordEntry(string username, string newPassword)
        {
            try
            {
                if (!File.Exists(ShadowPath))
                    return false;

                string[] lines = File.ReadAllLines(ShadowPath);
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
                    return false;

                string salt = Crypto.GenerateSalt();
                string hash = Crypto.HashPassword(newPassword, salt);
                lines[entryIndex] = username + ":" + salt + "$" + hash;
                File.WriteAllLines(ShadowPath, lines);
                return true;
            }
            catch (Exception ex)
            {
                SecurityLogger.LogEvent("ERR", "Password database update failed for '" + username + "': " + ex.Message);
                return false;
            }
        }

        private static int FindNextUid()
        {
            int nextUid = 1000;
            try
            {
                if (!File.Exists(PasswdPath))
                    return nextUid;

                string[] lines = File.ReadAllLines(PasswdPath);
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
