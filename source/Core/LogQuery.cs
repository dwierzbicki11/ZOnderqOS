using System;
using System.IO;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Allocation-bounded tail/filter reader shared by shell and GUI log viewers.
    /// The caller owns the destination buffer; only the newest matching lines are kept.
    /// </summary>
    public static class LogQuery
    {
        public static int ReadTail(
            string path,
            string[] destination,
            int maxLines,
            string contains,
            out long fileBytes,
            out string error)
        {
            fileBytes = 0;
            error = string.Empty;

            if (destination == null || destination.Length == 0)
            {
                error = "Destination buffer is empty.";
                return 0;
            }

            if (maxLines < 1)
                maxLines = 1;
            if (maxLines > destination.Length)
                maxLines = destination.Length;

            for (int i = 0; i < destination.Length; i++)
                destination[i] = null;

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                error = "Log file does not exist.";
                return 0;
            }

            try
            {
                FileInfo info = new FileInfo(path);
                fileBytes = info.Length;

                string filter = NormalizeFilter(contains);
                int matched = 0;

                using (StreamReader reader = new StreamReader(path))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (!Matches(line, filter))
                            continue;

                        destination[matched % maxLines] = line;
                        matched++;
                    }
                }

                int keep = Math.Min(matched, maxLines);
                if (matched > maxLines)
                {
                    int start = matched % maxLines;
                    if (start != 0)
                    {
                        Reverse(destination, 0, start - 1);
                        Reverse(destination, start, maxLines - 1);
                        Reverse(destination, 0, maxLines - 1);
                    }
                }

                return keep;
            }
            catch (Exception ex)
            {
                error = Safe(ex.Message);
                return 0;
            }
        }

        public static string ResolveSource(string source)
        {
            string key = string.IsNullOrWhiteSpace(source)
                ? "system"
                : source.Trim().ToLowerInvariant();

            switch (key)
            {
                case "auth":
                case "security":
                    return "/var/log/auth.log";
                case "guardian":
                case "sysmon":
                    return "/sysmon.log";
                case "error":
                case "legacy":
                    return "/var/error_log.txt";
                default:
                    return SystemLogger.LogPath;
            }
        }

        public static bool IsKnownSource(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return true;

            switch (source.Trim().ToLowerInvariant())
            {
                case "system":
                case "kernel":
                case "auth":
                case "security":
                case "guardian":
                case "sysmon":
                case "error":
                case "legacy":
                    return true;
                default:
                    return false;
            }
        }


        private static void Reverse(string[] values, int left, int right)
        {
            while (left < right)
            {
                string temp = values[left];
                values[left] = values[right];
                values[right] = temp;
                left++;
                right--;
            }
        }

        private static string NormalizeFilter(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string clean = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return clean.Length <= 96 ? clean : clean.Substring(0, 96);
        }

        private static bool Matches(string line, string filter)
        {
            if (string.IsNullOrEmpty(filter))
                return true;
            if (line == null)
                return false;

            return line.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "unknown";

            string clean = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return clean.Length <= 160 ? clean : clean.Substring(0, 160);
        }
    }
}
