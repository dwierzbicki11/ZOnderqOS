using System;

namespace ZonderqOS.SystemCore
{
    public static class DeviceFs
    {
        public static bool IsDevicePath(string path)
        {
            string normalized = Normalize(path);
            return normalized == "/dev" || normalized.StartsWith("/dev/", StringComparison.Ordinal);
        }

        public static bool TryList(string path, out string[] entries)
        {
            string normalized = Normalize(path);
            if (normalized == "/dev")
            {
                entries = new[] { "null", "console", "tty" };
                return true;
            }

            entries = null;
            return false;
        }

        public static bool TryRead(string path, out string content)
        {
            string normalized = Normalize(path);
            if (normalized == "/dev/null")
            {
                content = string.Empty;
                return true;
            }

            content = null;
            return false;
        }

        public static bool TryWrite(string path, string content)
        {
            string normalized = Normalize(path);

            if (normalized == "/dev/null")
                return true;

            if (normalized == "/dev/console" || normalized == "/dev/tty")
            {
                Console.WriteLine(content ?? string.Empty);
                return true;
            }

            return false;
        }

        private static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "/";

            string value = path.Replace('\\', '/').Trim();
            while (value.Length > 1 && value.EndsWith("/", StringComparison.Ordinal))
                value = value.Substring(0, value.Length - 1);
            return value;
        }
    }
}
