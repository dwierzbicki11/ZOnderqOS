using System;
using Cosmos.Kernel.Core.Memory;

namespace ZonderqOS.Commands
{
    public class CmdFree : ICommand
    {
        public string Name { get; } = "free";
        public string Description { get; } = "Displays RAM status (ASCII only)";

        public void Execute(string[] args, ref string currentPath)
        {
            try
            {
                ulong totalPages = PageAllocator.TotalPageCount;
                ulong freePages = PageAllocator.FreePageCount;
                ulong usedPages = totalPages - freePages;
                ulong pageSize = PageAllocator.PageSize;

                ulong totalRamMiB = (totalPages * pageSize) / (1024 * 1024);
                ulong usedRamMiB = (usedPages * pageSize) / (1024 * 1024);
                ulong freeRamMiB = (freePages * pageSize) / (1024 * 1024);

                CommandIO.WriteLine("              total        used        free");
                CommandIO.WriteLine($"Mem:          {totalRamMiB}M         {usedRamMiB}M         {freeRamMiB}M");
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