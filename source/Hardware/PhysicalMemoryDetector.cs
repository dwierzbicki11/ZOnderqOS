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

            // Estimate installed module capacity (standard DIMM / SO-DIMM powers of 2 or typical combinations)
            cachedInstalledBytes = EstimateInstalledCapacity(cachedTotalPhysicalBytes, highestAddr);
        }

        private static ulong EstimateInstalledCapacity(ulong physicalBytes, ulong highestAddr)
        {
            ulong reference = highestAddr > physicalBytes ? highestAddr : physicalBytes;
            if (reference == 0)
                return 0;

            const ulong GiB = 1024UL * 1024UL * 1024UL;

            // Round up to standard installed sizes: 1, 2, 4, 6, 8, 12, 16, 24, 32, 64, 128 GB
            if (reference <= 1UL * GiB) return 1UL * GiB;
            if (reference <= 2UL * GiB) return 2UL * GiB;
            if (reference <= 4UL * GiB) return 4UL * GiB;
            if (reference <= 6UL * GiB + (512UL * 1024UL * 1024UL)) return 6UL * GiB;
            if (reference <= 8UL * GiB + (1024UL * 1024UL * 1024UL)) return 8UL * GiB;
            if (reference <= 12UL * GiB + (1024UL * 1024UL * 1024UL)) return 12UL * GiB;
            if (reference <= 16UL * GiB + (1024UL * 1024UL * 1024UL)) return 16UL * GiB;
            if (reference <= 32UL * GiB + (2048UL * 1024UL * 1024UL)) return 32UL * GiB;
            if (reference <= 64UL * GiB + (4096UL * 1024UL * 1024UL)) return 64UL * GiB;

            return physicalBytes;
        }
    }
}
