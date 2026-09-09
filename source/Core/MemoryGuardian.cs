using System;
using System.IO;
using System.Threading;
using Cosmos.Kernel.Core.Memory;

namespace ZonderqOS.SystemCore
{
    public static class SystemGuardian
    {
        private const ulong RamCriticalThresholdPercent = 15; // Alarm, gdy wolny RAM spadnie poniżej 15%
        private const int MaxLogByteLength = 1024 * 64;         // Maksymalny rozmiar pliku logu (64 KB)

        public static void Initialize()
        {
            ProcessManager.Start("sys_guardian", (token) =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        // ==========================================
                        // 1. MONITOROWANIE I OCHRONA PAMIĘCI RAM
                        // ==========================================
                        ulong totalPages = PageAllocator.TotalPageCount;
                        ulong freePages = PageAllocator.FreePageCount;

                        if (totalPages > 0)
                        {
                            ulong freePercent = (freePages * 100) / totalPages;

                            if (freePercent <= RamCriticalThresholdPercent)
                            {
                                string ramAlert = $"[CRITICAL][RAM] Niski stan pamięci! Wolne: {freePercent}% ({freePages}/{totalPages} stron)\n";
                                Disk.AppendFile("/sysmon.log", ramAlert); // Korzystamy z bezpiecznej metody Disk[cite: 6]
                            }
                        }

                        // ==========================================
                        // 2. MONITOROWANIE I OCHRONA DYSKU / VFS (Logi)
                        // ==========================================
                        string logPath = "/sysmon.log";
                        if (File.Exists(logPath))
                        {
                            try
                            {
                                byte[] logBytes = File.ReadAllBytes(logPath);
                                if (logBytes.Length > MaxLogByteLength)
                                {
                                    string rotationNotice = "[GUARDIAN] Log file truncated due to size limits.\n";
                                    // Używamy Disk.CreateFile zgodnie z definicją w klasie Disk[cite: 6]
                                    Disk.CreateFile(logPath, rotationNotice);
                                }
                            }
                            catch
                            {
                                // Wyciszenie błędu I/O dla pojedynczego pliku
                            }
                        }
                    }
                    catch
                    {
                        // Awaryjne wyciszenie wszelkich wyjątków pętli demona
                    }

                    // Sprawdzanie stanu co 4 sekundy z natychmiastową reakcją na sygnał kill
                    if (token.WaitHandle.WaitOne(4000)) break;
                }
            });
        }
    }
}