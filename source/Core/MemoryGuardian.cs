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
        private static readonly TimeSpan RamAlertInterval = TimeSpan.FromMinutes(1);

        public static void Initialize()
        {
            ProcessManager.Start("sys_guardian", (token) =>
            {
                DateTime lastRamAlert = DateTime.MinValue;

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        ulong totalPages = PageAllocator.TotalPageCount;
                        ulong freePages = PageAllocator.FreePageCount;

                        if (totalPages > 0)
                        {
                            ulong freePercent = (freePages * 100) / totalPages;
                            if (freePercent <= RamCriticalThresholdPercent &&
                                DateTime.UtcNow - lastRamAlert >= RamAlertInterval)
                            {
                                string ramAlert = $"[CRITICAL][RAM] Niski stan pamięci! Wolne: {freePercent}% ({freePages}/{totalPages} stron)\n";
                                Disk.AppendFile("/sysmon.log", ramAlert);
                                lastRamAlert = DateTime.UtcNow;
                            }
                        }

                        string logPath = "/sysmon.log";
                        if (File.Exists(logPath))
                        {
                            try
                            {
                                FileInfo logInfo = new FileInfo(logPath);
                                if (logInfo.Length > MaxLogByteLength)
                                {
                                    Disk.CreateFile(logPath, "[GUARDIAN] Log file truncated due to size limits.\n");
                                }
                            }
                            catch
                            {
                            }
                        }
                    }
                    catch
                    {
                    }

                    if (token.WaitHandle.WaitOne(4000))
                    {
                        break;
                    }
                }
            });
        }
    }
}