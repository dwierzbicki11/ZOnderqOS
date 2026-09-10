using System;
using System.IO;

namespace ZonderqOS
{
    public static class UserManager
    {
        private static string PasswdPath = @"/etc/passwd";
        private static string ShadowPath = @"/etc/shadow";

        public static void Initialize()
        {
            try
            {
                if (!Directory.Exists("/etc"))
                    Directory.CreateDirectory("/etc");

                // Nie nadpisuj istniejącej bazy użytkowników tylko dlatego, że brakuje
                // jednego z plików. Każdy plik inicjalizujemy niezależnie.
                if (!File.Exists(PasswdPath))
                    File.WriteAllText(PasswdPath, "root:x:0:/root\n");

                if (!File.Exists(ShadowPath))
                {
                    string salt = Crypto.GenerateSalt();
                    string hash = Crypto.HashPassword("root", salt);
                    File.WriteAllText(ShadowPath, $"root:{salt}${hash}\n");
                }
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"UserManager init failed: {ex.Message}", "AUTH");
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

        public static bool UserExists(string username)
        {
            if (!IsValidUsername(username)) return false;

            try
            {
                if (!File.Exists(PasswdPath)) return false;
                string content = File.ReadAllText(PasswdPath);
                string[] lines = content.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    string[] parts = line.Split(':');
                    if (parts.Length > 0 && parts[0] == username)
                        return true;
                }
            }
            catch (Exception ex)
            {
                SecurityLogger.LogEvent("ERR", $"Passwd file read error: {ex.Message}");
            }

            return false;
        }

        public static bool ValidateCredentials(string username, string password)
        {
            if (!IsValidUsername(username) || password == null) return false;

            try
            {
                if (!File.Exists(ShadowPath)) return false;
                string content = File.ReadAllText(ShadowPath);
                string[] lines = content.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    string[] parts = line.Split(':');
                    if (parts.Length >= 2 && parts[0] == username)
                    {
                        string[] securityData = parts[1].Split('$');
                        if (securityData.Length == 2)
                        {
                            string salt = securityData[0];
                            string storedHash = securityData[1];
                            string computedHash = Crypto.HashPassword(password, salt);

                            return storedHash == computedHash;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SecurityLogger.LogEvent("ERR", $"Shadow file read error: {ex.Message}");
            }

            return false;
        }

        public static bool TryStartSession(string username, string password)
        {
            if (!ValidateCredentials(username, password))
            {
                SecurityLogger.LogEvent("WARN", $"Failed login attempt for '{username ?? "?"}'.");
                return false;
            }

            int uid;
            string home;
            if (!TryGetUserInfo(username, out uid, out home))
            {
                SecurityLogger.LogEvent("ERR", $"Authenticated user '{username}' is missing from passwd database.");
                return false;
            }

            SecurityContext.SetAuthenticated(username, home, uid);
            EnvironmentManager.Set("USER", username);
            EnvironmentManager.Set("HOME", home);
            SecurityLogger.LogEvent("INFO", $"User '{username}' logged in.");
            return true;
        }

        public static bool ActivateSession(string username)
        {
            int uid;
            string home;
            if (!TryGetUserInfo(username, out uid, out home))
                return false;

            SecurityContext.SetAuthenticated(username, home, uid);
            EnvironmentManager.Set("USER", username);
            EnvironmentManager.Set("HOME", home);
            return true;
        }

        public static void PrepareLogin()
        {
            SecurityContext.EnterLoginState();
            EnvironmentManager.Set("USER", string.Empty);
            EnvironmentManager.Set("HOME", "/");
        }

        public static void EndSession()
        {
            string user = SecurityContext.CurrentUser;
            if (SecurityContext.IsAuthenticated)
                SecurityLogger.LogEvent("INFO", $"User '{user}' logged out.");

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
                SecurityLogger.LogEvent("ERR", $"User info lookup failed: {ex.Message}");
            }

            return false;
        }

        public static bool CreateUser(string username, string password)
        {
            if (!IsValidUsername(username) || password == null || password.Length == 0 || password.Length > 128)
                return false;

            try
            {
                if (!UserExists(username))
                {
                    string homeDir = $"/home/{username}";
                    if (!Directory.Exists(homeDir)) Directory.CreateDirectory(homeDir);

                    string content = File.Exists(PasswdPath) ? File.ReadAllText(PasswdPath) : string.Empty;
                    int lineCount = content.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
                    int uid = 1000 + lineCount;

                    string salt = Crypto.GenerateSalt();
                    string hash = Crypto.HashPassword(password, salt);

                    string passwdEntry = $"{username}:x:{uid}:{homeDir}\n";
                    string shadowEntry = $"{username}:{salt}${hash}\n";

                    File.AppendAllText(PasswdPath, passwdEntry);
                    File.AppendAllText(ShadowPath, shadowEntry);
                    SecurityLogger.LogEvent("INFO", $"Local user '{username}' created by '{SecurityContext.CurrentUser}'.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"CreateUser exception: {ex.Message}", "AUTH");
            }

            return false;
        }

        /// <summary>
        /// Changes a local account password only after authenticating the current session.
        /// A normal user may change only their own password. Root may reset another account,
        /// but must still confirm the root password before /etc/shadow is rewritten.
        /// </summary>
        public static bool ChangePassword(string username, string authorizationPassword, string newPassword)
        {
            if (!SecurityContext.IsAuthenticated || !IsValidUsername(username) ||
                authorizationPassword == null || newPassword == null ||
                newPassword.Length == 0 || newPassword.Length > 128)
            {
                return false;
            }

            string actor = SecurityContext.CurrentUser;
            if (!IsValidUsername(actor))
                return false;

            bool ownAccount = actor == username;
            bool rootReset = actor == "root";
            if (!ownAccount && !rootReset)
            {
                SecurityLogger.LogEvent("WARN", $"User '{actor}' attempted to change password for '{username}'.");
                return false;
            }

            if (!ValidateCredentials(actor, authorizationPassword))
            {
                SecurityLogger.LogEvent("WARN", $"Password change authorization failed for '{actor}'.");
                return false;
            }

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
                SecurityLogger.LogEvent("INFO", $"Password changed for '{username}' by '{actor}'.");
                return true;
            }
            catch (Exception ex)
            {
                SecurityLogger.LogEvent("ERR", $"Password change failed for '{username}': {ex.Message}");
                return false;
            }
        }

        public static string GetHomeDirectory(string username)
        {
            int uid;
            string home;
            return TryGetUserInfo(username, out uid, out home) ? home : "/root";
        }
    }
}
