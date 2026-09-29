using System;
using System.IO;

namespace ZonderqOS.SystemCore
{
    /// <summary>Small, persistent /etc files used by the console and shell.</summary>
    public static class SystemIdentity
    {
        private const string HostnamePath = "/etc/hostname";
        private const string HostnameTempPath = "/etc/hostname.new";
        private const string HostnameBackupPath = "/etc/hostname.bak";
        private const string IssuePath = "/etc/issue";
        private const string MotdPath = "/etc/motd";
        private const string OsReleasePath = "/etc/os-release";

        public static void Initialize()
        {
            try
            {
                Directory.CreateDirectory("/etc");
                if (!File.Exists(HostnamePath) && File.Exists(HostnameBackupPath))
                    File.Move(HostnameBackupPath, HostnamePath);
                if (File.Exists(HostnameTempPath))
                    File.Delete(HostnameTempPath);

                if (!File.Exists(HostnamePath))
                {
                    string previous = EnvironmentManager.Get("HOSTNAME");
                    File.WriteAllText(HostnamePath, IsValidHostname(previous) ? previous + "\n" : "ZonderqOS\n");
                }
                if (!File.Exists(IssuePath))
                    File.WriteAllText(IssuePath, "ZonderqOS Gen 3\n");
                if (!File.Exists(MotdPath))
                    File.WriteAllText(MotdPath, "Welcome to ZonderqOS Gen 3. Type help for commands.\n");
                if (!File.Exists(OsReleasePath))
                    File.WriteAllText(OsReleasePath,
                        "NAME=\"ZonderqOS\"\nID=zonderqos\nVERSION_ID=\"3\"\nPRETTY_NAME=\"ZonderqOS Gen 3\"\n");

                PermissionManager.SetPermission(HostnamePath, "root", 644);
                PermissionManager.SetPermission(IssuePath, "root", 644);
                PermissionManager.SetPermission(MotdPath, "root", 644);
                PermissionManager.SetPermission(OsReleasePath, "root", 644);

                string error;
                if (!ReloadHostname(out error))
                    SystemLogger.Log(SystemLogLevel.Warning, "CONFIG", error);
            }
            catch (Exception ex)
            {
                SystemLogger.Log(SystemLogLevel.Warning, "CONFIG", "System identity setup failed: " + ex.Message);
            }
        }

        public static bool ReloadHostname(out string error)
        {
            error = string.Empty;
            try
            {
                string value = ReadBounded(HostnamePath, 64).Trim();
                if (!IsValidHostname(value))
                {
                    error = "Invalid /etc/hostname; use 1-63 ASCII letters, digits or hyphens.";
                    return false;
                }
                EnvironmentManager.Set("HOSTNAME", value);
                return true;
            }
            catch (Exception ex)
            {
                error = "Cannot read /etc/hostname: " + ex.Message;
                return false;
            }
        }

        public static bool SetHostname(string value, out string error)
        {
            error = string.Empty;
            if (!SecurityContext.IsAuthenticated || SecurityContext.CurrentUid != 0)
            {
                error = "Only authenticated root can change hostname.";
                return false;
            }
            if (!IsValidHostname(value))
            {
                error = "Invalid hostname; use 1-63 ASCII letters, digits or hyphens.";
                return false;
            }

            try
            {
                File.WriteAllText(HostnameTempPath, value + "\n");
                if (File.Exists(HostnameBackupPath))
                    File.Delete(HostnameBackupPath);
                if (File.Exists(HostnamePath))
                    File.Move(HostnamePath, HostnameBackupPath);
                try
                {
                    File.Move(HostnameTempPath, HostnamePath);
                }
                catch
                {
                    if (!File.Exists(HostnamePath) && File.Exists(HostnameBackupPath))
                        File.Move(HostnameBackupPath, HostnamePath);
                    throw;
                }

                PermissionManager.SetPermission(HostnamePath, "root", 644);
                EnvironmentManager.Set("HOSTNAME", value);
                return true;
            }
            catch (Exception ex)
            {
                error = "Cannot save hostname: " + ex.Message;
                return false;
            }
        }

        public static string ReadIssue()
        {
            try { return ReadBounded(IssuePath, 4096); }
            catch { return string.Empty; }
        }

        public static string ReadMotd()
        {
            try { return ReadBounded(MotdPath, 4096); }
            catch { return string.Empty; }
        }

        public static bool IsValidHostname(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 63 ||
                value[0] == '-' || value[value.Length - 1] == '-')
                return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                      (c >= '0' && c <= '9') || c == '-'))
                    return false;
            }
            return true;
        }

        private static string ReadBounded(string path, int maxCharacters)
        {
            char[] characters = new char[maxCharacters + 1];
            using (var reader = new StreamReader(path))
            {
                int count = 0;
                while (count < characters.Length)
                {
                    int read = reader.Read(characters, count, characters.Length - count);
                    if (read == 0)
                        break;
                    count += read;
                }
                if (count > maxCharacters)
                    throw new InvalidOperationException("Configuration file is too large.");
                return new string(characters, 0, count);
            }
        }
    }
}
