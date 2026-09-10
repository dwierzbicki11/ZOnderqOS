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

        public static bool CreateUser(string username, string password)
        {
            if (!IsValidUsername(username) || password == null || password.Length == 0)
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
                    return true;
                }
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"CreateUser exception: {ex.Message}", "AUTH");
            }

            return false;
        }

        public static string GetHomeDirectory(string username)
        {
            if (!IsValidUsername(username)) return "/root";

            try
            {
                if (!File.Exists(PasswdPath)) return "/root";
                string[] lines = File.ReadAllLines(PasswdPath);
                foreach (var line in lines)
                {
                    string[] parts = line.Split(':');
                    if (parts.Length >= 4 && parts[0] == username) return parts[3];
                }
            }
            catch (Exception ex)
            {
                SecurityLogger.LogEvent("ERR", $"Home directory lookup failed: {ex.Message}");
            }

            return "/root";
        }
    }
}