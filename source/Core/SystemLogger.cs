using System;
using System.IO;

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

    public static class SystemLogger
    {
        public const string LogPath = "/var/log/system.log";
        private const string BackupLogPath = "/var/log/system.log.1";
        private const int Capacity = 96;
        private const long MaxLogBytes = 512 * 1024;
        private const int StatRefreshInterval = 32;

        private static readonly object Sync = new object();
        private static readonly SystemLogEntry[] Entries = new SystemLogEntry[Capacity];
        private static int writeIndex;
        private static int count;
        private static long nextSequence = 1;
        private static bool initialized;
        private static long approxLogBytes = -1;
        private static int writesSinceStat;

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
                            long existingLength = new FileInfo(LogPath).Length;
                            if (existingLength > MaxLogBytes)
                            {
                                RotateFileLocked("[SYSTEM] Previous system log rotated at boot.\n");
                            }
                            else
                            {
                                approxLogBytes = existingLength;
                                writesSinceStat = 0;
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        File.WriteAllText(LogPath, string.Empty);
                        approxLogBytes = 0;
                        writesSinceStat = 0;
                    }

                    int start = count == Capacity ? writeIndex : 0;
                    for (int i = 0; i < count; i++)
                    {
                        int index = (start + i) % Capacity;
                        string line = Format(Entries[index]);
                        AppendPersistentLocked(line);
                    }

                    initialized = true;
                }
                catch
                {
                    initialized = false;
                }
            }

            try
            {
                PermissionManager.SetKernelTelemetryAclInternal(LogPath, 600);
            }
            catch { }

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
                        string formatted = Format(entry);
                        RotateIfNeededLocked(formatted.Length + 1);
                        AppendPersistentLocked(formatted);
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
                        approxLogBytes = 0;
                        writesSinceStat = 0;
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

        private static void RotateIfNeededLocked(int nextBytes)
        {
            try
            {
                if (approxLogBytes < 0 || writesSinceStat >= StatRefreshInterval)
                {
                    if (!File.Exists(LogPath))
                    {
                        approxLogBytes = 0;
                        writesSinceStat = 0;
                        return;
                    }
                    approxLogBytes = new FileInfo(LogPath).Length;
                    writesSinceStat = 0;
                }

                if (approxLogBytes + nextBytes > MaxLogBytes)
                {
                    RotateFileLocked("[SYSTEM] Log rotated after reaching size limit.\n");
                }
            }
            catch { }
        }

        private static void RotateFileLocked(string rotatedHeader)
        {
            try
            {
                if (File.Exists(LogPath))
                {
                    if (File.Exists(BackupLogPath))
                        File.Delete(BackupLogPath);
                    File.Move(LogPath, BackupLogPath);
                }
            }
            catch { }

            File.WriteAllText(LogPath, rotatedHeader);
            approxLogBytes = rotatedHeader.Length;
            writesSinceStat = 0;
        }

        private static void AppendPersistentLocked(string formattedLine)
        {
            try
            {
                string line = formattedLine + "\n";
                File.AppendAllText(LogPath, line);
                if (approxLogBytes < 0)
                    approxLogBytes = 0;
                approxLogBytes += line.Length;
                writesSinceStat++;
            }
            catch { }
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
                DateTime now = DateTime.Now;
                return now.Year.ToString("D4") + "-" + now.Month.ToString("D2") + "-" +
                       now.Day.ToString("D2") + " " + now.Hour.ToString("D2") + ":" +
                       now.Minute.ToString("D2") + ":" + now.Second.ToString("D2");
            }
            catch
            {
                return "TIME_ERROR";
            }
        }
    }
}
