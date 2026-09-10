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
            ProcessManager.Start("sys_guardian", (token) =>
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

                        // IMPORTANT: never force OrionGC.Collect() from this background
                        // guardian thread. Cosmos Gen3 performs a collection with CPU
                        // interrupts disabled. Triggering a full collection concurrently
                        // with the GUI/input loop can stall mouse/keyboard processing and
                        // is not a safe memory-pressure mechanism for this OS. Memory
                        // stability is achieved by bounded caches, reusable buffers and
                        // low-allocation rendering instead.

                        // FileInfo is a managed object, so do not create it every four
                        // seconds just to police the log size. Once per minute is enough.
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
