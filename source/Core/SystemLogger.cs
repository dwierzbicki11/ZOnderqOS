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
                PermissionManager.SetPermission(LogPath, "root", 600);
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

        private static readonly char[] TimestampChars = new char[19];
        private static int lastTimestampYear = -1;
        private static int lastTimestampMonth = -1;
        private static int lastTimestampDay = -1;
        private static int lastTimestampHour = -1;
        private static int lastTimestampMinute = -1;
        private static int lastTimestampSecond = -1;
        private static string cachedTimestamp = "1970-01-01 00:00:00";

        private static string NormalizeSource(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return "SYS";

            bool alreadyClean = source.Length <= 16 && source[0] > ' ' && source[source.Length - 1] > ' ';
            if (alreadyClean)
            {
                for (int i = 0; i < source.Length; i++)
                {
                    char c = source[i];
                    if (c >= 'a' && c <= 'z')
                    {
                        alreadyClean = false;
                        break;
                    }
                }
                if (alreadyClean)
                    return source;
            }

            source = source.Trim().ToUpperInvariant();
            return source.Length <= 16 ? source : source.Substring(0, 16);
        }

        private static string NormalizeMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return string.Empty;

            if (message.IndexOf('\r') < 0 && message.IndexOf('\n') < 0)
                return message;

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

        private static void WriteTwoDigits(char[] buf, int offset, int value)
        {
            buf[offset] = (char)('0' + (value / 10) % 10);
            buf[offset + 1] = (char)('0' + (value % 10));
        }

        private static string GetTimestamp()
        {
            try
            {
                DateTime now = DateTime.Now;
                int year = now.Year;
                int month = now.Month;
                int day = now.Day;
                int hour = now.Hour;
                int minute = now.Minute;
                int second = now.Second;

                if (year == lastTimestampYear &&
                    month == lastTimestampMonth &&
                    day == lastTimestampDay &&
                    hour == lastTimestampHour &&
                    minute == lastTimestampMinute &&
                    second == lastTimestampSecond)
                {
                    return cachedTimestamp;
                }

                lastTimestampYear = year;
                lastTimestampMonth = month;
                lastTimestampDay = day;
                lastTimestampHour = hour;
                lastTimestampMinute = minute;
                lastTimestampSecond = second;

                TimestampChars[0] = (char)('0' + (year / 1000) % 10);
                TimestampChars[1] = (char)('0' + (year / 100) % 10);
                TimestampChars[2] = (char)('0' + (year / 10) % 10);
                TimestampChars[3] = (char)('0' + (year % 10));
                TimestampChars[4] = '-';
                WriteTwoDigits(TimestampChars, 5, month);
                TimestampChars[7] = '-';
                WriteTwoDigits(TimestampChars, 8, day);
                TimestampChars[10] = ' ';
                WriteTwoDigits(TimestampChars, 11, hour);
                TimestampChars[13] = ':';
                WriteTwoDigits(TimestampChars, 14, minute);
                TimestampChars[16] = ':';
                WriteTwoDigits(TimestampChars, 17, second);

                cachedTimestamp = new string(TimestampChars);
                return cachedTimestamp;
            }
            catch
            {
                return "TIME_ERROR";
            }
        }
    }
}
