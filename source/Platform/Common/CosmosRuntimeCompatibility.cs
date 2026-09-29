using System;

#pragma warning disable COSMOS0001

// Cosmos.Kernel.System.Diagnostics comes directly from the Cosmos 3.0.85 package.
// Do not shadow SchedulerInfo/MemoryInfo here: monitors must observe the real
// scheduler registry, CPU counters and physical page allocator. Only narrow
// accessibility wrappers for internal Cosmos.Core memory types remain below.

namespace Cosmos.Kernel.Core.Memory
{
    public static class PageAllocator
    {
        public static ulong TotalPageCount
        {
            get { try { return global::Cosmos.Kernel.System.Diagnostics.MemoryInfo.TotalPages; } catch { return 0UL; } }
        }

        public static ulong FreePageCount
        {
            get { try { return global::Cosmos.Kernel.System.Diagnostics.MemoryInfo.FreePages; } catch { return 0UL; } }
        }

        public static ulong PageSize
        {
            get { try { return global::Cosmos.Kernel.System.Diagnostics.MemoryInfo.PageSizeBytes; } catch { return 4096UL; } }
        }

        public static ulong RamSize
        {
            get { try { return global::Cosmos.Kernel.System.Diagnostics.MemoryInfo.RamSizeBytes; } catch { return 0UL; } }
        }
    }
}

namespace Cosmos.Kernel.Core.Memory.GarbageCollector
{
    public static class GarbageCollector
    {
        public static bool IsEnabled => true;

        private static ulong ReadLiveHeapBytes()
        {
            try
            {
                long live = GC.GetTotalMemory(forceFullCollection: false);
                return live > 0 ? (ulong)live : 0UL;
            }
            catch
            {
                return 0UL;
            }
        }

        public static ulong GetHeapSizeBytes()
        {
            ulong live = ReadLiveHeapBytes();
            if (live > 0)
                return live;

            try
            {
                long snapshot = GC.GetGCMemoryInfo().HeapSizeBytes;
                return snapshot > 0 ? (ulong)snapshot : 0UL;
            }
            catch { return 0UL; }
        }

        public static ulong GetTotalCommittedBytes()
        {
            try
            {
                GCMemoryInfo info = GC.GetGCMemoryInfo();
                long value = Math.Max(info.TotalCommittedBytes, info.HeapSizeBytes);
                return value > 0 ? (ulong)value : ReadLiveHeapBytes();
            }
            catch { return ReadLiveHeapBytes(); }
        }

        public static ulong GetFragmentedBytes()
        {
            try
            {
                long value = GC.GetGCMemoryInfo().FragmentedBytes;
                return value > 0 ? (ulong)value : 0UL;
            }
            catch { return 0UL; }
        }

        public static ulong GetPinnedObjectsCount()
        {
            try
            {
                long value = GC.GetGCMemoryInfo().PinnedObjectsCount;
                return value > 0 ? (ulong)value : 0UL;
            }
            catch { return 0UL; }
        }

        public static int GetCollectionIndex()
        {
            try { return global::Cosmos.Kernel.System.Diagnostics.MemoryInfo.TotalCollections; }
            catch { return 0; }
        }
    }
}

#pragma warning restore COSMOS0001
