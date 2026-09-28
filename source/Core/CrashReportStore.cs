using System;
using System.IO;
using System.Text;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.System.Diagnostics;
using Cosmos.Kernel.System.Storage;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Best-effort persistent panic report. Failures are intentionally contained:
    /// crash reporting must never become a second crash path.
    /// </summary>
    public static class CrashReportStore
    {
        public const string CrashDirectory = "/var/crash";
        public const string LastCrashPath = CrashDirectory + "/last-panic.txt";
        private const string TempCrashPath = CrashDirectory + "/last-panic.txt.new";
        private const string BackupCrashPath = CrashDirectory + "/last-panic.txt.bak";

        private const int MaxRecentLogs = 24;
        private const int MaxMessageLength = 512;
        private const int MaxReportChars = 16 * 1024;

        public static bool TryPersist(Exception exception, string phase, out string error)
        {
            error = string.Empty;

            try
            {
                if (!Directory.Exists("/var"))
                    Directory.CreateDirectory("/var");
                if (!Directory.Exists(CrashDirectory))
                    Directory.CreateDirectory(CrashDirectory);

                string report = BuildReport(exception, phase);
                if (report.Length > MaxReportChars)
                    report = report.Substring(0, MaxReportChars) + "\n[TRUNCATED]\n";

                if (File.Exists(TempCrashPath))
                    File.Delete(TempCrashPath);

                File.WriteAllText(TempCrashPath, report);
                PermissionManager.SetPermission(TempCrashPath, "root", 600);

                if (File.Exists(BackupCrashPath))
                    File.Delete(BackupCrashPath);

                if (File.Exists(LastCrashPath))
                    File.Move(LastCrashPath, BackupCrashPath);

                try
                {
                    File.Move(TempCrashPath, LastCrashPath);
                }
                catch
                {
                    if (!File.Exists(LastCrashPath) && File.Exists(BackupCrashPath))
                        File.Move(BackupCrashPath, LastCrashPath);
                    throw;
                }

                PermissionManager.SetPermission(LastCrashPath, "root", 600);

                if (File.Exists(BackupCrashPath))
                    File.Delete(BackupCrashPath);

                PermissionManager.RemovePermission(TempCrashPath);
                PermissionManager.RemovePermission(BackupCrashPath);
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    if (File.Exists(TempCrashPath))
                        File.Delete(TempCrashPath);
                    PermissionManager.RemovePermission(TempCrashPath);
                }
                catch { }

                error = Safe(ex.Message, 180);
                return false;
            }
        }

        public static bool TryRead(out string report, out string error)
        {
            report = string.Empty;
            error = string.Empty;

            try
            {
                if (!File.Exists(LastCrashPath))
                {
                    error = "No persistent crash report exists.";
                    return false;
                }

                FileInfo info = new FileInfo(LastCrashPath);
                if (info.Length < 0 || info.Length > 64 * 1024)
                {
                    error = "Crash report size is invalid.";
                    return false;
                }

                report = File.ReadAllText(LastCrashPath);
                return true;
            }
            catch (Exception ex)
            {
                error = "Cannot read crash report: " + Safe(ex.Message, 180);
                return false;
            }
        }

        public static bool TryClear(out string error)
        {
            error = string.Empty;

            if (!SecurityContext.IsAuthenticated ||
                SecurityContext.CurrentUid != 0 ||
                !string.Equals(SecurityContext.CurrentUser, "root", StringComparison.Ordinal))
            {
                error = "Only authenticated root can clear crash reports.";
                return false;
            }

            try
            {
                if (File.Exists(LastCrashPath))
                    File.Delete(LastCrashPath);

                PermissionManager.RemovePermission(LastCrashPath);
                return true;
            }
            catch (Exception ex)
            {
                error = "Cannot clear crash report: " + Safe(ex.Message, 180);
                return false;
            }
        }

        private static string BuildReport(Exception exception, string phase)
        {
            StringBuilder text = new StringBuilder(4096);

            text.AppendLine("=== ZOnderqOS Gen 3 Persistent Panic Report ===");
            text.AppendLine("Generated=" + Timestamp());
            text.AppendLine("Phase=" + Safe(phase, 64));
            text.AppendLine("ExceptionType=" + Safe(exception == null ? "Exception" : exception.GetType().Name, 128));
            text.AppendLine("Message=" + Safe(exception == null ? "Unknown fatal error." : exception.Message, MaxMessageLength));
            text.AppendLine();

            AppendMemory(text);
            AppendScheduler(text);
            AppendStorage(text);
            AppendRecentLogs(text);

            text.AppendLine();
            text.AppendLine("[PRIVACY]");
            text.AppendLine("UserIdentity=excluded");
            text.AppendLine("CommandHistory=excluded");
            text.AppendLine("NetworkIdentifiers=excluded");
            text.AppendLine("SerialNumbers=excluded");

            return text.ToString();
        }

        private static void AppendMemory(StringBuilder text)
        {
            text.AppendLine("[MEMORY]");
            try
            {
                ulong total = PageAllocator.TotalPageCount;
                ulong free = PageAllocator.FreePageCount;
                ulong used = total >= free ? total - free : 0;
                text.AppendLine("TotalPages=" + total);
                text.AppendLine("UsedPages=" + used);
                text.AppendLine("FreePages=" + free);
                text.AppendLine("PageSize=" + PageAllocator.PageSize);
            }
            catch
            {
                text.AppendLine("State=unavailable");
            }
            text.AppendLine();
        }

        private static void AppendScheduler(StringBuilder text)
        {
            text.AppendLine("[SCHEDULER]");
            try
            {
                text.AppendLine("Name=" + Safe(SchedulerInfo.SchedulerName, 96));
                text.AppendLine("CpuCount=" + SchedulerInfo.CpuCount);
                text.AppendLine("ThreadSlots=" + SchedulerInfo.ThreadSlotCount);
            }
            catch
            {
                text.AppendLine("State=unavailable");
            }
            text.AppendLine();
        }

        private static void AppendStorage(StringBuilder text)
        {
            text.AppendLine("[STORAGE]");
            try
            {
                text.AppendLine("Devices=" + StorageManager.DeviceCount);
                text.AppendLine("Partitions=" +
                    (StorageManager.Partitions == null ? 0 : StorageManager.Partitions.Count));
            }
            catch
            {
                text.AppendLine("State=unavailable");
            }
            text.AppendLine();
        }

        private static void AppendRecentLogs(StringBuilder text)
        {
            text.AppendLine("[RECENT_SYSTEM_LOG]");
            int available;
            try
            {
                available = SystemLogger.Count;
            }
            catch
            {
                text.AppendLine("(unavailable)");
                return;
            }

            int take = Math.Min(MaxRecentLogs, available);
            for (int offset = take - 1; offset >= 0; offset--)
            {
                try
                {
                    SystemLogEntry entry;
                    if (!SystemLogger.TryGetRecent(offset, out entry))
                        continue;

                    text.AppendLine(
                        "#" + entry.Sequence +
                        " [" + entry.Level + "]" +
                        " [" + Safe(entry.Source, 16) + "] " +
                        Safe(entry.Message, 240));
                }
                catch
                {
                    text.AppendLine("(log entry unavailable)");
                }
            }

            if (take == 0)
                text.AppendLine("(none)");
        }

        private static string Timestamp()
        {
            try
            {
                DateTime now = DateTime.Now;
                return now.Year.ToString("D4") + "-" +
                       now.Month.ToString("D2") + "-" +
                       now.Day.ToString("D2") + " " +
                       now.Hour.ToString("D2") + ":" +
                       now.Minute.ToString("D2") + ":" +
                       now.Second.ToString("D2");
            }
            catch
            {
                return "TIME_ERROR";
            }
        }

        private static string Safe(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return "n/a";

            string clean = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return clean.Length <= maxLength ? clean : clean.Substring(0, maxLength);
        }
    }
}
