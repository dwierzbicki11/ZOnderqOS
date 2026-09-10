using System;
using System.IO;
using System.Threading;
using Cosmos.Kernel.Core.Memory;
using CosmosGc = Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector;

namespace ZonderqOS.SystemCore
{
    public static class SystemGuardian
    {
        private const ulong RamCriticalThresholdPercent = 15;
        private const int MaxLogByteLength = 1024 * 64;

        // 2048 x 4 KiB = 8 MiB. If transient managed allocations consume this
        // much RAM since the last stable sample, request one collection and
        // establish a new baseline. This avoids collecting every frame while
        // preventing small render-time allocations from accumulating forever.
        private const ulong ManagedDriftCollectionPages = 2048;
        private const int LogMaintenanceCycles = 15; // 15 * 4s ~= once per minute
        private static readonly TimeSpan RamAlertInterval = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan MinimumGcInterval = TimeSpan.FromSeconds(15);

        public static void Initialize()
        {
            ProcessManager.Start("sys_guardian", (token) =>
            {
                DateTime lastRamAlert = DateTime.MinValue;
                DateTime lastManagedCollection = DateTime.MinValue;
                ulong stableFreePages = 0;
                int logMaintenanceCounter = 0;

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        ulong totalPages = PageAllocator.TotalPageCount;
                        ulong freePages = PageAllocator.FreePageCount;

                        if (totalPages > 0)
                        {
                            if (stableFreePages == 0 || freePages > stableFreePages)
                                stableFreePages = freePages;

                            DateTime now = DateTime.UtcNow;
                            ulong driftPages = stableFreePages > freePages ? stableFreePages - freePages : 0;

                            if (CosmosGc.IsEnabled && driftPages >= ManagedDriftCollectionPages &&
                                now - lastManagedCollection >= MinimumGcInterval)
                            {
                                // Cosmos Gen3 uses OrionGC. Collect only on meaningful
                                // memory drift, never per-frame. Live app memory remains;
                                // only unreachable temporary GUI objects are reclaimed.
                                CosmosGc.Collect();
                                lastManagedCollection = now;
                                stableFreePages = PageAllocator.FreePageCount;
                                freePages = stableFreePages;
                            }

                            ulong freePercent = (freePages * 100) / totalPages;
                            if (freePercent <= RamCriticalThresholdPercent &&
                                now - lastRamAlert >= RamAlertInterval)
                            {
                                string ramAlert = $"[CRITICAL][RAM] Niski stan pamięci! Wolne: {freePercent}% ({freePages}/{totalPages} stron)\n";
                                Disk.AppendFile("/sysmon.log", ramAlert);
                                lastRamAlert = now;
                            }
                        }

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
