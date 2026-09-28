using System;
using System.Collections.Generic;
using System.IO;

namespace ZonderqOS
{
    public static class PermissionManager
    {
        private const string AclPath = "/etc/acl.map";
        private const string AclTempPath = "/etc/acl.map.new";
        private const string AclBackupPath = "/etc/acl.map.bak";
        private const long MaxAclBytes = 256 * 1024;
        private const int MaxAclEntries = 4096;
        private const int MaxPathLength = 512;
        private const int MaxOwnerLength = 64;

        private static readonly Dictionary<string, (string Owner, int Perms)> _aclCache = new Dictionary<string, (string, int)>();
        private static readonly object _aclLock = new object();

        public static void Initialize()
        {
            try
            {
                if (!Directory.Exists("/etc"))
                    Directory.CreateDirectory("/etc");

                // Recover the last complete ACL if power was lost after the
                // old file was renamed but before the new file was committed.
                if (!File.Exists(AclPath) && File.Exists(AclBackupPath))
                    File.Move(AclBackupPath, AclPath);

                // A temp file is never authoritative across boots.
                if (File.Exists(AclTempPath))
                    File.Delete(AclTempPath);

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
                    if (!File.Exists(AclPath))
                        return;

                    FileInfo info = new FileInfo(AclPath);
                    if (info.Length < 0 || info.Length > MaxAclBytes)
                    {
                        WriteMessage.WriteError("ACL file size is invalid; using fail-closed defaults.", "SEC");
                        return;
                    }

                    using (StreamReader reader = new StreamReader(AclPath))
                    {
                        string line;
                        int parsed = 0;

                        while ((line = reader.ReadLine()) != null)
                        {
                            if (parsed >= MaxAclEntries)
                            {
                                WriteMessage.WriteError("ACL entry limit exceeded; remaining entries ignored.", "SEC");
                                break;
                            }

                            string[] parts = line.Split('|');
                            if (parts.Length != 3)
                                continue;

                            string path = NormalizePath(parts[0]);
                            string owner = parts[1];

                            int perms;
                            if (!IsValidAclIdentity(path, owner) ||
                                !int.TryParse(parts[2], out perms) ||
                                !IsValidPermissionMode(perms))
                                continue;

                            _aclCache[path] = (owner, perms);
                            parsed++;
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
            if (!IsValidAclIdentity(NormalizePath(path), owner) || !IsValidPermissionMode(perms))
            {
                WriteMessage.WriteError("Invalid ACL entry.", "SEC");
                return;
            }

            lock (_aclLock)
            {
                string normalized = NormalizePath(path);
                if (!_aclCache.ContainsKey(normalized) && _aclCache.Count >= MaxAclEntries)
                {
                    WriteMessage.WriteError("ACL entry limit reached.", "SEC");
                    return;
                }

                _aclCache[normalized] = (owner, perms);
                SaveAclLocked();
            }
        }

        public static void RemovePermission(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            lock (_aclLock)
            {
                if (_aclCache.Remove(NormalizePath(path)))
                    SaveAclLocked();
            }
        }

        public static void RemovePermissionsUnder(string rootPath)
        {
            if (string.IsNullOrEmpty(rootPath))
                return;

            string root = NormalizePath(rootPath);
            lock (_aclLock)
            {
                var paths = new List<string>();
                foreach (var kvp in _aclCache)
                {
                    if (IsSameOrChildPath(kvp.Key, root))
                        paths.Add(kvp.Key);
                }

                if (paths.Count == 0)
                    return;

                for (int i = 0; i < paths.Count; i++)
                    _aclCache.Remove(paths[i]);

                SaveAclLocked();
            }
        }

        public static void CopyPermissionsUnder(string sourcePath, string destinationPath, string newOwner)
        {
            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destinationPath) || string.IsNullOrEmpty(newOwner))
                return;

            string source = NormalizePath(sourcePath);
            string destination = NormalizePath(destinationPath);

            lock (_aclLock)
            {
                var entries = new List<(string Path, int Perms)>();
                foreach (var kvp in _aclCache)
                {
                    if (IsSameOrChildPath(kvp.Key, source))
                        entries.Add((MapPath(kvp.Key, source, destination), kvp.Value.Perms));
                }

                if (entries.Count == 0 && File.Exists(source))
                    entries.Add((destination, 600));

                if (entries.Count == 0)
                    return;

                for (int i = 0; i < entries.Count; i++)
                    _aclCache[entries[i].Path] = (newOwner, entries[i].Perms);

                SaveAclLocked();
            }
        }

        public static void MovePermissionsUnder(string sourcePath, string destinationPath)
        {
            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destinationPath))
                return;

            string source = NormalizePath(sourcePath);
            string destination = NormalizePath(destinationPath);

            lock (_aclLock)
            {
                var entries = new List<(string OldPath, string NewPath, string Owner, int Perms)>();
                foreach (var kvp in _aclCache)
                {
                    if (IsSameOrChildPath(kvp.Key, source))
                    {
                        entries.Add((
                            kvp.Key,
                            MapPath(kvp.Key, source, destination),
                            kvp.Value.Owner,
                            kvp.Value.Perms));
                    }
                }

                if (entries.Count == 0)
                    return;

                for (int i = 0; i < entries.Count; i++)
                    _aclCache.Remove(entries[i].OldPath);
                for (int i = 0; i < entries.Count; i++)
                    _aclCache[entries[i].NewPath] = (entries[i].Owner, entries[i].Perms);

                SaveAclLocked();
            }
        }

        public static (string Owner, int Perms) GetPermission(string path)
        {
            lock (_aclLock)
            {
                if (!string.IsNullOrEmpty(path) && _aclCache.TryGetValue(NormalizePath(path), out var acl))
                    return acl;
            }

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

        private static bool IsValidAclIdentity(string path, string owner)
        {
            if (string.IsNullOrEmpty(path) || path.Length > MaxPathLength)
                return false;

            if (path[0] != '/')
                return false;

            if (string.IsNullOrEmpty(owner) || owner.Length > MaxOwnerLength)
                return false;

            if (path.IndexOf('|') >= 0 || path.IndexOf('\r') >= 0 || path.IndexOf('\n') >= 0)
                return false;

            if (owner.IndexOf('|') >= 0 || owner.IndexOf('\r') >= 0 || owner.IndexOf('\n') >= 0)
                return false;

            return true;
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

        private static string NormalizePath(string path)
        {
            string value = (path ?? string.Empty).Replace('\\', '/');
            while (value.Length > 1 && value.EndsWith("/", StringComparison.Ordinal))
                value = value.Substring(0, value.Length - 1);
            return value;
        }

        private static bool IsSameOrChildPath(string path, string root)
        {
            if (string.Equals(path, root, StringComparison.Ordinal))
                return true;

            if (root == "/")
                return path.StartsWith("/", StringComparison.Ordinal);

            return path.StartsWith(root + "/", StringComparison.Ordinal);
        }

        private static string MapPath(string path, string sourceRoot, string destinationRoot)
        {
            if (string.Equals(path, sourceRoot, StringComparison.Ordinal))
                return destinationRoot;

            string relative = path.Substring(sourceRoot.Length).TrimStart('/');
            if (destinationRoot == "/")
                return "/" + relative;
            return destinationRoot + "/" + relative;
        }

        private static void SaveAclLocked()
        {
            try
            {
                if (_aclCache.Count > MaxAclEntries)
                {
                    WriteMessage.WriteError("ACL cache exceeds safety limit; refusing to persist.", "SEC");
                    return;
                }

                var lines = new List<string>(_aclCache.Count);
                foreach (var kvp in _aclCache)
                {
                    if (!IsValidAclIdentity(kvp.Key, kvp.Value.Owner) ||
                        !IsValidPermissionMode(kvp.Value.Perms))
                        continue;

                    lines.Add(kvp.Key + "|" + kvp.Value.Owner + "|" + kvp.Value.Perms);
                }

                lines.Sort(StringComparer.Ordinal);
                string payload = lines.Count == 0
                    ? string.Empty
                    : string.Join("\n", lines) + "\n";

                if (payload.Length > MaxAclBytes)
                {
                    WriteMessage.WriteError("ACL payload exceeds safety limit; refusing to persist.", "SEC");
                    return;
                }

                if (File.Exists(AclTempPath))
                    File.Delete(AclTempPath);

                File.WriteAllText(AclTempPath, payload);

                if (File.Exists(AclBackupPath))
                    File.Delete(AclBackupPath);

                if (File.Exists(AclPath))
                    File.Move(AclPath, AclBackupPath);

                try
                {
                    File.Move(AclTempPath, AclPath);
                }
                catch
                {
                    if (!File.Exists(AclPath) && File.Exists(AclBackupPath))
                        File.Move(AclBackupPath, AclPath);
                    throw;
                }

                if (File.Exists(AclBackupPath))
                    File.Delete(AclBackupPath);
            }
            catch (Exception ex)
            {
                try
                {
                    if (File.Exists(AclTempPath))
                        File.Delete(AclTempPath);
                }
                catch { }

                WriteMessage.WriteError($"ACL save failed: {ex.Message}", "SEC");
                SystemLogger.Log(SystemLogLevel.Error, "ACL", "ACL save failed: " + ex.Message);
            }
        }
    }
}
