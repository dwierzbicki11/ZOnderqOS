using System;
using Cosmos.Kernel.Core.Memory;

namespace ZonderqOS.Commands
{
    public class CmdFree : ICommand
    {
        public string Name { get; } = "free";
        public string Description { get; } = "Displays RAM status (ASCII only)";

        private static string FormatMiB(ulong bytes)
        {
            const ulong MiB = 1024UL * 1024UL;
            ulong whole = bytes / MiB;
            ulong hundredths = (bytes % MiB) * 100UL / MiB;
            return whole + "." + (hundredths < 10 ? "0" : "") + hundredths + "M";
        }

        public void Execute(string[] args, ref string currentPath)
        {
            try
            {
                ulong totalPages = PageAllocator.TotalPageCount;
                ulong freePages = Math.Min(PageAllocator.FreePageCount, totalPages);
                ulong usedPages = totalPages - freePages;
                ulong pageSize = PageAllocator.PageSize;

                ulong totalBytes = totalPages * pageSize;
                ulong usedBytes = usedPages * pageSize;
                ulong freeBytes = freePages * pageSize;

                CommandIO.WriteLine("              total        used        free");
                CommandIO.WriteLine($"Mem:          {FormatMiB(totalBytes)}     {FormatMiB(usedBytes)}     {FormatMiB(freeBytes)}");
                CommandIO.LastCommandSuccess = true;
            }
            catch (Exception ex)
            {
                CommandIO.WriteLine($"[ERROR] Kernel exception: {ex.Message}");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}