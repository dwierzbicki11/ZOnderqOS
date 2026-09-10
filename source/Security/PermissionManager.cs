using System;
using System.Collections.Generic;
using System.IO;

namespace ZonderqOS
{
    public static class PermissionManager
    {
        private static string AclPath = @"/etc/acl.map";
        private static readonly Dictionary<string, (string Owner, int Perms)> _aclCache = new Dictionary<string, (string, int)>();

        public static void Initialize()
        {
            if (!Directory.Exists("/etc")) Directory.CreateDirectory("/etc");

            if (!File.Exists(AclPath))
            {
                string defaultAcl =
                    "/etc/shadow|root|600\n" +
                    "/etc/passwd|root|644\n" +
                    "/var/log/auth.log|root|600\n" +
                    "/etc/profile|root|644\n";
                File.WriteAllText(AclPath, defaultAcl);
            }
            LoadAcl();
        }

        public static void LoadAcl()
        {
            _aclCache.Clear();
            try
            {
                string[] lines = File.ReadAllLines(AclPath);
                foreach (var line in lines)
                {
                    string[] parts = line.Split('|');
                    if (parts.Length == 3 &&
                        !string.IsNullOrEmpty(parts[0]) &&
                        !string.IsNullOrEmpty(parts[1]) &&
                        int.TryParse(parts[2], out int perms) &&
                        perms >= 0 && perms <= 777)
                    {
                        _aclCache[parts[0]] = (parts[1], perms);
                    }
                }
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"ACL load failed: {ex.Message}", "SEC");
            }
        }

        public static void SaveAcl()
        {
            try
            {
                var lines = new List<string>(_aclCache.Count);
                foreach (var kvp in _aclCache)
                    lines.Add($"{kvp.Key}|{kvp.Value.Owner}|{kvp.Value.Perms}");

                File.WriteAllText(AclPath, string.Join("\n", lines) + "\n");
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"ACL save failed: {ex.Message}", "SEC");
            }
        }

        public static void SetPermission(string path, string owner, int perms)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(owner) || perms < 0 || perms > 777)
            {
                WriteMessage.WriteError("Invalid ACL entry.", "SEC");
                return;
            }

            _aclCache[path] = (owner, perms);
            SaveAcl();
        }

        public static (string Owner, int Perms) GetPermission(string path)
        {
            if (_aclCache.ContainsKey(path)) return _aclCache[path];

            // Bezpieczny domyślny model: nieznany plik nie jest zapisywalny
            // przez zwykłego użytkownika. Root nadal ma pełny dostęp.
            return ("root", 600);
        }

        public static bool CanRead(string path, string user)
        {
            if (user == "root") return true;

            var acl = GetPermission(path);
            int ownerPerms = (acl.Perms / 100) % 10;
            int othersPerms = acl.Perms % 10;

            if (acl.Owner == user)
                return (ownerPerms & 4) == 4;

            return (othersPerms & 4) == 4;
        }

        public static bool CanWrite(string path, string user)
        {
            if (user == "root") return true;

            var acl = GetPermission(path);
            int ownerPerms = (acl.Perms / 100) % 10;
            int othersPerms = acl.Perms % 10;

            if (acl.Owner == user)
                return (ownerPerms & 2) == 2;

            return (othersPerms & 2) == 2;
        }
    }
}