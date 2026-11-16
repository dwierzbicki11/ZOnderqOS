using System;
using System.Threading;
using Cosmos.Kernel.Core.Memory;
using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public class CmdSysmond : ICommand
    {
        public string Name { get; } = "sysmond";
        public string Description { get; } = "Spawns background telemetry daemon (in-memory sampler)";

        private static long sampleCount;
        private static ulong lastFreePages;
        private static ulong lastFreeRamMiB;

        public static long SampleCount => Volatile.Read(ref sampleCount);
        public static ulong LastFreePages => Volatile.Read(ref lastFreePages);
        public static ulong LastFreeRamMiB => Volatile.Read(ref lastFreeRamMiB);

        public void Execute(string[] args, ref string currentPath)
        {
            if (ProcessManager.IsRunning("sysmond"))
            {
                CommandIO.WriteLine("[INFO] Demon sysmond już działa (próbki: " + SampleCount + ", wolny RAM: " + LastFreeRamMiB + " MB).");
                CommandIO.LastCommandSuccess = true;
                return;
            }

            CommandIO.WriteLine("[INFO] Inicjalizacja sysmond w tle...");

            int pid = ProcessManager.Start("sysmond", token =>
            {
                while (!token.IsCancellationRequested)
                {
                    // Sample directly into static primitives to avoid periodic
                    // string allocations and 32 KiB FAT32 cluster buffers.
                    ulong freePages = PageAllocator.FreePageCount;
                    ulong pageSize = PageAllocator.PageSize;
                    ulong freeRamMiB = (freePages * pageSize) / (1024UL * 1024UL);

                    Volatile.Write(ref lastFreePages, freePages);
                    Volatile.Write(ref lastFreeRamMiB, freeRamMiB);
                    Interlocked.Increment(ref sampleCount);

                    for (int i = 0; i < 20 && !token.IsCancellationRequested; i++)
                        Thread.Sleep(500);
                }
            });

            CommandIO.WriteLine($"[OK] Demon sysmond uruchomiony pomyślnie (PID {pid}).");
            CommandIO.LastCommandSuccess = true;
        }
    }
}
