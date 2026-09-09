using System;
using Cosmos.Kernel.Core.Memory;

namespace ZonderqOS.Commands
{
    public class CmdFree : ICommand
    {
        // Używamy właściwości (get), aby spełnić wymogi interfejsu
        public string Name { get; } = "free";
        public string Description { get; } = "Displays RAM status (ASCII only)";

        // Sygnatura idealnie zgodna z Twoim interfejsem
        public void Execute(string[] args, ref string currentPath)
        {
            try
            {
                // Używamy natywnego alokatora stron Cosmosa (nie psuje linkera)
                ulong totalPages = PageAllocator.TotalPageCount;
                ulong freePages = PageAllocator.FreePageCount;
                ulong usedPages = totalPages - freePages;
                ulong pageSize = PageAllocator.PageSize;

                ulong totalRamMiB = (totalPages * pageSize) / (1024 * 1024);
                ulong usedRamMiB = (usedPages * pageSize) / (1024 * 1024);
                ulong freeRamMiB = (freePages * pageSize) / (1024 * 1024);

                Console.WriteLine("              total        used        free");
                Console.WriteLine($"Mem:          {totalRamMiB}M         {usedRamMiB}M         {freeRamMiB}M");
                
                // Usunąłem wywołanie Heap.Collect(), aby zminimalizować ryzyko 
                // naruszenia niestabilnego API Garbage Collectora podczas kompilacji.
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Kernel exception: {ex.Message}");
            }
        }
    }
}