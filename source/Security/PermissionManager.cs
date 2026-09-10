using System;
using System.Collections.Generic;
using System.IO;

namespace ZonderqOS
{
    public static class PermissionManager
    {
        private static string AclPath = @"/etc/acl.map";
        private static readonly Dictionary<string, (string Owner, int Perms)> _aclCache = new Dictionary<string, (string, int)>();
        private static readonly object _aclLock = new object();

        public static void Initialize()
        {
            try
            {
                if (!Directory.Exists("/etc"))
                    Directory.CreateDirectory("/etc");

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
            catch (Exception ex)
            {
                WriteMessage.WriteError($"ACL initialization failed: {ex.Message}", "SEC");
            }
        }

        public static void LoadAcl()
        {
            lock (_aclLock)
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
                            IsValidPermissionMode(perms))
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
        }

        public static void SaveAcl()
        {
            lock (_aclLock)
            {
                SaveAclLocked();
            }
        }

        public static void SetPermission(string path, string owner, int perms)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(owner) || !IsValidPermissionMode(perms))
            {
                WriteMessage.WriteError("Invalid ACL entry.", "SEC");
                return;
            }

            lock (_aclLock)
            {
                _aclCache[path] = (owner, perms);
                SaveAclLocked();
            }
        }

        public static void RemovePermission(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            lock (_aclLock)
            {
                if (_aclCache.Remove(path))
                    SaveAclLocked();
            }
        }

        public static (string Owner, int Perms) GetPermission(string path)
        {
            lock (_aclLock)
            {
                if (!string.IsNullOrEmpty(path) && _aclCache.TryGetValue(path, out var acl))
                    return acl;
            }

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

        private static bool IsValidPermissionMode(int perms)
        {
            if (perms < 0 || perms > 777)
                return false;

            int owner = (perms / 100) % 10;
            int group = (perms / 10) % 10;
            int others = perms % 10;
            return owner <= 7 && group <= 7 && others <= 7;
        }

        private static void SaveAclLocked()
        {
            try
            {
                var lines = new List<string>(_aclCache.Count);
                foreach (var kvp in _aclCache)
                    lines.Add($"{kvp.Key}|{kvp.Value.Owner}|{kvp.Value.Perms}");

                File.WriteAllText(AclPath, lines.Count == 0 ? string.Empty : string.Join("\n", lines) + "\n");
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"ACL save failed: {ex.Message}", "SEC");
            }
        }
    }
}
