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

        [ThreadStatic]
        private static int aclSaveDepth;

        [ThreadStatic]
        private static int kernelTelemetryAclSaveDepth;

        internal static bool IsSavingAclOnThread => aclSaveDepth > 0;

        internal static bool IsSavingKernelTelemetryAclOnThread => kernelTelemetryAclSaveDepth > 0;

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
            lock (_aclLock)
            {
                try
                {
                    string etcDisk = ToDiskPath("/etc");
                    if (!Directory.Exists(etcDisk))
                        Directory.CreateDirectory(etcDisk);

                    string aclDisk = ToDiskPath(AclPath);
                    string aclTempDisk = ToDiskPath(AclTempPath);
                    string aclBakDisk = ToDiskPath(AclBackupPath);

                    // A temp file is never authoritative across boots.
                    if (File.Exists(aclTempDisk))
                        File.Delete(aclTempDisk);

                    bool activeValid = File.Exists(aclDisk) && new FileInfo(aclDisk).Length > 0;
                    bool bakValid = File.Exists(aclBakDisk) && new FileInfo(aclBakDisk).Length > 0;

                    // Recover the last complete ACL if power was lost after the
                    // old file was moved to .bak or truncated before commit.
                    if (!activeValid && bakValid)
                    {
                        File.Copy(aclBakDisk, aclDisk, true);
                        File.Delete(aclBakDisk);
                    }
                    else if (activeValid && bakValid)
                    {
                        string expectedHash;
                        if (IntegrityGuard.TryReadCommittedHashForRecovery(AclPath, out expectedHash))
                        {
                            string activeHash = Crypto.ComputeSha256(File.ReadAllBytes(aclDisk));
                            string bakHash = Crypto.ComputeSha256(File.ReadAllBytes(aclBakDisk));
                            if (!Crypto.FixedTimeEquals(activeHash, expectedHash) &&
                                Crypto.FixedTimeEquals(bakHash, expectedHash))
                            {
                                File.Copy(aclBakDisk, aclDisk, true);
                                SecurityLogger.LogEvent("WARN",
                                    "Rolled back interrupted /etc/acl.map swap to committed integrity baseline.");
                            }
                        }

                        File.Delete(aclBakDisk);
                    }

                    if (!File.Exists(aclDisk))
                    {
                        bool seededSystem =
                            File.Exists(ToDiskPath(IntegrityDbPath)) ||
                            File.Exists(ToDiskPath(IntegrityDbBackupPath)) ||
                            IntegrityGuard.HasSeededProvisioningMarker();

                        if (!seededSystem)
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

                    LoadAclLocked();
                }
                catch (Exception ex)
                {
                    WriteMessage.WriteError($"ACL initialization failed: {ex.Message}", "SEC");
                }
            }
        }

        public static void LoadAcl()
        {
            if (!IsAuthenticatedRoot())
            {
                SecurityLogger.LogEvent("WARN",
                    "Blocked unauthorized PermissionManager.LoadAcl() by '" +
                    (SecurityContext.CurrentUser ?? "unknown") + "'.");
                return;
            }

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
            if (!IsAuthenticatedRoot())
            {
                SecurityLogger.LogEvent("WARN",
                    "Blocked unauthorized PermissionManager.SaveAcl() by '" +
                    (SecurityContext.CurrentUser ?? "unknown") + "'.");
                return false;
            }

            lock (_aclLock)
            {
                return SaveAclLocked();
            }
        }

        public static bool SetPermission(string path, string owner, int perms)
        {
            if (!TryGetAuthenticatedActor(out string actor, out bool isRoot))
            {
                SecurityLogger.LogEvent("WARN", "Blocked unauthenticated PermissionManager.SetPermission().");
                WriteMessage.WriteError("Permission denied: authentication required.", "SEC");
                return false;
            }

            string normalized = NormalizePath(path);
            if (!IsValidAclIdentity(normalized, owner) || !IsValidPermissionMode(perms))
            {
                WriteMessage.WriteError("Invalid ACL entry.", "SEC");
                return false;
            }

            if (IsCoreSecurityFileConstraintViolated(normalized, owner, perms))
            {
                SecurityLogger.LogEvent("CRIT",
                    "Blocked attempt to weaken core security ACL for '" + normalized + "' by '" + actor + "'.");
                WriteMessage.WriteError("Permission denied: protected security database ACL cannot be weakened.", "SEC");
                return false;
            }

            lock (_aclLock)
            {
                if (!isRoot && !AuthorizeNonRootTargetLocked(actor, normalized, owner, allowNewEntry: true))
                {
                    SecurityLogger.LogEvent("WARN",
                        "Unauthorized SetPermission on '" + normalized + "' (owner='" + owner + "') by user '" + actor + "'.");
                    WriteMessage.WriteError("Permission denied.", "SEC");
                    return false;
                }

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
            if (!TryGetAuthenticatedActor(out string actor, out bool isRoot))
            {
                SecurityLogger.LogEvent("WARN", "Blocked unauthenticated PermissionManager.RemovePermission().");
                return false;
            }

            if (string.IsNullOrEmpty(path))
                return false;

            string normalized = NormalizePath(path);
            if (IsCoreSecurityDatabasePath(normalized))
            {
                SecurityLogger.LogEvent("CRIT",
                    "Blocked attempt to remove core security ACL entry '" + normalized + "' by '" + actor + "'.");
                return false;
            }

            lock (_aclLock)
            {
                if (!isRoot && !AuthorizeNonRootTargetLocked(actor, normalized, actor, allowNewEntry: false))
                {
                    SecurityLogger.LogEvent("WARN",
                        "Unauthorized RemovePermission on '" + normalized + "' by user '" + actor + "'.");
                    return false;
                }

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
            if (!TryGetAuthenticatedActor(out string actor, out bool isRoot))
            {
                SecurityLogger.LogEvent("WARN", "Blocked unauthenticated PermissionManager.RemovePermissionsUnder().");
                return false;
            }

            if (string.IsNullOrEmpty(rootPath))
                return false;

            string root = NormalizePath(rootPath);
            if (string.IsNullOrEmpty(root) || root == "/" || IsProtectedSystemRoot(root))
            {
                SecurityLogger.LogEvent("CRIT",
                    "Blocked PermissionManager.RemovePermissionsUnder on protected path '" + root + "' by '" + actor + "'.");
                return false;
            }

            lock (_aclLock)
            {
                if (!isRoot && !IsAllowedNonRootSubtreeLocked(actor, root))
                {
                    SecurityLogger.LogEvent("WARN",
                        "Unauthorized RemovePermissionsUnder on '" + root + "' by user '" + actor + "'.");
                    return false;
                }

                var removed = new List<(string Path, string Owner, int Perms)>();
                foreach (var kvp in _aclCache)
                {
                    if (IsSameOrChildPath(kvp.Key, root))
                    {
                        if (IsCoreSecurityDatabasePath(kvp.Key) ||
                            (!isRoot && !string.Equals(kvp.Value.Owner, actor, StringComparison.Ordinal)))
                        {
                            SecurityLogger.LogEvent("CRIT",
                                "Blocked RemovePermissionsUnder on '" + kvp.Key + "' by user '" + actor + "'.");
                            return false;
                        }
                        removed.Add((kvp.Key, kvp.Value.Owner, kvp.Value.Perms));
                    }
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
            if (!TryGetAuthenticatedActor(out string actor, out bool isRoot))
            {
                SecurityLogger.LogEvent("WARN", "Blocked unauthenticated PermissionManager.CopyPermissionsUnder().");
                return false;
            }

            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destinationPath) || !IsValidUsernameString(newOwner))
                return false;

            string source = NormalizePath(sourcePath);
            string destination = NormalizePath(destinationPath);
            if (!IsValidAclIdentity(source, newOwner) || !IsValidAclIdentity(destination, newOwner))
                return false;

            if (!isRoot && !string.Equals(newOwner, actor, StringComparison.Ordinal))
            {
                SecurityLogger.LogEvent("WARN",
                    "Blocked non-root CopyPermissionsUnder ownership change to '" + newOwner + "' by '" + actor + "'.");
                return false;
            }

            lock (_aclLock)
            {
                if (!isRoot && !IsAllowedNonRootSubtreeLocked(actor, destination))
                {
                    SecurityLogger.LogEvent("WARN",
                        "Unauthorized CopyPermissionsUnder destination '" + destination + "' by '" + actor + "'.");
                    return false;
                }

                if (!isRoot && !CanReadSubtreeLocked(source, actor))
                {
                    SecurityLogger.LogEvent("WARN",
                        "Unauthorized CopyPermissionsUnder source read '" + source + "' by '" + actor + "'.");
                    return false;
                }

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
                {
                    if (IsCoreSecurityFileConstraintViolated(entries[i].Path, newOwner, entries[i].Perms))
                        return false;
                    if (!isRoot && _aclCache.TryGetValue(entries[i].Path, out var existingDest) &&
                        !string.Equals(existingDest.Owner, actor, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }

                var backup = SnapshotCacheLocked();
                for (int i = 0; i < entries.Count; i++)
                    _aclCache[entries[i].Path] = (newOwner, entries[i].Perms);

                if (!SaveAclLocked())
                {
                    RestoreCacheLocked(backup);
                    return false;
                }
                return true;
            }
        }

        public static bool MovePermissionsUnder(string sourcePath, string destinationPath)
        {
            if (!TryGetAuthenticatedActor(out string actor, out bool isRoot))
            {
                SecurityLogger.LogEvent("WARN", "Blocked unauthenticated PermissionManager.MovePermissionsUnder().");
                return false;
            }

            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destinationPath))
                return false;

            string source = NormalizePath(sourcePath);
            string destination = NormalizePath(destinationPath);
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(destination) ||
                source[0] != '/' || destination[0] != '/' ||
                IsCoreSecurityDatabasePath(source) || IsCoreSecurityDatabasePath(destination))
            {
                return false;
            }

            lock (_aclLock)
            {
                if (!isRoot && (!IsAllowedNonRootSubtreeLocked(actor, source) || !IsAllowedNonRootSubtreeLocked(actor, destination)))
                {
                    SecurityLogger.LogEvent("WARN",
                        "Unauthorized MovePermissionsUnder ('" + source + "' -> '" + destination + "') by '" + actor + "'.");
                    return false;
                }

                var entries = new List<(string OldPath, string NewPath, string Owner, int Perms)>();
                foreach (var kvp in _aclCache)
                {
                    if (IsSameOrChildPath(kvp.Key, source))
                    {
                        if (IsCoreSecurityDatabasePath(kvp.Key) ||
                            (!isRoot && !string.Equals(kvp.Value.Owner, actor, StringComparison.Ordinal)))
                        {
                            return false;
                        }

                        string newPath = MapPath(kvp.Key, source, destination);
                        if (IsCoreSecurityFileConstraintViolated(newPath, kvp.Value.Owner, kvp.Value.Perms))
                            return false;
                        if (!isRoot && _aclCache.TryGetValue(newPath, out var existingDest) &&
                            !string.Equals(existingDest.Owner, actor, StringComparison.Ordinal))
                        {
                            return false;
                        }

                        entries.Add((
                            kvp.Key,
                            newPath,
                            kvp.Value.Owner,
                            kvp.Value.Perms));
                    }
                }

                if (entries.Count == 0)
                    return true;

                var backup = SnapshotCacheLocked();
                for (int i = 0; i < entries.Count; i++)
                    _aclCache.Remove(entries[i].OldPath);
                for (int i = 0; i < entries.Count; i++)
                    _aclCache[entries[i].NewPath] = (entries[i].Owner, entries[i].Perms);

                if (!SaveAclLocked())
                {
                    RestoreCacheLocked(backup);
                    return false;
                }
                return true;
            }
        }

        internal static bool SetBootSystemPermissionInternal(string path, int perms)
        {
            if (!SecurityContext.IsBootInitializationActive || SecurityContext.IsAuthenticated)
            {
                SecurityLogger.LogEvent("CRIT",
                    "Blocked internal boot ACL call outside sealed pre-login initialization for '" + (path ?? string.Empty) + "'.");
                return false;
            }

            string normalized = NormalizePath(path);
            if (!IsBootSystemManagedPath(normalized) || !IsValidPermissionMode(perms))
                return false;

            return ApplyFixedSystemAclLocked(normalized, "root", perms, skipIfUnchanged: true);
        }

        internal static bool SetKernelTelemetryAclInternal(string path, int perms)
        {
            string normalized = NormalizePath(path);
            if (!IsKernelTelemetryManagedPath(normalized) || !IsValidPermissionMode(perms))
                return false;

            kernelTelemetryAclSaveDepth++;
            try
            {
                return ApplyFixedSystemAclLocked(normalized, "root", perms, skipIfUnchanged: true);
            }
            finally
            {
                kernelTelemetryAclSaveDepth--;
            }
        }

        internal static bool RemoveKernelTelemetryAclInternal(string path)
        {
            string normalized = NormalizePath(path);
            if (!IsKernelTelemetryManagedPath(normalized))
                return false;

            kernelTelemetryAclSaveDepth++;
            try
            {
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
            finally
            {
                kernelTelemetryAclSaveDepth--;
            }
        }

        internal static bool EnsureUserProfileAclsInternal(string username, string homePath, string[] paths)
        {
            if (!TryGetAuthenticatedActor(out string actor, out bool isRoot))
                return false;

            if (!IsValidUsernameString(username) || paths == null || paths.Length == 0)
                return false;

            if (!isRoot && !string.Equals(actor, username, StringComparison.Ordinal))
            {
                SecurityLogger.LogEvent("CRIT",
                    "Blocked cross-user profile ACL setup for '" + username + "' by '" + actor + "'.");
                return false;
            }

            string normalizedHome = NormalizePath(homePath);
            bool validHome = string.Equals(username, "root", StringComparison.Ordinal)
                ? string.Equals(normalizedHome, "/root", StringComparison.Ordinal)
                : normalizedHome.StartsWith("/home/", StringComparison.Ordinal) &&
                  normalizedHome.Length > 6 &&
                  normalizedHome.IndexOf('/', 6) < 0 &&
                  string.Equals(normalizedHome, "/home/" + username, StringComparison.Ordinal);
            if (!validHome)
                return false;

            var desired = new List<string>(paths.Length);
            for (int i = 0; i < paths.Length; i++)
            {
                string p = NormalizePath(paths[i]);
                if (!IsSameOrChildPath(p, normalizedHome) || !IsValidAclIdentity(p, username))
                    return false;
                desired.Add(p);
            }

            lock (_aclLock)
            {
                bool needsSave = false;
                for (int i = 0; i < desired.Count; i++)
                {
                    if (_aclCache.TryGetValue(desired[i], out var existing))
                    {
                        if (!string.Equals(existing.Owner, username, StringComparison.Ordinal))
                        {
                            if (!isRoot)
                                return false;
                            needsSave = true;
                        }
                        else if (existing.Perms != 700)
                        {
                            needsSave = true;
                        }
                    }
                    else
                    {
                        needsSave = true;
                    }
                }

                if (!needsSave)
                    return true;

                var backup = SnapshotCacheLocked();
                for (int i = 0; i < desired.Count; i++)
                    _aclCache[desired[i]] = (username, 700);

                if (!SaveAclLocked())
                {
                    RestoreCacheLocked(backup);
                    return false;
                }
                return true;
            }
        }

        private static bool ApplyFixedSystemAclLocked(string normalized, string owner, int perms, bool skipIfUnchanged)
        {
            lock (_aclLock)
            {
                if (skipIfUnchanged && _aclCache.TryGetValue(normalized, out var existing) &&
                    string.Equals(existing.Owner, owner, StringComparison.Ordinal) && existing.Perms == perms)
                {
                    return true;
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

        private static Dictionary<string, (string Owner, int Perms)> SnapshotCacheLocked()
        {
            var copy = new Dictionary<string, (string Owner, int Perms)>(_aclCache.Count, StringComparer.Ordinal);
            foreach (var kvp in _aclCache)
                copy[kvp.Key] = kvp.Value;
            return copy;
        }

        private static void RestoreCacheLocked(Dictionary<string, (string Owner, int Perms)> snapshot)
        {
            _aclCache.Clear();
            foreach (var kvp in snapshot)
                _aclCache[kvp.Key] = kvp.Value;
        }

        private static bool TryGetAuthenticatedActor(out string actor, out bool isRoot)
        {
            actor = string.Empty;
            isRoot = false;
            if (!SecurityContext.IsAuthenticated || SecurityContext.CurrentUid < 0)
                return false;

            string current = SecurityContext.CurrentUser;
            if (!IsValidUsernameString(current) ||
                string.Equals(current, "unauthenticated", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            actor = current;
            isRoot = SecurityContext.CurrentUid == 0 && string.Equals(current, "root", StringComparison.Ordinal);
            return true;
        }

        private static bool IsAuthenticatedRoot()
        {
            return TryGetAuthenticatedActor(out _, out bool isRoot) && isRoot;
        }

        private static bool IsBootSystemManagedPath(string normalized)
        {
            return string.Equals(normalized, "/var/log/system.log", StringComparison.Ordinal) ||
                   string.Equals(normalized, "/var/log/auth.log", StringComparison.Ordinal) ||
                   string.Equals(normalized, "/var/lib/zonderq/boot-session.state", StringComparison.Ordinal) ||
                   string.Equals(normalized, "/etc/hostname", StringComparison.Ordinal) ||
                   string.Equals(normalized, "/etc/issue", StringComparison.Ordinal) ||
                   string.Equals(normalized, "/etc/motd", StringComparison.Ordinal) ||
                   string.Equals(normalized, "/etc/os-release", StringComparison.Ordinal) ||
                   string.Equals(normalized, "/etc/firewall.cfg", StringComparison.Ordinal) ||
                   string.Equals(normalized, "/var/lib/zpkg/installed", StringComparison.Ordinal) ||
                   string.Equals(normalized, "/var/cache/zpkg/packages", StringComparison.Ordinal) ||
                   string.Equals(normalized, "/var/crash/last-panic.txt", StringComparison.Ordinal);
        }

        private static bool IsKernelTelemetryManagedPath(string normalized)
        {
            return string.Equals(normalized, "/var/log/system.log", StringComparison.Ordinal) ||
                   string.Equals(normalized, "/var/log/auth.log", StringComparison.Ordinal) ||
                   string.Equals(normalized, "/var/lib/zonderq/boot-session.state", StringComparison.Ordinal) ||
                   string.Equals(normalized, "/var/crash/last-panic.txt", StringComparison.Ordinal);
        }

        private static bool IsCoreSecurityDatabasePath(string normalized)
        {
            return string.Equals(normalized, "/etc/shadow", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "/etc/shadow.new", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "/etc/shadow.bak", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "/etc/passwd", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "/etc/passwd.new", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "/etc/passwd.bak", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "/etc/acl.map", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "/etc/acl.map.new", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "/etc/acl.map.bak", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "/etc/integrity.db", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "/etc/integrity.db.new", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "/etc/integrity.db.bak", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCoreSecurityFileConstraintViolated(string normalized, string owner, int perms)
        {
            if (string.Equals(normalized, "/etc/shadow", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "/etc/acl.map", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "/etc/integrity.db", StringComparison.OrdinalIgnoreCase))
            {
                return !string.Equals(owner, "root", StringComparison.Ordinal) || perms != 600;
            }

            if (string.Equals(normalized, "/etc/passwd", StringComparison.OrdinalIgnoreCase))
            {
                int groupPerms = (perms / 10) % 10;
                int othersPerms = perms % 10;
                return !string.Equals(owner, "root", StringComparison.Ordinal) ||
                       (groupPerms & 2) != 0 ||
                       (othersPerms & 2) != 0;
            }

            if (IsCoreSecurityDatabasePath(normalized))
                return !string.Equals(owner, "root", StringComparison.Ordinal);

            return false;
        }

        private static bool IsProtectedSystemRoot(string normalized)
        {
            if (string.IsNullOrEmpty(normalized) || normalized == "/" ||
                normalized == "/home" || normalized == "/tmp" || normalized == "/root")
                return true;

            return IsSameOrChildPath(normalized, "/etc") ||
                   IsSameOrChildPath(normalized, "/boot") ||
                   IsSameOrChildPath(normalized, "/bin") ||
                   IsSameOrChildPath(normalized, "/sbin") ||
                   IsSameOrChildPath(normalized, "/usr") ||
                   IsSameOrChildPath(normalized, "/lib") ||
                   IsSameOrChildPath(normalized, "/var") ||
                   IsSameOrChildPath(normalized, "/dev") ||
                   IsSameOrChildPath(normalized, "/proc") ||
                   IsSameOrChildPath(normalized, "/sys") ||
                   IsSameOrChildPath(normalized, "/root");
        }

        private static bool IsAllowedNonRootSubtreeLocked(string actor, string normalized)
        {
            if (string.IsNullOrEmpty(normalized) || IsProtectedSystemRoot(normalized) || IsCoreSecurityDatabasePath(normalized))
                return false;

            string actorHome = "/home/" + actor;
            bool inOwnHome = normalized.StartsWith(actorHome + "/", StringComparison.Ordinal);
            bool inTmp = normalized.StartsWith("/tmp/", StringComparison.Ordinal);
            if (!inOwnHome && !inTmp)
                return false;

            // Check that no ancestor directory inside the workspace explicitly blocks writes for actor.
            string parent = GetParentDirectory(normalized);
            string floor = inOwnHome ? actorHome : "/tmp";
            return CanWriteToAncestorLocked(parent, floor, actor);
        }

        private static bool AuthorizeNonRootTargetLocked(string actor, string normalized, string requestedOwner, bool allowNewEntry)
        {
            if (!string.Equals(requestedOwner, actor, StringComparison.Ordinal))
                return false;

            if (!IsAllowedNonRootSubtreeLocked(actor, normalized))
                return false;

            if (_aclCache.TryGetValue(normalized, out var existing))
                return string.Equals(existing.Owner, actor, StringComparison.Ordinal);

            return allowNewEntry;
        }

        private static bool CanWriteToAncestorLocked(string path, string floor, string actor)
        {
            string cur = path;
            while (!string.IsNullOrEmpty(cur))
            {
                if (_aclCache.TryGetValue(cur, out var acl))
                {
                    int ownerPerms = (acl.Perms / 100) % 10;
                    int othersPerms = acl.Perms % 10;
                    return string.Equals(acl.Owner, actor, StringComparison.Ordinal)
                        ? (ownerPerms & 2) == 2
                        : (othersPerms & 2) == 2;
                }

                if (string.Equals(cur, floor, StringComparison.Ordinal))
                {
                    // /home/<actor> is writable by actor; /tmp (if uncached) allows creating new files by actor.
                    return true;
                }

                cur = GetParentDirectory(cur);
            }

            return false;
        }

        private static bool CanReadSubtreeLocked(string source, string actor)
        {
            bool foundEntry = false;
            foreach (var kvp in _aclCache)
            {
                if (IsSameOrChildPath(kvp.Key, source))
                {
                    foundEntry = true;
                    if (!CanReadAcl(kvp.Value, actor))
                        return false;
                }
            }

            if (foundEntry)
                return true;

            var fallback = _aclCache.TryGetValue(source, out var acl) ? acl : ("root", 600);
            return CanReadAcl(fallback, actor);
        }

        private static bool CanReadAcl((string Owner, int Perms) acl, string user)
        {
            if (user == "root") return true;
            int ownerPerms = (acl.Perms / 100) % 10;
            int othersPerms = acl.Perms % 10;
            return acl.Owner == user ? (ownerPerms & 4) == 4 : (othersPerms & 4) == 4;
        }

        private static string GetParentDirectory(string normalized)
        {
            if (string.IsNullOrEmpty(normalized) || normalized == "/")
                return string.Empty;

            int idx = normalized.LastIndexOf('/');
            if (idx <= 0)
                return "/";
            return normalized.Substring(0, idx);
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

        private static bool IsValidUsernameString(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 32)
                return false;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool ok = (c >= 'a' && c <= 'z') ||
                          (c >= 'A' && c <= 'Z') ||
                          (c >= '0' && c <= '9') ||
                          c == '_' || c == '-';
                if (!ok)
                    return false;
            }

            return true;
        }

        private static bool IsValidAclIdentity(string path, string owner)
        {
            if (string.IsNullOrEmpty(path) || path.Length > MaxPathLength)
                return false;

            if (path[0] != '/')
                return false;

            if (string.IsNullOrEmpty(owner) || owner.Length > MaxOwnerLength || !IsValidUsernameString(owner))
                return false;

            if (path.IndexOf('|') >= 0 || path.IndexOf('\r') >= 0 || path.IndexOf('\n') >= 0)
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
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            string raw = path.Replace('\\', '/').Trim();
            if (raw.Length == 0 || raw[0] != '/')
                return string.Empty;

            string[] segments = raw.Split('/');
            var canonical = new List<string>(segments.Length);
            for (int i = 0; i < segments.Length; i++)
            {
                string seg = segments[i];
                if (string.IsNullOrEmpty(seg) || seg == ".")
                    continue;
                if (seg == "..")
                {
                    if (canonical.Count > 0)
                        canonical.RemoveAt(canonical.Count - 1);
                    continue;
                }
                canonical.Add(seg);
            }

            return canonical.Count == 0 ? "/" : "/" + string.Join("/", canonical);
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

            aclSaveDepth++;
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

                if (!IntegrityGuard.TryBeginManagedFilesUpdate(ManagedOperationKind.AclSave, new[] { AclPath }, out lease))
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

                if (!IntegrityGuard.CommitManagedFilesUpdate(lease))
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
            finally
            {
                aclSaveDepth--;
            }
        }
    }
}
