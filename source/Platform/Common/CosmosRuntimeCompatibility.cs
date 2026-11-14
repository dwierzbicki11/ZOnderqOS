using System;
using System.Runtime.CompilerServices;
#if ZONDERQ_COSMOS_INPUT_API
using RuntimeMemoryInfo = Cosmos.Kernel.System.Diagnostics.MemoryDiagnostics;
#else
using RuntimeMemoryInfo = Cosmos.Kernel.System.Diagnostics.MemoryInfo;
#endif

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
            get { try { return RuntimeMemoryInfo.TotalPages; } catch { return 0UL; } }
        }

        public static ulong FreePageCount
        {
            get { try { return RuntimeMemoryInfo.FreePages; } catch { return 0UL; } }
        }

        public static ulong PageSize
        {
            get { try { return RuntimeMemoryInfo.PageSizeBytes; } catch { return 4096UL; } }
        }

        public static ulong RamSize
        {
            get { try { return RuntimeMemoryInfo.RamSizeBytes; } catch { return 0UL; } }
        }

#if ZONDERQ_COSMOS_INPUT_API
        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "GetPageCountsByType")]
        private static extern void NativeGetPageCountsByType(
            [UnsafeAccessorType("Cosmos.Kernel.Core.Memory.PageAllocator, Cosmos.Kernel.Core")] object _,
            out ulong empty, out ulong gcHeap, out ulong heapSmall, out ulong heapMedium, out ulong heapLarge,
            out ulong unmanaged, out ulong pageDirectory, out ulong pageAllocator, out ulong smt, out ulong extension, out ulong unknown);
#endif

        public static void GetPageCountsByType(
            out ulong empty, out ulong gcHeap, out ulong heapSmall, out ulong heapMedium, out ulong heapLarge,
            out ulong unmanaged, out ulong pageDirectory, out ulong pageAllocator, out ulong smt, out ulong extension, out ulong unknown)
        {
            empty = gcHeap = heapSmall = heapMedium = heapLarge = 0UL;
            unmanaged = pageDirectory = pageAllocator = smt = extension = unknown = 0UL;
#if ZONDERQ_COSMOS_INPUT_API
            try
            {
                NativeGetPageCountsByType(
                    null!,
                    out empty, out gcHeap, out heapSmall, out heapMedium, out heapLarge,
                    out unmanaged, out pageDirectory, out pageAllocator, out smt, out extension, out unknown);

                // PageAllocator.AllocPages marks only page 0 of multi-page runs with the caller's
                // PageType and marks continuation pages as PageType.Extension; fold GC segment
                // continuation pages back into gcHeap so GC vs system page accounting is accurate.
                ulong pageSize = PageSize;
                ulong committedGcPages = pageSize > 0UL
                    ? global::Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector.GetTotalCommittedBytes() / pageSize
                    : 0UL;
                if (gcHeap > 0UL && extension > 0UL && committedGcPages > gcHeap)
                {
                    ulong gcExt = Math.Min(extension, committedGcPages - gcHeap);
                    gcHeap += gcExt;
                    extension -= gcExt;
                }
            }
            catch
            {
            }
#endif
        }
    }
}

namespace Cosmos.Kernel.Core.Memory.GarbageCollector
{
    public static class GarbageCollector
    {
        public static bool IsEnabled => true;

#if ZONDERQ_COSMOS_INPUT_API
        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "GetHeapSizeBytes")]
        private static extern ulong NativeGetHeapSizeBytes(
            [UnsafeAccessorType("Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector, Cosmos.Kernel.Core")] object _);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "GetTotalCommittedBytes")]
        private static extern ulong NativeGetTotalCommittedBytes(
            [UnsafeAccessorType("Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector, Cosmos.Kernel.Core")] object _);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "GetCommittedGcSegmentsBytes")]
        private static extern ulong NativeGetCommittedGcSegmentsBytes(
            [UnsafeAccessorType("Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector, Cosmos.Kernel.Core")] object _);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "GetFragmentedBytes")]
        private static extern ulong NativeGetFragmentedBytes(
            [UnsafeAccessorType("Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector, Cosmos.Kernel.Core")] object _);
#endif

        private static ulong ReadLiveHeapBytes()
        {
            try
            {
#if ZONDERQ_COSMOS_INPUT_API
                ulong liveNative = NativeGetHeapSizeBytes(null!);
                if (liveNative > 0UL)
                    return liveNative;
                long live = GC.GetGCMemoryInfo().HeapSizeBytes;
#else
                long live = GC.GetTotalMemory(forceFullCollection: false);
#endif
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
#if ZONDERQ_COSMOS_INPUT_API
            try
            {
                ulong liveCommitted = NativeGetTotalCommittedBytes(null!);
                ulong maxPoolBytes = PageAllocator.TotalPageCount * PageAllocator.PageSize;
                bool validNative = liveCommitted > 0UL &&
                    (maxPoolBytes > 0UL ? liveCommitted <= maxPoolBytes : liveCommitted < 0xFFFF000000000000UL);
                if (validNative)
                    return liveCommitted;

                // Cosmos 3.0.95 UpdateFrozenSegment stores the raw HHDM end pointer (0xFFFF8000...)
                // into FrozenSegmentInfo.CommitSize, causing NativeGetTotalCommittedBytes to overflow.
                // Fall back to committed GC segment bytes plus pinned-segment delta rounded to 64 KB.
                ulong segCommitted = NativeGetCommittedGcSegmentsBytes(null!);
                ulong liveHeap = ReadLiveHeapBytes();
                if (liveHeap > segCommitted)
                {
                    ulong diff = liveHeap - segCommitted;
                    segCommitted += (diff + 65535UL) & ~65535UL;
                }
                if (segCommitted > 0UL)
                    return segCommitted;
            }
            catch { }
#endif
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
#if ZONDERQ_COSMOS_INPUT_API
            try
            {
                ulong liveFrag = NativeGetFragmentedBytes(null!);
                if (liveFrag > 0UL)
                    return liveFrag;
            }
            catch { }
#endif
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
            try { return RuntimeMemoryInfo.TotalCollections; }
            catch { return 0; }
        }

        public static int GetTotalObjectsFreed()
        {
#if ZONDERQ_COSMOS_INPUT_API
            try { return RuntimeMemoryInfo.TotalObjectsFreed; }
            catch { return 0; }
#else
            return 0;
#endif
        }

        public static int GetLastGCPercentTimeInGC()
        {
#if ZONDERQ_COSMOS_INPUT_API
            try { return RuntimeMemoryInfo.GcTimePercent; }
            catch { return 0; }
#else
            return 0;
#endif
        }
    }
}

#pragma warning restore COSMOS0001
