using System;
using System.IO;

namespace ZonderqOS
{
    /// <summary>
    /// Small per-user profile helper. Profile folders are created only when an account
    /// is created or a session starts. No filesystem work is performed from GUI render loops.
    /// </summary>
    public static class UserProfileManager
    {
        private const string ConfigDirectory = "/etc/zonderq";
        private const string LastUserPath = ConfigDirectory + "/last-user";

        public static string CurrentHome
        {
            get
            {
                if (!SecurityContext.IsAuthenticated)
                    return "/";

                string home = SecurityContext.CurrentHome;
                if (string.IsNullOrEmpty(home) || home[0] != '/')
                    return "/";

                return home;
            }
        }

        public static void EnsureProfile(string home)
        {
            string owner = SecurityContext.IsAuthenticated ? SecurityContext.CurrentUser : "root";
            EnsureProfile(owner, home);
        }

        public static void EnsureProfile(string owner, string home)
        {
            if (!IsValidUsername(owner) || !IsSafeHome(home))
                return;

            try
            {
                EnsureDirectory(home);
                string desktop = Append(home, "Desktop");
                string documents = Append(home, "Documents");
                string downloads = Append(home, "Downloads");
                string pictures = Append(home, "Pictures");
                string config = Append(home, ".config");
                string zonderqConfig = Append(home, ".config/zonderq");

                EnsureDirectory(desktop);
                EnsureDirectory(documents);
                EnsureDirectory(downloads);
                EnsureDirectory(pictures);
                EnsureDirectory(config);
                EnsureDirectory(zonderqConfig);

                // The VFS itself does not provide Unix ownership metadata, so ZOnderqOS
                // keeps ownership in PermissionManager. Without these ACLs a freshly-created
                // user's own home would fall back to root:600 and be unusable to that account.
                EnsurePermission(home, owner, 700);
                EnsurePermission(desktop, owner, 700);
                EnsurePermission(documents, owner, 700);
                EnsurePermission(downloads, owner, 700);
                EnsurePermission(pictures, owner, 700);
                EnsurePermission(config, owner, 700);
                EnsurePermission(zonderqConfig, owner, 700);
            }
            catch (Exception ex)
            {
                SecurityLogger.LogEvent("WARN", "Profile initialization failed for '" + home + "': " + ex.Message);
            }
        }

        public static void RememberLastUser(string username)
        {
            if (!IsValidUsername(username))
                return;

            try
            {
                if (!Directory.Exists(ConfigDirectory))
                    Directory.CreateDirectory(ConfigDirectory);

                File.WriteAllText(LastUserPath, username);
            }
            catch (Exception ex)
            {
                SecurityLogger.LogEvent("WARN", "Could not remember last login: " + ex.Message);
            }
        }

        public static string GetLastUser()
        {
            try
            {
                if (!File.Exists(LastUserPath))
                    return null;

                string username = File.ReadAllText(LastUserPath).Trim();
                if (!IsValidUsername(username))
                    return null;

                return UserManager.UserExists(username) ? username : null;
            }
            catch
            {
                return null;
            }
        }

        private static void EnsurePermission(string path, string owner, int permissions)
        {
            var current = PermissionManager.GetPermission(path);
            if (current.Owner == owner && current.Perms == permissions)
                return;

            PermissionManager.SetPermission(path, owner, permissions);
        }

        private static bool IsSafeHome(string home)
        {
            return !string.IsNullOrEmpty(home) && home.Length > 1 && home[0] == '/';
        }

        private static void EnsureDirectory(string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }

        private static string Append(string root, string child)
        {
            return root.EndsWith("/") ? root + child : root + "/" + child;
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
                    return false;
            }

            return true;
        }
    }
}
