namespace ZonderqOS.SystemCore
{
    public static class VirtualFs
    {
        public static bool IsVirtualPath(string path)
        {
            return ProcFs.IsProcPath(path) || SysFs.IsSysPath(path) || DeviceFs.IsDevicePath(path);
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

            entries = null;
            return false;
        }

        public static bool TryWrite(string path, string content)
        {
            if (DeviceFs.IsDevicePath(path))
                return DeviceFs.TryWrite(path, content);

            return false;
        }
    }
}
