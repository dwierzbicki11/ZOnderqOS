using System;
using System.IO;
using System.Threading;
using Cosmos.Kernel.Core.Memory;
using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public class CmdSysmond : ICommand
    {
        public string Name { get; } = "sysmond";
        public string Description { get; } = "Spawns background telemetry daemon (mem logger)";

        public void Execute(string[] args, ref string currentPath)
        {
            Console.WriteLine("[INFO] Inicjalizacja sysmond w tle...");

            ProcessManager.Start("sysmond", (token) => 
            {
                string logFile = @"/sysmon.log"; 
                
                while (!token.IsCancellationRequested)
                {
                    ulong freePages = PageAllocator.FreePageCount;
                    ulong pageSize = PageAllocator.PageSize;
                    ulong freeRamMiB = (freePages * pageSize) / (1024 * 1024);

                    string logEntry = $"[DAEMON-TICK] Wolny RAM: {freeRamMiB} MB\n";
                    try { File.AppendAllText(logFile, logEntry); } catch {}

                    // Czeka 10 sekund, ale natychmiast przerywa odliczanie, gdy użytkownik wywoła 'kill'
                    if (token.WaitHandle.WaitOne(10000)) break;
                }
            });

            Console.WriteLine("[OK] Demon sysmond uruchomiony pomyślnie.");
        }
    }
}