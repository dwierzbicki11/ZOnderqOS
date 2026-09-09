using System;
using System.Collections.Generic;
using System.IO;

namespace ZonderqOS
{
    public static class PermissionManager
    {
        private static string AclPath = @"/etc/acl.map";
        // Słownik: Ścieżka -> (Właściciel, Uprawnienia)
        private static Dictionary<string, (string Owner, int Perms)> _aclCache = new Dictionary<string, (string, int)>();

        public static void Initialize()
        {
            if (!Directory.Exists("/etc")) Directory.CreateDirectory("/etc");

            if (!File.Exists(AclPath))
            {
                // Domyślne, restrykcyjne uprawnienia dla plików systemowych
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
                    if (parts.Length == 3 && int.TryParse(parts[2], out int perms))
                    {
                        _aclCache[parts[0]] = (parts[1], perms);
                    }
                }
            } 
            catch { }
        }

        public static void SaveAcl()
        {
            try 
            {
                var lines = new List<string>();
                foreach (var kvp in _aclCache)
                {
                    lines.Add($"{kvp.Key}|{kvp.Value.Owner}|{kvp.Value.Perms}");
                }
                File.WriteAllText(AclPath, string.Join("\n", lines) + "\n");
            } 
            catch { }
        }

        public static void SetPermission(string path, string owner, int perms)
        {
            _aclCache[path] = (owner, perms);
            SaveAcl();
        }

        public static (string Owner, int Perms) GetPermission(string path)
        {
            if (_aclCache.ContainsKey(path)) return _aclCache[path];
            
            // Jeśli pliku nie ma w rejestrze, domyślnie należy do roota i jest czytelny dla wszystkich (644)
            return ("root", 644);
        }

        public static bool CanRead(string path, string user)
        {
            if (user == "root") return true; // Root ma dostęp wszędzie
            
            var acl = GetPermission(path);
            int ownerPerms = acl.Perms / 100;
            int othersPerms = acl.Perms % 10;

            if (acl.Owner == user)
                return (ownerPerms & 4) == 4; // Sprawdzanie bitu odczytu dla właściciela
            else
                return (othersPerms & 4) == 4; // Sprawdzanie bitu odczytu dla reszty
        }

        public static bool CanWrite(string path, string user)
        {
            if (user == "root") return true;
            
            var acl = GetPermission(path);
            int ownerPerms = acl.Perms / 100;
            int othersPerms = acl.Perms % 10;

            if (acl.Owner == user)
                return (ownerPerms & 2) == 2; // Sprawdzanie bitu zapisu dla właściciela
            else
                return (othersPerms & 2) == 2; // Sprawdzanie bitu zapisu dla reszty
        }
    }
}