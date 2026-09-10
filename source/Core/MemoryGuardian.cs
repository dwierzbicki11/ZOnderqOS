using System;
using System.IO;
using System.Threading;
using Cosmos.Kernel.Core.Memory;

namespace ZonderqOS.SystemCore
{
    public static class SystemGuardian
    {
        private const ulong RamCriticalThresholdPercent = 15;
        private const int MaxLogByteLength = 1024 * 64;
        private const int LogMaintenanceCycles = 15; // 15 * 4s ~= once per minute
        private static readonly TimeSpan RamAlertInterval = TimeSpan.FromMinutes(1);

        public static void Initialize()
        {
            if (ProcessManager.IsRunning("sys_guardian"))
                return;

            ProcessManager.Start("sys_guardian", token =>
            {
                DateTime lastRamAlert = DateTime.MinValue;
                int logMaintenanceCounter = 0;

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        ulong totalPages = PageAllocator.TotalPageCount;
                        ulong freePages = PageAllocator.FreePageCount;

                        if (totalPages > 0)
                        {
                            ulong freePercent = (freePages * 100) / totalPages;
                            DateTime now = DateTime.UtcNow;

                            if (freePercent <= RamCriticalThresholdPercent &&
                                now - lastRamAlert >= RamAlertInterval)
                            {
                                string ramAlert = $"[CRITICAL][RAM] Niski stan pamięci! Wolne: {freePercent}% ({freePages}/{totalPages} stron)\n";
                                Disk.AppendFile("/sysmon.log", ramAlert);
                                lastRamAlert = now;
                            }
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
                                        Disk.CreateFile(logPath, "[GUARDIAN] Log file truncated due to size limits.\n");
                                }
                                catch
                                {
                                }
                            }
                        }
                    }
                    catch
                    {
                    }

                    if (token.WaitHandle.WaitOne(4000))
                        break;
                }
            });
        }
    }
}
