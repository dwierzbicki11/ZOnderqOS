using System;
using System.IO;

namespace ZonderqOS
{
    public static class SecurityLogger
    {
        private static string LogPath = @"/var/log/auth.log";
        private static readonly string BackupLogPath = @"/var/log/auth.log.1";
        private const long MaxLogSize = 256 * 1024;
        private const int StatRefreshInterval = 32;
        private static readonly object Sync = new object();
        private static long approxLogBytes = -1;
        private static int writesSinceStat;

        public static void Initialize()
        {
            try
            {
                if (!Directory.Exists("/var/log")) Directory.CreateDirectory("/var/log");

                lock (Sync)
                {
                    if (!File.Exists(LogPath))
                    {
                        string initial = $"[{GetTimestamp()}] [SYS] Security audit log initialized.\n";
                        File.WriteAllText(LogPath, initial);
                        approxLogBytes = initial.Length;
                    }
                    else
                    {
                        approxLogBytes = new FileInfo(LogPath).Length;
                    }
                    writesSinceStat = 0;
                }
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Audit logger init failed: {ex.Message}", "SEC");
            }
        }

        public static void LogEvent(string severity, string message)
        {
            SystemLogger.Log(MapSeverity(severity), "SEC", message);

            try
            {
                string currentUser = SecurityContext.CurrentUser ?? "unknown";
                string logEntry = $"[{GetTimestamp()}] [{severity}] [UID:{currentUser}] {message}\n";

                lock (Sync)
                {
                    if (approxLogBytes < 0 || writesSinceStat >= StatRefreshInterval)
                    {
                        approxLogBytes = File.Exists(LogPath) ? new FileInfo(LogPath).Length : 0;
                        writesSinceStat = 0;
                    }

                    if (approxLogBytes + logEntry.Length > MaxLogSize)
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

                        string rotatedHeader = $"[{GetTimestamp()}] [SYS] Security audit log rotated.\n";
                        File.WriteAllText(LogPath, rotatedHeader);
                        approxLogBytes = rotatedHeader.Length;
                        writesSinceStat = 0;
                    }

                    File.AppendAllText(LogPath, logEntry);
                    approxLogBytes += logEntry.Length;
                    writesSinceStat++;
                }
            }
            catch
            {
                // Logging must never panic the kernel.
            }
        }

        private static SystemLogLevel MapSeverity(string severity)
        {
            if (string.IsNullOrEmpty(severity))
                return SystemLogLevel.Info;

            string value = severity.ToUpperInvariant();
            if (value == "CRITICAL" || value == "FATAL") return SystemLogLevel.Critical;
            if (value == "ERROR" || value == "ERR") return SystemLogLevel.Error;
            if (value == "WARN" || value == "WARNING") return SystemLogLevel.Warning;
            if (value == "DEBUG") return SystemLogLevel.Debug;
            return SystemLogLevel.Info;
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
