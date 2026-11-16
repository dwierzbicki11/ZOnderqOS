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
        private static DateTime lastCheckTimeUtc = DateTime.MinValue;
        private static long formattedCheckTimeTicks = -1;
        private static string lastCheckTime = "never";
        private static bool logWrittenThisSession = false;

        public static long CycleCount { get { lock (StatusLock) return cycleCount; } }
        public static ulong LastFreePercent { get { lock (StatusLock) return lastFreePercent; } }
        public static long WarningCount { get { lock (StatusLock) return warningCount; } }
        public static long CriticalCount { get { lock (StatusLock) return criticalCount; } }
        public static long LogTruncationCount { get { lock (StatusLock) return logTruncationCount; } }
        public static long FaultCount { get { lock (StatusLock) return faultCount; } }
        public static string LastFault { get { lock (StatusLock) return lastFault; } }
        public static string LastCheckTime
        {
            get
            {
                DateTime checkedAt;
                lock (StatusLock)
                {
                    checkedAt = lastCheckTimeUtc;
                    if (checkedAt == DateTime.MinValue)
                        return "never";
                    if (checkedAt.Ticks == formattedCheckTimeTicks)
                        return lastCheckTime;
                    lastCheckTime = FormatTime(checkedAt);
                    formattedCheckTimeTicks = checkedAt.Ticks;
                    return lastCheckTime;
                }
            }
        }
        public static bool IsRunning => ProcessManager.IsRunning("sys_guardian");

        public static void Initialize()
        {
            if (ProcessManager.IsRunning("sys_guardian"))
                return;

            ProcessManager.Start("sys_guardian", token =>
            {
                DateTime lastWarningAlert = DateTime.MinValue;
                DateTime lastCriticalAlert = DateTime.MinValue;

                SystemLogger.Log(SystemLogLevel.Info, "GUARDIAN", "System guardian started.");

                int guardianTick = 0;
                while (!token.IsCancellationRequested)
                {
                    // Existing memory alarms keep their 4-second cadence.
                    if ((guardianTick++ & 1) == 0)
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
                            lastCheckTimeUtc = now;
                        }

                        // Keep low-memory tracking strictly in-memory inside the periodic
                        // guardian loop. Appending to /sysmon.log or /var/log/system.log or
                        // polling FileInfo("/sysmon.log") on a 32 KiB-cluster FAT32 partition
                        // allocates 32 KiB cluster buffers (> OrionGC's max free-list class),
                        // forcing new 64 KiB GCSegments from PageAllocator whenever free RAM is low.
                        if (totalPages == 0)
                        {
                            RecordFault("Page allocator reports zero total pages.");
                        }
                        else if (freePercent <= RamCriticalThresholdPercent &&
                                 now - lastCriticalAlert >= RamCriticalInterval)
                        {
                            lock (StatusLock)
                                criticalCount++;
                            lastCriticalAlert = now;
                        }
                        else if (freePercent <= RamWarningThresholdPercent &&
                                 now - lastWarningAlert >= RamWarningInterval)
                        {
                            lock (StatusLock)
                                warningCount++;
                            lastWarningAlert = now;
                        }

                        // OrionGC performs its own collection from the allocator slow path.
                        // Do not call or request a manual collection from a scheduled thread:
                        // forcing a full mark/sweep outside the allocator path has caused
                        // #PF/#GP crashes in the GUI/input workload. Memory stability here is
                        // maintained by bounded buffers and reusable render/snapshot objects.
                    }
                    catch (Exception ex)
                    {
                        RecordFault(ex.Message);
                    }
                    }

                    // Same sys_guardian thread: policy evaluates every 10s,
                    // without spawning an additional managed worker.
                    MemoryOptimizer.SampleIfDue(
                        System.Diagnostics.Stopwatch.GetTimestamp(),
                        System.Diagnostics.Stopwatch.Frequency);

                    if (token.WaitHandle.WaitOne(2000))
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
