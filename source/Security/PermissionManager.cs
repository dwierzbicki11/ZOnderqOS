using System;
using System.Collections.Generic;
using System.IO;
using ZonderqOS.Security;

namespace ZonderqOS
{
    public static class PermissionManager
    {
        private const string AclPath = "/etc/acl.map";
        private const string AclTempPath = "/etc/acl.map.new";
        private const string AclBackupPath = "/etc/acl.map.bak";
        private const string IntegrityDbPath = "/etc/integrity.db";
        private const string IntegrityDbBackupPath = "/etc/integrity.db.bak";
        private const long MaxAclBytes = 256 * 1024;
        private const int MaxAclEntries = 4096;
        private const int MaxPathLength = 512;
        private const int MaxOwnerLength = 64;

        private static readonly Dictionary<string, (string Owner, int Perms)> _aclCache = new Dictionary<string, (string, int)>();
        private static readonly object _aclLock = new object();

        private static string ToDiskPath(string logicalPath)
        {
#if ZIG_HOST_TESTS
            string root = Environment.GetEnvironmentVariable("ZIG_TEST_ROOT");
            if (!string.IsNullOrEmpty(root))
                return Path.Combine(root, logicalPath.TrimStart('/'));
#endif
            return logicalPath;
        }

        public static void Initialize()
        {
            try
            {
                string etcDisk = ToDiskPath("/etc");
                if (!Directory.Exists(etcDisk))
                    Directory.CreateDirectory(etcDisk);

                string aclDisk = ToDiskPath(AclPath);
                string aclTempDisk = ToDiskPath(AclTempPath);
                string aclBakDisk = ToDiskPath(AclBackupPath);

                // Recover the last complete ACL if power was lost after the
                // old file was moved to .bak or truncated before commit.
                if ((!File.Exists(aclDisk) || new FileInfo(aclDisk).Length == 0) &&
                    File.Exists(aclBakDisk) && new FileInfo(aclBakDisk).Length > 0)
                {
                    File.Copy(aclBakDisk, aclDisk, true);
                    File.Delete(aclBakDisk);
                }

                // A temp file is never authoritative across boots.
                if (File.Exists(aclTempDisk))
                    File.Delete(aclTempDisk);

                if (File.Exists(aclDisk) && File.Exists(aclBakDisk))
                    File.Delete(aclBakDisk);

                if (!File.Exists(aclDisk))
                {
                    bool integrityInitialized =
                        File.Exists(ToDiskPath(IntegrityDbPath)) ||
                        File.Exists(ToDiskPath(IntegrityDbBackupPath));

                    if (!integrityInitialized)
                    {
                        string defaultAcl =
                            "/etc/shadow|root|600\n" +
                            "/etc/passwd|root|644\n" +
                            "/etc/acl.map|root|600\n" +
                            "/etc/firewall.cfg|root|600\n" +
                            "/etc/hostname|root|644\n" +
                            "/etc/zonderq/settings.conf|root|644\n" +
                            "/var/log/auth.log|root|600\n" +
                            "/etc/profile|root|644\n";
                        File.WriteAllText(aclDisk, defaultAcl);
                    }
                    else
                    {
                        SecurityLogger.LogEvent("CRITICAL",
                            "Missing /etc/acl.map on initialized system; fail-closed defaults active.");
                    }
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
                LoadAclLocked();
            }
        }

        private static void LoadAclLocked()
        {
            _aclCache.Clear();
            try
            {
                string aclDisk = ToDiskPath(AclPath);
                if (!File.Exists(aclDisk))
                    return;

                FileInfo info = new FileInfo(aclDisk);
                if (info.Length < 0 || info.Length > MaxAclBytes)
                {
                    WriteMessage.WriteError("ACL file size is invalid; using fail-closed defaults.", "SEC");
                    return;
                }

                using (StreamReader reader = new StreamReader(aclDisk))
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

        public static bool SaveAcl()
        {
            lock (_aclLock)
            {
                return SaveAclLocked();
            }
        }

        public static bool SetPermission(string path, string owner, int perms)
        {
            string normalized = NormalizePath(path);
            if (!IsValidAclIdentity(normalized, owner) || !IsValidPermissionMode(perms))
            {
                WriteMessage.WriteError("Invalid ACL entry.", "SEC");
                return false;
            }

            lock (_aclLock)
            {
                if (!_aclCache.ContainsKey(normalized) && _aclCache.Count >= MaxAclEntries)
                {
                    WriteMessage.WriteError("ACL entry limit reached.", "SEC");
                    return false;
                }

                bool hadPrevious = _aclCache.TryGetValue(normalized, out var previous);
                _aclCache[normalized] = (owner, perms);
                if (!SaveAclLocked())
                {
                    if (hadPrevious)
                        _aclCache[normalized] = previous;
                    else
                        _aclCache.Remove(normalized);
                    return false;
                }
                return true;
            }
        }

        public static bool RemovePermission(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            string normalized = NormalizePath(path);
            lock (_aclLock)
            {
                if (!_aclCache.TryGetValue(normalized, out var previous))
                    return true;

                _aclCache.Remove(normalized);
                if (!SaveAclLocked())
                {
                    _aclCache[normalized] = previous;
                    return false;
                }
                return true;
            }
        }

        public static bool RemovePermissionsUnder(string rootPath)
        {
            if (string.IsNullOrEmpty(rootPath))
                return false;

            string root = NormalizePath(rootPath);
            lock (_aclLock)
            {
                var removed = new List<(string Path, string Owner, int Perms)>();
                foreach (var kvp in _aclCache)
                {
                    if (IsSameOrChildPath(kvp.Key, root))
                        removed.Add((kvp.Key, kvp.Value.Owner, kvp.Value.Perms));
                }

                if (removed.Count == 0)
                    return true;

                for (int i = 0; i < removed.Count; i++)
                    _aclCache.Remove(removed[i].Path);

                if (!SaveAclLocked())
                {
                    for (int i = 0; i < removed.Count; i++)
                        _aclCache[removed[i].Path] = (removed[i].Owner, removed[i].Perms);
                    return false;
                }
                return true;
            }
        }

        public static bool CopyPermissionsUnder(string sourcePath, string destinationPath, string newOwner)
        {
            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destinationPath) || string.IsNullOrEmpty(newOwner))
                return false;

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

                if (entries.Count == 0 && File.Exists(ToDiskPath(source)))
                    entries.Add((destination, 600));

                if (entries.Count == 0)
                    return true;

                for (int i = 0; i < entries.Count; i++)
                    _aclCache[entries[i].Path] = (newOwner, entries[i].Perms);

                if (!SaveAclLocked())
                {
                    LoadAclLocked();
                    return false;
                }
                return true;
            }
        }

        public static bool MovePermissionsUnder(string sourcePath, string destinationPath)
        {
            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destinationPath))
                return false;

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
                    return true;

                for (int i = 0; i < entries.Count; i++)
                    _aclCache.Remove(entries[i].OldPath);
                for (int i = 0; i < entries.Count; i++)
                    _aclCache[entries[i].NewPath] = (entries[i].Owner, entries[i].Perms);

                if (!SaveAclLocked())
                {
                    LoadAclLocked();
                    return false;
                }
                return true;
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

        private static bool SaveAclLocked()
        {
            string aclDisk = ToDiskPath(AclPath);
            string aclTempDisk = ToDiskPath(AclTempPath);
            string aclBakDisk = ToDiskPath(AclBackupPath);
            ManagedFileUpdateLease lease = null;

            try
            {
                if (_aclCache.Count > MaxAclEntries)
                {
                    WriteMessage.WriteError("ACL cache exceeds safety limit; refusing to persist.", "SEC");
                    return false;
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
                    return false;
                }

                if (IntegrityGuard.IsInitialized &&
                    !IntegrityGuard.TryBeginManagedFilesUpdate(ManagedOperationKind.AclSave, new[] { AclPath }, out lease))
                {
                    WriteMessage.WriteError("Refused ACL update: /etc/acl.map failed pre-write integrity verification.", "SEC");
                    return false;
                }

#if ZIG_HOST_TESTS
                if (string.Equals(IntegrityGuard.SimulatedWriteFailurePath, AclPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(IntegrityGuard.SimulatedWriteFailurePath, AclTempPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("Simulated ACL write failure.");
                }
#endif

                if (File.Exists(aclTempDisk))
                    File.Delete(aclTempDisk);

                File.WriteAllText(aclTempDisk, payload);

                if (File.Exists(aclBakDisk))
                    File.Delete(aclBakDisk);

                bool hadOriginal = File.Exists(aclDisk);
                if (hadOriginal)
                    File.Move(aclDisk, aclBakDisk);

                try
                {
                    File.Move(aclTempDisk, aclDisk);
                }
                catch
                {
                    if (!File.Exists(aclDisk) && File.Exists(aclBakDisk))
                        File.Move(aclBakDisk, aclDisk);
                    throw;
                }

                if (lease != null && !IntegrityGuard.CommitManagedFilesUpdate(lease))
                {
                    if (hadOriginal && File.Exists(aclBakDisk))
                    {
                        if (File.Exists(aclDisk))
                            File.Delete(aclDisk);
                        File.Move(aclBakDisk, aclDisk);
                    }
                    WriteMessage.WriteError("ACL update rolled back: integrity baseline commit failed.", "SEC");
                    return false;
                }

                if (File.Exists(aclBakDisk))
                    File.Delete(aclBakDisk);

                return true;
            }
            catch (Exception ex)
            {
                if (lease != null)
                    IntegrityGuard.CancelManagedFilesUpdate(lease);

                try
                {
                    if (File.Exists(aclTempDisk))
                        File.Delete(aclTempDisk);
                }
                catch { }

                WriteMessage.WriteError($"ACL save failed: {ex.Message}", "SEC");
                SystemLogger.Log(SystemLogLevel.Error, "ACL", "ACL save failed: " + ex.Message);
                return false;
            }
        }
    }
}
