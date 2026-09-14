using System;
using System.Diagnostics;
using Cosmos.Kernel.Core.Memory;

namespace ZonderqOS
{
    /// <summary>
    /// Last-resort console diagnostics for uncaught kernel exceptions. The screen avoids
    /// graphics and filesystem dependencies so it can still be useful during partial failure.
    /// </summary>
    public static class KernelPanic
    {
        private const int RecentLogLines = 10;

        public static bool Show(Exception exception, string phase, bool allowContinue)
        {
            string safePhase = string.IsNullOrEmpty(phase) ? "KERNEL" : phase;
            string message = exception == null ? "Unknown fatal error." : exception.Message;
            string typeName = exception == null ? "Exception" : exception.GetType().Name;

            SystemLogger.Log(SystemLogLevel.Critical, "PANIC",
                safePhase + ": " + typeName + ": " + message);

            try
            {
                Console.BackgroundColor = ConsoleColor.DarkRed;
                Console.ForegroundColor = ConsoleColor.White;
                Console.Clear();

                Console.WriteLine("============================================================");
                Console.WriteLine("                    ZOnderqOS KERNEL PANIC");
                Console.WriteLine("============================================================");
                Console.WriteLine();
                Console.WriteLine("PHASE : " + safePhase);
                Console.WriteLine("TYPE  : " + typeName);
                Console.WriteLine("ERROR : " + message);
                Console.WriteLine("UPTIME: " + GetUptimeSeconds() + " s");
                Console.WriteLine("RAM   : " + GetMemorySummary());
                Console.WriteLine();
                Console.WriteLine("LAST SYSTEM LOG ENTRIES");
                Console.WriteLine("------------------------------------------------------------");

                int available = SystemLogger.Count;
                int take = Math.Min(RecentLogLines, available);
                for (int offset = take - 1; offset >= 0; offset--)
                {
                    SystemLogEntry entry;
                    if (SystemLogger.TryGetRecent(offset, out entry))
                        Console.WriteLine(SystemLogger.Format(entry));
                }

                if (take == 0)
                    Console.WriteLine("No log entries available.");

                Console.WriteLine("------------------------------------------------------------");
                Console.WriteLine();
                Console.WriteLine(allowContinue
                    ? "[R] Reboot   [S] Shutdown   [C] Continue at your own risk"
                    : "[R] Reboot   [S] Shutdown");

                while (true)
                {
                    ConsoleKey key = Console.ReadKey(true).Key;
                    if (key == ConsoleKey.R)
                    {
                        SystemLogger.Log(SystemLogLevel.Warning, "PANIC", "Reboot requested from panic screen.");
                        Cosmos.Kernel.System.Power.Reboot();
                    }
                    else if (key == ConsoleKey.S)
                    {
                        SystemLogger.Log(SystemLogLevel.Warning, "PANIC", "Shutdown requested from panic screen.");
                        Cosmos.Kernel.System.Power.Shutdown();
                    }
                    else if (allowContinue && key == ConsoleKey.C)
                    {
                        SystemLogger.Log(SystemLogLevel.Warning, "PANIC", "Runtime continuation requested after panic.");
                        RestoreConsole();
                        return true;
                    }
                }
            }
            catch
            {
                try
                {
                    RestoreConsole();
                    Console.WriteLine("ZOnderqOS fatal kernel error: " + message);
                    Console.WriteLine("Press R to reboot or S to shut down.");
                    while (true)
                    {
                        ConsoleKey key = Console.ReadKey(true).Key;
                        if (key == ConsoleKey.R)
                            Cosmos.Kernel.System.Power.Reboot();
                        else if (key == ConsoleKey.S)
                            Cosmos.Kernel.System.Power.Shutdown();
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        private static long GetUptimeSeconds()
        {
            try
            {
                long frequency = Stopwatch.Frequency;
                if (frequency <= 0)
                    return 0;
                return Stopwatch.GetTimestamp() / frequency;
            }
            catch
            {
                return 0;
            }
        }

        private static string GetMemorySummary()
        {
            try
            {
                ulong total = PageAllocator.TotalPageCount;
                ulong free = PageAllocator.FreePageCount;
                ulong used = total >= free ? total - free : 0;
                return "pages used=" + used + " free=" + free + " total=" + total;
            }
            catch
            {
                return "unavailable";
            }
        }

        private static void RestoreConsole()
        {
            try
            {
                Console.BackgroundColor = ConsoleColor.Black;
                Console.ForegroundColor = ConsoleColor.White;
                Console.Clear();
            }
            catch
            {
            }
        }
    }
}
