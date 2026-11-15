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

        private static string ToDiskPath(string logicalPath)
        {
#if ZIG_HOST_TESTS
            string root = Environment.GetEnvironmentVariable("ZIG_TEST_ROOT");
            if (!string.IsNullOrEmpty(root))
                return Path.Combine(root, logicalPath.TrimStart('/'));
#endif
            return logicalPath;
        }

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

        public static bool EnsureProfile(string home)
        {
            if (!SecurityContext.IsAuthenticated)
                return false;

            return EnsureProfile(SecurityContext.CurrentUser, home);
        }

        public static bool EnsureProfile(string owner, string home)
        {
            if (!IsValidUsername(owner) || !IsSafeHome(home))
                return false;

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
                // keeps ownership in PermissionManager. Commit all profile ACLs atomically
                // in one guarded transaction rather than partial per-folder writes.
                return PermissionManager.EnsureUserProfileAclsInternal(
                    owner,
                    home,
                    new[]
                    {
                        home,
                        desktop,
                        documents,
                        downloads,
                        pictures,
                        config,
                        zonderqConfig
                    });
            }
            catch (Exception ex)
            {
                SecurityLogger.LogEvent("WARN", "Profile initialization failed for '" + home + "': " + ex.Message);
                return false;
            }
        }

        public static void RememberLastUser(string username)
        {
            if (!IsValidUsername(username))
                return;

            try
            {
                string configDisk = ToDiskPath(ConfigDirectory);
                if (!Directory.Exists(configDisk))
                    Directory.CreateDirectory(configDisk);

                File.WriteAllText(ToDiskPath(LastUserPath), username);
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
                string lastUserDisk = ToDiskPath(LastUserPath);
                if (!File.Exists(lastUserDisk))
                    return null;

                string username = File.ReadAllText(lastUserDisk).Trim();
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
            string diskPath = ToDiskPath(path);
            if (!Directory.Exists(diskPath))
                Directory.CreateDirectory(diskPath);
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
