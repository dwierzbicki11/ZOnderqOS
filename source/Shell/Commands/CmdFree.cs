using System;
using Cosmos.Kernel.System.Diagnostics;

namespace ZonderqOS.Commands
{
    public sealed class CmdFree : ICommand
    {
        public string Name => "free";
        public string Description => "Show physical page allocator memory usage";

        public void Execute(string[] args, ref string currentPath)
        {
            bool human = false;
            bool mebibytes = false;

            if (args.Length == 2)
            {
                human = args[1] == "-h" || args[1] == "--human";
                mebibytes = args[1] == "-m";
                if (!human && !mebibytes)
                {
                    Usage();
                    CommandIO.LastCommandSuccess = false;
                    return;
                }
            }
            else if (args.Length > 2)
            {
                Usage();
                CommandIO.LastCommandSuccess = false;
                return;
            }

            try
            {
                ulong totalPages = MemoryInfo.TotalPages;
                ulong freePages = Math.Min(totalPages, MemoryInfo.FreePages);
                ulong pageSize = MemoryInfo.PageSizeBytes;
                ulong total = MultiplySaturated(totalPages, pageSize);
                ulong freeBytes = MultiplySaturated(freePages, pageSize);
                ulong used = total >= freeBytes ? total - freeBytes : 0;

                CommandIO.WriteLine("              total        used        free");
                CommandIO.WriteLine(
                    "Mem:    " +
                    Format(total, human, mebibytes).PadLeft(12) +
                    Format(used, human, mebibytes).PadLeft(12) +
                    Format(freeBytes, human, mebibytes).PadLeft(12));
                CommandIO.WriteLine("Page size: " + pageSize + " B");
                CommandIO.LastCommandSuccess = true;
            }
            catch (Exception ex)
            {
                CommandIO.WriteLine("[ERROR] Memory telemetry failed: " + ex.Message);
                CommandIO.LastCommandSuccess = false;
            }
        }

        private static void Usage() => CommandIO.WriteLine("Usage: free [-h|-m]");

        private static ulong MultiplySaturated(ulong a, ulong b)
        {
            if (a == 0 || b == 0) return 0;
            if (a > ulong.MaxValue / b) return ulong.MaxValue;
            return a * b;
        }

        private static string Format(ulong bytes, bool human, bool mebibytes)
        {
            if (mebibytes)
                return (bytes / 1048576UL).ToString();

            if (!human)
                return (bytes / 1024UL).ToString();

            if (bytes >= 1073741824UL)
                return ((double)bytes / 1073741824d).ToString("0.0") + " GiB";
            if (bytes >= 1048576UL)
                return ((double)bytes / 1048576d).ToString("0.0") + " MiB";
            if (bytes >= 1024UL)
                return ((double)bytes / 1024d).ToString("0.0") + " KiB";
            return bytes + " B";
        }
    }
}
