using System;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.Memory;

namespace ZonderqOS.Hardware
{
    /// <summary>
    /// Detects total physical RAM installed on the machine and available to the firmware
    /// by querying the Limine bootloader memory map response across all memory regions.
    /// This resolves the issue where a multi-gigabyte memory map split across the 4 GB MMIO hole
    /// is only partially reported by the single-range Cosmos heap allocator.
    /// </summary>
    public static class PhysicalMemoryDetector
    {
        private static bool initialized;
        private static ulong cachedTotalPhysicalBytes;
        private static ulong cachedInstalledBytes;

        public static ulong TotalPhysicalBytes
        {
            get
            {
                EnsureDetected();
                return cachedTotalPhysicalBytes;
            }
        }

        public static ulong InstalledBytes
        {
            get
            {
                EnsureDetected();
                return cachedInstalledBytes;
            }
        }

        public static ulong HeapPoolBytes
        {
            get
            {
                try
                {
                    return PageAllocator.TotalPageCount * PageAllocator.PageSize;
                }
                catch
                {
                    return 0;
                }
            }
        }

        private static void EnsureDetected()
        {
            if (initialized)
                return;

            initialized = true;

            ulong heapBytes = 0;
            try
            {
                heapBytes = PageAllocator.TotalPageCount * PageAllocator.PageSize;
            }
            catch
            {
                heapBytes = 0;
            }

            ulong detectedUsable = 0;
            ulong highestAddr = 0;

            try
            {
                unsafe
                {
                    LimineMemmapResponse* response = Limine.MemoryMap.Response;
                    if (response != null && response->Entries != null && response->EntryCount > 0)
                    {
                        ulong count = response->EntryCount;
                        for (ulong i = 0; i < count; i++)
                        {
                            LimineMemmapEntry* entry = response->Entries[i];
                            if (entry == null)
                                continue;

                            ulong baseAddr = (ulong)entry->Base;
                            ulong length = entry->Length;
                            ulong endAddr = baseAddr + length;
                            if (endAddr > highestAddr)
                                highestAddr = endAddr;

                            LimineMemmapType type = entry->Type;
                            // Sum RAM that was available to firmware/OS (usable + bootloader + kernel modules + ACPI reclaimable)
                            if (type == LimineMemmapType.Usable ||
                                type == LimineMemmapType.BootloaderReclaimable ||
                                type == LimineMemmapType.AcpiReclaimable ||
                                type == LimineMemmapType.KernelAndModules)
                            {
                                detectedUsable += length;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback for host tests or environments without Limine memory map
                detectedUsable = 0;
                highestAddr = 0;
            }

            // If Limine gave us valid physical RAM regions, use them
            if (detectedUsable > heapBytes)
            {
                cachedTotalPhysicalBytes = detectedUsable;
            }
            else
            {
                cachedTotalPhysicalBytes = heapBytes;
            }

            // Estimate installed module capacity based strictly on detected physical RAM bytes
            cachedInstalledBytes = EstimateInstalledCapacity(cachedTotalPhysicalBytes);
        }

        private static ulong EstimateInstalledCapacity(ulong physicalBytes)
        {
            if (physicalBytes == 0)
                return 0;

            const ulong GiB = 1024UL * 1024UL * 1024UL;

            // Round detected physical usable RAM up to standard module sizes (1, 2, 4, 6, 8, 12, 16, 24, 32, 64, 128 GB)
            if (physicalBytes <= 1UL * GiB) return 1UL * GiB;
            if (physicalBytes <= 2UL * GiB) return 2UL * GiB;
            if (physicalBytes <= 4UL * GiB) return 4UL * GiB;
            if (physicalBytes <= 6UL * GiB) return 6UL * GiB;
            if (physicalBytes <= 8UL * GiB) return 8UL * GiB;
            if (physicalBytes <= 12UL * GiB) return 12UL * GiB;
            if (physicalBytes <= 16UL * GiB) return 16UL * GiB;
            if (physicalBytes <= 24UL * GiB) return 24UL * GiB;
            if (physicalBytes <= 32UL * GiB) return 32UL * GiB;
            if (physicalBytes <= 64UL * GiB) return 64UL * GiB;
            if (physicalBytes <= 128UL * GiB) return 128UL * GiB;

            return physicalBytes;
        }
    }
}
