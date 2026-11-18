using System;
using System.IO;
using System.Text;

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
        private static readonly StringBuilder PendingAuditBuilder = new StringBuilder(1024);
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
                    EnqueuePendingLocked(timestamp, "INFO", currentUser, normalizedMessage);
                }
            }
            catch
            {
                // Logging must never panic the kernel.
            }
        }

        /// <summary>
        /// Logs a graphical unlock failure in RAM without triggering immediate multi-write
        /// FAT32 flushes during casual mistyped passwords. When flushImmediately is true
        /// (e.g., lockout triggered), flushes pending entries in one batched write.
        /// </summary>
        internal static void LogSessionUnlockFailure(string message, bool flushImmediately)
        {
            string normalizedMessage = message ?? string.Empty;
            SystemLogger.LogBuffered(SystemLogLevel.Warning, "SEC", normalizedMessage);

            try
            {
                string currentUser = SecurityContext.CurrentUser ?? "unknown";
                string timestamp = GetTimestamp();
                lock (Sync)
                {
                    EnqueuePendingLocked(timestamp, "WARN", currentUser, normalizedMessage);
                    if (flushImmediately)
                        FlushPendingBatchLocked(false, null, null, null, null);
                }

                if (flushImmediately)
                    SystemLogger.Flush();
            }
            catch
            {
                // Logging must never panic the kernel.
            }
        }

        private static void EnqueuePendingLocked(string timestamp, string severity, string currentUser, string message)
        {
            PendingAuditEntry entry = new PendingAuditEntry
            {
                Timestamp = timestamp,
                Severity = severity,
                User = currentUser,
                Message = message
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
                string timestamp = GetTimestamp();

                lock (Sync)
                {
                    FlushPendingBatchLocked(true, timestamp, severity ?? "INFO", currentUser, message ?? string.Empty);
                }
            }
            catch
            {
                // Logging must never panic the kernel.
            }
        }

        private static void FlushPendingLocked()
        {
            FlushPendingBatchLocked(false, null, null, null, null);
        }

        private static void FlushPendingBatchLocked(bool hasExtra, string extraTimestamp, string extraSeverity,
            string extraUser, string extraMessage)
        {
            if (pendingAuditCount == 0 && !hasExtra)
                return;

            PendingAuditBuilder.Clear();
            try
            {
                for (int i = 0; i < pendingAuditCount; i++)
                {
                    int index = (pendingAuditHead + i) % PendingAuditCapacity;
                    PendingAuditEntry pending = PendingAuditEntries[index];
                    PendingAuditEntries[index] = default(PendingAuditEntry);
                    AppendAuditLine(PendingAuditBuilder, pending.Timestamp, pending.Severity, pending.User, pending.Message);
                }
                pendingAuditHead = 0;
                pendingAuditCount = 0;

                if (hasExtra)
                    AppendAuditLine(PendingAuditBuilder, extraTimestamp, extraSeverity, extraUser, extraMessage);

                if (PendingAuditBuilder.Length > 0)
                    AppendFormattedEntryLocked(PendingAuditBuilder.ToString());
            }
            finally
            {
                PendingAuditBuilder.Clear();
            }
        }

        private static void AppendAuditLine(StringBuilder sb, string timestamp, string severity, string user, string message)
        {
            sb.Append('[');
            sb.Append(timestamp ?? "TIME_ERROR");
            sb.Append("] [");
            sb.Append(severity ?? "INFO");
            sb.Append("] [UID:");
            sb.Append(user ?? "unknown");
            sb.Append("] ");
            sb.Append(message ?? string.Empty);
            sb.Append('\n');
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
