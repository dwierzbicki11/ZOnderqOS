using System;
using System.IO;
using System.Threading;
using Cosmos.Kernel.Core.Memory;

namespace ZonderqOS.SystemCore
{
    public static class SystemGuardian
    {
        private const ulong RamWarningThresholdPercent = 25;
        private const ulong RamCriticalThresholdPercent = 15;
        private const int MaxLogByteLength = 1024 * 64;
        private const int LogMaintenanceCycles = 15; // 15 * 4s ~= once per minute
        private static readonly TimeSpan RamWarningInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan RamCriticalInterval = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan FaultLogInterval = TimeSpan.FromMinutes(1);

        private static readonly object StatusLock = new object();
        private static long cycleCount;
        private static ulong lastFreePercent = 100;
        private static long warningCount;
        private static long criticalCount;
        private static long logTruncationCount;
        private static long faultCount;
        private static string lastFault = string.Empty;
        private static string lastCheckTime = "never";

        public static long CycleCount { get { lock (StatusLock) return cycleCount; } }
        public static ulong LastFreePercent { get { lock (StatusLock) return lastFreePercent; } }
        public static long WarningCount { get { lock (StatusLock) return warningCount; } }
        public static long CriticalCount { get { lock (StatusLock) return criticalCount; } }
        public static long LogTruncationCount { get { lock (StatusLock) return logTruncationCount; } }
        public static long FaultCount { get { lock (StatusLock) return faultCount; } }
        public static string LastFault { get { lock (StatusLock) return lastFault; } }
        public static string LastCheckTime { get { lock (StatusLock) return lastCheckTime; } }
        public static bool IsRunning => ProcessManager.IsRunning("sys_guardian");

        public static void Initialize()
        {
            if (ProcessManager.IsRunning("sys_guardian"))
                return;

            ProcessManager.Start("sys_guardian", token =>
            {
                DateTime lastWarningAlert = DateTime.MinValue;
                DateTime lastCriticalAlert = DateTime.MinValue;
                DateTime lastFaultLog = DateTime.MinValue;
                int logMaintenanceCounter = 0;

                SystemLogger.Log(SystemLogLevel.Info, "GUARDIAN", "System guardian started.");

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        ulong totalPages = PageAllocator.TotalPageCount;
                        ulong freePages = PageAllocator.FreePageCount;
                        DateTime now = DateTime.UtcNow;

                        if (freePages > totalPages)
                            freePages = totalPages;

                        ulong freePercent = totalPages == 0
                            ? 0
                            : (freePages * 100) / totalPages;

                        lock (StatusLock)
                        {
                            cycleCount++;
                            lastFreePercent = freePercent;
                            lastCheckTime = FormatTime(now);
                        }

                        if (totalPages == 0)
                        {
                            RecordFault("Page allocator reports zero total pages.");
                        }
                        else if (freePercent <= RamCriticalThresholdPercent &&
                                 now - lastCriticalAlert >= RamCriticalInterval)
                        {
                            string message = "Critical memory pressure: free=" + freePercent + "% (" +
                                             freePages + "/" + totalPages + " pages)";
                            SystemLogger.Log(SystemLogLevel.Critical, "RAM", message);
                            Disk.AppendFile("/sysmon.log", "[CRITICAL][RAM] " + message + "\n");
                            lock (StatusLock)
                                criticalCount++;
                            lastCriticalAlert = now;
                        }
                        else if (freePercent <= RamWarningThresholdPercent &&
                                 now - lastWarningAlert >= RamWarningInterval)
                        {
                            string message = "Memory pressure warning: free=" + freePercent + "% (" +
                                             freePages + "/" + totalPages + " pages)";
                            SystemLogger.Log(SystemLogLevel.Warning, "RAM", message);
                            Disk.AppendFile("/sysmon.log", "[WARN][RAM] " + message + "\n");
                            lock (StatusLock)
                                warningCount++;
                            lastWarningAlert = now;
                        }

                        // OrionGC performs its own collection from the allocator slow path.
                        // Do not call or request a manual collection from a scheduled thread:
                        // forcing a full mark/sweep outside the allocator path has caused
                        // #PF/#GP crashes in the GUI/input workload. Memory stability here is
                        // maintained by bounded buffers and reusable render/snapshot objects.

                        logMaintenanceCounter++;
                        if (logMaintenanceCounter >= LogMaintenanceCycles)
                        {
                            logMaintenanceCounter = 0;
                            const string logPath = "/sysmon.log";
                            if (File.Exists(logPath))
                            {
                                try
                                {
                                    FileInfo logInfo = new FileInfo(logPath);
                                    if (logInfo.Length > MaxLogByteLength)
                                    {
                                        Disk.CreateFile(logPath, "[GUARDIAN] Log file truncated due to size limits.\n");
                                        lock (StatusLock)
                                            logTruncationCount++;
                                        SystemLogger.Log(SystemLogLevel.Warning, "GUARDIAN",
                                            "Legacy /sysmon.log truncated after reaching size limit.");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    RecordFault("Log maintenance: " + ex.Message);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        RecordFault(ex.Message);

                        DateTime faultNow = DateTime.UtcNow;
                        if (faultNow - lastFaultLog >= FaultLogInterval)
                        {
                            SystemLogger.Log(
                                SystemLogLevel.Error,
                                "GUARDIAN",
                                "Guardian cycle failed: " + Safe(ex.Message));
                            lastFaultLog = faultNow;
                        }
                    }

                    if (token.WaitHandle.WaitOne(4000))
                        break;
                }
            });
        }
        private static void RecordFault(string message)
        {
            lock (StatusLock)
            {
                faultCount++;
                lastFault = Safe(message);
            }
        }

        private static string Safe(string message)
        {
            if (string.IsNullOrEmpty(message))
                return "unknown";

            string value = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return value.Length <= 160 ? value : value.Substring(0, 160);
        }

        private static string FormatTime(DateTime value)
        {
            try
            {
                return value.Year.ToString("D4") + "-" +
                       value.Month.ToString("D2") + "-" +
                       value.Day.ToString("D2") + " " +
                       value.Hour.ToString("D2") + ":" +
                       value.Minute.ToString("D2") + ":" +
                       value.Second.ToString("D2") + "Z";
            }
            catch
            {
                return "TIME_ERROR";
            }
        }

    }
}
