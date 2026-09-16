using System;
using System.IO;

namespace ZonderqOS
{
    public static class SecurityLogger
    {
        private static string LogPath = @"/var/log/auth.log";
        private const long MaxLogSize = 256 * 1024;

        public static void Initialize()
        {
            try
            {
                if (!Directory.Exists("/var/log")) Directory.CreateDirectory("/var/log");

                if (!File.Exists(LogPath))
                    File.WriteAllText(LogPath, $"[{GetTimestamp()}] [SYS] Security audit log initialized.\n");
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

                if (File.Exists(LogPath) && new FileInfo(LogPath).Length + logEntry.Length > MaxLogSize)
                    File.WriteAllText(LogPath, $"[{GetTimestamp()}] [SYS] Security audit log rotated.\n");

                File.AppendAllText(LogPath, logEntry);
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
