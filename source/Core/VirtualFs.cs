using System;
using System.Collections.Generic;

namespace ZonderqOS.SystemCore
{
    public static class VirtualFs
    {
        private static readonly object OwnerSync = new object();
        private static readonly Dictionary<string, string> VolatileOwners =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private static string NormalizeOwnerKey(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "/";
            string value = path.Replace('\\', '/').Trim().TrimEnd('/');
            return value.Length == 0 ? "/" : value;
        }

        private static bool CanMutateVolatilePath(string path, bool isRunPath, bool requireExistingOwnerMatch, out string error)
        {
            error = null;
            if (!SecurityContext.IsAuthenticated)
            {
                error = "authentication required for virtual filesystem mutation";
                return false;
            }

            if (isRunPath && SecurityContext.CurrentUid != 0)
            {
                error = "permission denied: /run requires root privileges";
                return false;
            }

            if (SecurityContext.CurrentUid == 0 || !requireExistingOwnerMatch)
                return true;

            string key = NormalizeOwnerKey(path);
            string currentUser = SecurityContext.CurrentUser ?? string.Empty;
            lock (OwnerSync)
            {
                if (VolatileOwners.TryGetValue(key, out string owner) &&
                    !string.IsNullOrEmpty(owner) &&
                    !string.Equals(owner, currentUser, StringComparison.Ordinal))
                {
                    error = "permission denied: owned by " + owner;
                    return false;
                }
            }

            return true;
        }

        private static void RecordOwnerIfNew(string path)
        {
            string key = NormalizeOwnerKey(path);
            string currentUser = SecurityContext.CurrentUser ?? "root";
            lock (OwnerSync)
            {
                if (!VolatileOwners.ContainsKey(key))
                    VolatileOwners[key] = currentUser;
            }
        }

        private static void RemoveOwner(string path, bool recursive)
        {
            string key = NormalizeOwnerKey(path);
            lock (OwnerSync)
            {
                VolatileOwners.Remove(key);
                if (!recursive)
                    return;

                string prefix = key + "/";
                var toRemove = new List<string>();
                foreach (string existing in VolatileOwners.Keys)
                {
                    if (existing.StartsWith(prefix, StringComparison.Ordinal))
                        toRemove.Add(existing);
                }
                for (int i = 0; i < toRemove.Count; i++)
                    VolatileOwners.Remove(toRemove[i]);
            }
        }

        public static bool IsVirtualPath(string path)
        {
            return ProcFs.IsProcPath(path) || SysFs.IsSysPath(path) || DeviceFs.IsDevicePath(path) || RunFs.IsRunPath(path) || TmpFs.IsTmpPath(path);
        }

        public static bool IsReadOnlyPath(string path)
        {
            return ProcFs.IsProcPath(path) || SysFs.IsSysPath(path) || DeviceFs.IsDevicePath(path);
        }

        public static bool TryRead(string path, out string content)
        {
            if (ProcFs.IsProcPath(path))
                return ProcFs.TryRead(path, out content);

            if (SysFs.IsSysPath(path))
                return SysFs.TryRead(path, out content);

            if (DeviceFs.IsDevicePath(path))
                return DeviceFs.TryRead(path, out content);

            if (RunFs.IsRunPath(path))
                return RunFs.TryRead(path, out content);

            if (TmpFs.IsTmpPath(path))
                return TmpFs.TryRead(path, out content);

            content = null;
            return false;
        }

        public static bool TryList(string path, out string[] entries)
        {
            if (ProcFs.IsProcPath(path))
                return ProcFs.TryList(path, out entries);

            if (SysFs.IsSysPath(path))
                return SysFs.TryList(path, out entries);

            if (DeviceFs.IsDevicePath(path))
                return DeviceFs.TryList(path, out entries);

            if (RunFs.IsRunPath(path))
                return RunFs.TryList(path, out entries);

            if (TmpFs.IsTmpPath(path))
                return TmpFs.TryList(path, out entries);

            entries = null;
            return false;
        }

        public static bool TryWrite(string path, string content)
        {
            return TryWrite(path, content, false, out _);
        }

        public static bool TryWrite(string path, string content, bool append, out string error)
        {
            error = null;

            if (DeviceFs.IsDevicePath(path))
            {
                string normDev = NormalizeOwnerKey(path);
                if (normDev != "/dev/null" && !SecurityContext.IsAuthenticated)
                {
                    error = "authentication required to write to device";
                    return false;
                }
                return DeviceFs.TryWrite(path, content);
            }

            if (RunFs.IsRunPath(path))
            {
                bool existed = RunFs.FileExists(path);
                if (!CanMutateVolatilePath(path, true, existed, out error))
                    return false;
                bool ok = RunFs.TryWrite(path, content, append, out error);
                if (ok && !existed)
                    RecordOwnerIfNew(path);
                return ok;
            }

            if (TmpFs.IsTmpPath(path))
            {
                bool existed = TmpFs.FileExists(path);
                if (!CanMutateVolatilePath(path, false, existed, out error))
                    return false;
                bool ok = TmpFs.TryWrite(path, content, append, out error);
                if (ok && !existed)
                    RecordOwnerIfNew(path);
                return ok;
            }

            return false;
        }

        public static bool TryCreateDirectory(string path, out string error)
        {
            if (RunFs.IsRunPath(path))
            {
                if (!CanMutateVolatilePath(path, true, false, out error))
                    return false;
                bool ok = RunFs.TryCreateDirectory(path, out error);
                if (ok)
                    RecordOwnerIfNew(path);
                return ok;
            }

            if (TmpFs.IsTmpPath(path))
            {
                if (!CanMutateVolatilePath(path, false, false, out error))
                    return false;
                bool ok = TmpFs.TryCreateDirectory(path, out error);
                if (ok)
                    RecordOwnerIfNew(path);
                return ok;
            }

            error = null;
            return false;
        }

        public static bool TryDeleteFile(string path, out string error)
        {
            if (RunFs.IsRunPath(path))
            {
                if (!CanMutateVolatilePath(path, true, true, out error))
                    return false;
                bool ok = RunFs.TryDeleteFile(path, out error);
                if (ok)
                    RemoveOwner(path, false);
                return ok;
            }

            if (TmpFs.IsTmpPath(path))
            {
                if (!CanMutateVolatilePath(path, false, true, out error))
                    return false;
                bool ok = TmpFs.TryDeleteFile(path, out error);
                if (ok)
                    RemoveOwner(path, false);
                return ok;
            }

            error = null;
            return false;
        }

        public static bool TryDeleteDirectory(string path, bool recursive, out string error)
        {
            if (RunFs.IsRunPath(path))
            {
                if (!CanMutateVolatilePath(path, true, true, out error))
                    return false;
                bool ok = RunFs.TryDeleteDirectory(path, recursive, out error);
                if (ok)
                    RemoveOwner(path, recursive);
                return ok;
            }

            if (TmpFs.IsTmpPath(path))
            {
                if (!CanMutateVolatilePath(path, false, true, out error))
                    return false;
                bool ok = TmpFs.TryDeleteDirectory(path, recursive, out error);
                if (ok)
                    RemoveOwner(path, recursive);
                return ok;
            }

            error = null;
            return false;
        }
    }
}
