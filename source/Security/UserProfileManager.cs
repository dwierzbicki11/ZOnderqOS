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
            if (!IsSafeHome(home))
                return;

            try
            {
                EnsureDirectory(home);
                EnsureDirectory(Append(home, "Desktop"));
                EnsureDirectory(Append(home, "Documents"));
                EnsureDirectory(Append(home, "Downloads"));
                EnsureDirectory(Append(home, "Pictures"));
                EnsureDirectory(Append(home, ".config"));
                EnsureDirectory(Append(home, ".config/zonderq"));
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
