using System;
using System.IO;
using Cosmos.Kernel.HAL.X64.Devices.Clock;

namespace ZonderqOS
{
    public enum SystemLogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        Critical = 4
    }

    public struct SystemLogEntry
    {
        public long Sequence;
        public SystemLogLevel Level;
        public string Source;
        public string Message;
        public string TimeText;
    }

    /// <summary>
    /// General-purpose system log used by kernel, GUI and services. The in-memory
    /// ring stays bounded and remains useful even when the filesystem is unavailable.
    /// Persistent logging is best-effort and must never become a source of kernel faults.
    /// </summary>
    public static class SystemLogger
    {
        public const string LogPath = "/var/log/system.log";
        private const int Capacity = 96;
        private const long MaxLogBytes = 512 * 1024;

        private static readonly object Sync = new object();
        private static readonly SystemLogEntry[] Entries = new SystemLogEntry[Capacity];
        private static int writeIndex;
        private static int count;
        private static long nextSequence = 1;
        private static bool initialized;

        public static int Count
        {
            get
            {
                lock (Sync)
                    return count;
            }
        }

        public static void Initialize()
        {
            lock (Sync)
            {
                if (initialized)
                    return;

                try
                {
                    if (!Directory.Exists("/var/log"))
                        Directory.CreateDirectory("/var/log");

                    if (File.Exists(LogPath))
                    {
                        try
                        {
                            if (new FileInfo(LogPath).Length > MaxLogBytes)
                                File.WriteAllText(LogPath, "[SYSTEM] Previous system log rotated at boot.\n");
                        }
                        catch
                        {
                        }
                    }
                    else
                    {
                        File.WriteAllText(LogPath, string.Empty);
                    }

                    // Flush messages collected before VFS/log initialization. This keeps
                    // early boot diagnostics without forcing file IO before storage is ready.
                    int start = count == Capacity ? writeIndex : 0;
                    for (int i = 0; i < count; i++)
                    {
                        int index = (start + i) % Capacity;
                        AppendPersistentLocked(Entries[index]);
                    }

                    initialized = true;
                }
                catch
                {
                    // The memory ring is still valid even if persistent storage failed.
                    initialized = false;
                }
            }

            try
            {
                PermissionManager.SetPermission(LogPath, "root", 600);
            }
            catch
            {
            }

            Log(SystemLogLevel.Info, "LOGGER", "Central system logger initialized.");
        }

        public static void Log(SystemLogLevel level, string source, string message)
        {
            try
            {
                string normalizedSource = NormalizeSource(source);
                string normalizedMessage = NormalizeMessage(message);
                string timestamp = GetTimestamp();

                lock (Sync)
                {
                    SystemLogEntry entry = new SystemLogEntry
                    {
                        Sequence = nextSequence++,
                        Level = level,
                        Source = normalizedSource,
                        Message = normalizedMessage,
                        TimeText = timestamp
                    };

                    Entries[writeIndex] = entry;
                    writeIndex = (writeIndex + 1) % Capacity;
                    if (count < Capacity)
                        count++;

                    if (initialized)
                    {
                        RotateIfNeededLocked(entry);
                        AppendPersistentLocked(entry);
                    }
                }
            }
            catch
            {
                // Logging is diagnostic infrastructure; failures must stay contained.
            }
        }

        public static bool TryGetRecent(int newestOffset, out SystemLogEntry entry)
        {
            lock (Sync)
            {
                if (newestOffset < 0 || newestOffset >= count)
                {
                    entry = default(SystemLogEntry);
                    return false;
                }

                int index = writeIndex - 1 - newestOffset;
                while (index < 0)
                    index += Capacity;
                entry = Entries[index % Capacity];
                return true;
            }
        }

        public static string Format(SystemLogEntry entry)
        {
            return "[" + (entry.TimeText ?? "TIME_ERROR") + "] [" + LevelName(entry.Level) + "] [" +
                   (entry.Source ?? "SYS") + "] " + (entry.Message ?? string.Empty);
        }

        public static bool Clear()
        {
            bool success = true;
            lock (Sync)
            {
                for (int i = 0; i < Entries.Length; i++)
                    Entries[i] = default(SystemLogEntry);

                writeIndex = 0;
                count = 0;

                if (initialized)
                {
                    try
                    {
                        File.WriteAllText(LogPath, string.Empty);
                    }
                    catch
                    {
                        success = false;
                    }
                }
            }

            Log(SystemLogLevel.Warning, "LOGGER", "System log cleared by administrator.");
            return success;
        }

        private static void RotateIfNeededLocked(SystemLogEntry nextEntry)
        {
            try
            {
                if (!File.Exists(LogPath))
                    return;

                long estimated = new FileInfo(LogPath).Length + Format(nextEntry).Length + 1;
                if (estimated > MaxLogBytes)
                    File.WriteAllText(LogPath, "[SYSTEM] Log rotated after reaching size limit.\n");
            }
            catch
            {
            }
        }

        private static void AppendPersistentLocked(SystemLogEntry entry)
        {
            try
            {
                File.AppendAllText(LogPath, Format(entry) + "\n");
            }
            catch
            {
            }
        }

        private static string NormalizeSource(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return "SYS";

            source = source.Trim().ToUpperInvariant();
            return source.Length <= 16 ? source : source.Substring(0, 16);
        }

        private static string NormalizeMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return string.Empty;

            return message.Replace('\r', ' ').Replace('\n', ' ');
        }

        private static string LevelName(SystemLogLevel level)
        {
            switch (level)
            {
                case SystemLogLevel.Debug: return "DEBUG";
                case SystemLogLevel.Warning: return "WARN";
                case SystemLogLevel.Error: return "ERROR";
                case SystemLogLevel.Critical: return "CRITICAL";
                default: return "INFO";
            }
        }

        private static string GetTimestamp()
        {
            try
            {
                var (year, month, day, hour, minute, second) = RTC.ReadTime();
                return "20" + year.ToString("D2") + "-" + month.ToString("D2") + "-" +
                       day.ToString("D2") + " " + hour.ToString("D2") + ":" +
                       minute.ToString("D2") + ":" + second.ToString("D2");
            }
            catch
            {
                return "TIME_ERROR";
            }
        }
    }
}
