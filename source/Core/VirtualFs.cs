namespace ZonderqOS.SystemCore
{
    public static class VirtualFs
    {
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
                return DeviceFs.TryWrite(path, content);

            if (RunFs.IsRunPath(path))
                return RunFs.TryWrite(path, content, append, out error);

            if (TmpFs.IsTmpPath(path))
                return TmpFs.TryWrite(path, content, append, out error);

            return false;
        }

        public static bool TryCreateDirectory(string path, out string error)
        {
            if (RunFs.IsRunPath(path))
                return RunFs.TryCreateDirectory(path, out error);

            if (TmpFs.IsTmpPath(path))
                return TmpFs.TryCreateDirectory(path, out error);

            error = null;
            return false;
        }

        public static bool TryDeleteFile(string path, out string error)
        {
            if (RunFs.IsRunPath(path))
                return RunFs.TryDeleteFile(path, out error);

            if (TmpFs.IsTmpPath(path))
                return TmpFs.TryDeleteFile(path, out error);

            error = null;
            return false;
        }

        public static bool TryDeleteDirectory(string path, bool recursive, out string error)
        {
            if (RunFs.IsRunPath(path))
                return RunFs.TryDeleteDirectory(path, recursive, out error);

            if (TmpFs.IsTmpPath(path))
                return TmpFs.TryDeleteDirectory(path, recursive, out error);

            error = null;
            return false;
        }
    }
}
