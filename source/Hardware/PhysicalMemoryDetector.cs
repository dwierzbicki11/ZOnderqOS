using System;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.Memory;

namespace ZonderqOS.Hardware
{
    /// <summary>
    /// Separates the four memory quantities that used to be conflated:
    ///  * Installed physical RAM  - only from SMBIOS (Type 17/19). Cosmos 3.0.95's Limine
    ///    binding exposes no SMBIOS request and raw physical scans of 0xF0000 are not
    ///    guaranteed to be HHDM-mapped under Limine base revision 3, so this value is
    ///    reported as UNKNOWN (0, InstalledBytesConfirmed == false). We never round the
    ///    memory map up to a "standard module size" and present it as installed RAM.
    ///  * Usable RAM              - Limine memory map entries of type Usable.
    ///  * Firmware-visible RAM    - Usable + BootloaderReclaimable + AcpiReclaimable +
    ///    KernelAndModules (lower bound of installed RAM; excludes firmware-reserved RAM).
    ///  * Kernel heap pool        - PageAllocator.TotalPageCount * PageSize (live values
    ///    for used/free must be read from PageAllocator, not from this class).
    /// </summary>
    public static class PhysicalMemoryDetector
    {
        private static bool initialized;
        private static ulong cachedUsableBytes;
        private static ulong cachedFirmwareVisibleBytes;
        private static bool memoryMapAvailable;

        /// <summary>Firmware-visible RAM (lower bound of installed). Kept for API compatibility.</summary>
        public static ulong TotalPhysicalBytes
        {
            get { EnsureDetected(); return cachedFirmwareVisibleBytes; }
        }

        /// <summary>Limine "Usable" regions only.</summary>
        public static ulong UsableBytes
        {
            get { EnsureDetected(); return cachedUsableBytes; }
        }

        /// <summary>Installed module capacity confirmed by SMBIOS; 0 when unknown.</summary>
        public static ulong InstalledBytes
        {
            get { EnsureDetected(); return 0; }
        }

        public static bool InstalledBytesConfirmed => false;

        public static bool MemoryMapAvailable
        {
            get { EnsureDetected(); return memoryMapAvailable; }
        }

        public static string InstalledSourceText => "nieznany (brak SMBIOS w Cosmos 3.0.95)";

        public static ulong HeapPoolBytes
        {
            get
            {
                try { return PageAllocator.TotalPageCount * PageAllocator.PageSize; }
                catch { return 0; }
            }
        }

        private static void EnsureDetected()
        {
            if (initialized)
                return;
            initialized = true;

            ulong usable = 0;
            ulong visible = 0;
            try
            {
                unsafe
                {
                    LimineMemmapResponse* response = Limine.MemoryMap.Response;
                    if (response != null && response->Entries != null && response->EntryCount > 0)
                    {
                        memoryMapAvailable = true;
                        ulong count = response->EntryCount;
                        for (ulong i = 0; i < count; i++)
                        {
                            LimineMemmapEntry* entry = response->Entries[i];
                            if (entry == null)
                                continue;
                            ulong length = entry->Length;
                            LimineMemmapType type = entry->Type;
                            if (type == LimineMemmapType.Usable)
                                usable = SaturatingAdd(usable, length);
                            if (type == LimineMemmapType.Usable ||
                                type == LimineMemmapType.BootloaderReclaimable ||
                                type == LimineMemmapType.AcpiReclaimable ||
                                type == LimineMemmapType.KernelAndModules)
                                visible = SaturatingAdd(visible, length);
                        }
                    }
                }
            }
            catch
            {
                memoryMapAvailable = false;
                usable = 0;
                visible = 0;
            }

            cachedUsableBytes = usable;
            cachedFirmwareVisibleBytes = visible;
        }

        private static ulong SaturatingAdd(ulong a, ulong b)
        {
            ulong r = a + b;
            return r < a ? ulong.MaxValue : r;
        }
    }
}
