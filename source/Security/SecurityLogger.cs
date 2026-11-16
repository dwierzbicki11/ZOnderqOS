using System;
using System.IO;

namespace ZonderqOS
{
    public static class SecurityLogger
    {
        private struct PendingAuditEntry
        {
            public string Timestamp;
            public string Severity;
            public string User;
            public string Message;
        }

        private const int PendingAuditCapacity = 32;
        private static readonly PendingAuditEntry[] PendingAuditEntries = new PendingAuditEntry[PendingAuditCapacity];
        private static int pendingAuditHead;
        private static int pendingAuditCount;

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

        /// <summary>
        /// Buffers routine Lock/Unlock session transitions in RAM without per-cycle
        /// FAT32 cluster allocations. Entries flush cleanly on Flush() or the next
        /// persistent audit event.
        /// </summary>
        internal static void LogSessionLockEvent(string message)
        {
            string normalizedMessage = message ?? string.Empty;
            SystemLogger.LogBuffered(SystemLogLevel.Info, "SEC", normalizedMessage);

            try
            {
                string currentUser = SecurityContext.CurrentUser ?? "unknown";
                string timestamp = GetTimestamp();
                lock (Sync)
                {
                    PendingAuditEntry entry = new PendingAuditEntry
                    {
                        Timestamp = timestamp,
                        Severity = "INFO",
                        User = currentUser,
                        Message = normalizedMessage
                    };

                    if (pendingAuditCount == PendingAuditCapacity)
                    {
                        PendingAuditEntries[pendingAuditHead] = entry;
                        pendingAuditHead = (pendingAuditHead + 1) % PendingAuditCapacity;
                    }
                    else
                    {
                        int tail = (pendingAuditHead + pendingAuditCount) % PendingAuditCapacity;
                        PendingAuditEntries[tail] = entry;
                        pendingAuditCount++;
                    }
                }
            }
            catch
            {
                // Logging must never panic the kernel.
            }
        }

        public static void Flush()
        {
            try
            {
                lock (Sync)
                {
                    FlushPendingLocked();
                }
            }
            catch
            {
                // Logging must never panic the kernel.
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
                    FlushPendingLocked();
                    AppendFormattedEntryLocked(logEntry);
                }
            }
            catch
            {
                // Logging must never panic the kernel.
            }
        }

        private static void FlushPendingLocked()
        {
            if (pendingAuditCount == 0)
                return;

            for (int i = 0; i < pendingAuditCount; i++)
            {
                int index = (pendingAuditHead + i) % PendingAuditCapacity;
                PendingAuditEntry pending = PendingAuditEntries[index];
                PendingAuditEntries[index] = default(PendingAuditEntry);
                string formatted = $"[{pending.Timestamp}] [{pending.Severity}] [UID:{pending.User}] {pending.Message}\n";
                AppendFormattedEntryLocked(formatted);
            }
            pendingAuditHead = 0;
            pendingAuditCount = 0;
        }

        private static void AppendFormattedEntryLocked(string logEntry)
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

        private static SystemLogLevel MapSeverity(string severity)
        {
            if (string.IsNullOrEmpty(severity))
                return SystemLogLevel.Info;

            if (string.Equals(severity, "CRITICAL", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(severity, "FATAL", StringComparison.OrdinalIgnoreCase))
                return SystemLogLevel.Critical;
            if (string.Equals(severity, "ERROR", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(severity, "ERR", StringComparison.OrdinalIgnoreCase))
                return SystemLogLevel.Error;
            if (string.Equals(severity, "WARN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(severity, "WARNING", StringComparison.OrdinalIgnoreCase))
                return SystemLogLevel.Warning;
            if (string.Equals(severity, "DEBUG", StringComparison.OrdinalIgnoreCase))
                return SystemLogLevel.Debug;
            return SystemLogLevel.Info;
        }

        private static string GetTimestamp()
        {
            return SystemLogger.GetTimestampCached();
        }
    }
}
