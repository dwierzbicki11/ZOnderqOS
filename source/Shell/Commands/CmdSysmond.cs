using System;
using System.IO;
using Cosmos.Kernel.Core.Memory;
using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public class CmdSysmond : ICommand
    {
        public string Name { get; } = "sysmond";
        public string Description { get; } = "Spawns background telemetry daemon (mem logger)";

        private const long MaxLogBytes = 64 * 1024;

        public void Execute(string[] args, ref string currentPath)
        {
            CommandIO.WriteLine("[INFO] Inicjalizacja sysmond w tle...");

            ProcessManager.Start("sysmond", (token) =>
            {
                string logFile = @"/sysmon.log";
                while (!token.IsCancellationRequested)
                {
                    ulong freePages = PageAllocator.FreePageCount;
                    ulong pageSize = PageAllocator.PageSize;
                    ulong freeRamMiB = (freePages * pageSize) / (1024 * 1024);
                    string logEntry = $"[DAEMON-TICK] Wolny RAM: {freeRamMiB} MB\n";

                    try
                    {
                        if (File.Exists(logFile) && new FileInfo(logFile).Length >= MaxLogBytes)
                        {
                            File.WriteAllText(logFile, "=== sysmond log rotated ===\n");
                        }

                        File.AppendAllText(logFile, logEntry);
                    }
                    catch
                    {
                        // Telemetria nie może zatrzymać procesu sysmond.
                    }

                    if (token.WaitHandle.WaitOne(10000)) break;
                }
            });

            CommandIO.WriteLine("[OK] Demon sysmond uruchomiony pomyślnie.");
            CommandIO.LastCommandSuccess = true;
        }
    }
}