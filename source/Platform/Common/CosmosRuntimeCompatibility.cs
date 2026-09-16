using System;
using ZonderqOS.SystemCore;

#pragma warning disable COSMOS0001

// Cross-architecture Cosmos compatibility belongs in Platform/Common. The
// application/GUI layer must not care which packaged architecture exposes a
// particular diagnostics type. Both x64 and ARM64 consume the same facade and
// the architecture-specific code is selected separately by the project file.

namespace Cosmos.Kernel.System.Diagnostics
{
    public enum KernelThreadState : byte
    {
        Created,
        Ready,
        Running,
        Blocked,
        Sleeping,
        Dead
    }

    public readonly struct KernelThreadInfo
    {
        public uint Id { get; }
        public uint CpuId { get; }
        public KernelThreadState State { get; }
        public bool IsIdle { get; }
        public bool IsManaged { get; }
        public ulong TotalRuntimeNs { get; }
        public nuint StackSize { get; }
        public long Priority { get; }
        public bool HasPriority { get; }

        public KernelThreadInfo(
            uint id,
            uint cpuId,
            KernelThreadState state,
            bool isIdle,
            bool isManaged,
            ulong totalRuntimeNs,
            nuint stackSize,
            long priority,
            bool hasPriority)
        {
            Id = id;
            CpuId = cpuId;
            State = state;
            IsIdle = isIdle;
            IsManaged = isManaged;
            TotalRuntimeNs = totalRuntimeNs;
            StackSize = stackSize;
            Priority = priority;
            HasPriority = hasPriority;
        }
    }

    public static class MemoryInfo
    {
        public const ulong PageSizeBytes = 4096UL;

        // GC.GetGCMemoryInfo() is a collection snapshot. On OrionGC its heap and
        // committed fields can still be zero before the first collection even
        // though the runtime has already allocated objects. Use the cumulative
        // allocation counter only as an early-boot fallback; as soon as a live
        // snapshot exists, the current heap/committed values win.
        private static ulong ReadLiveManagedBytes()
        {
            try
            {
                GCMemoryInfo info = GC.GetGCMemoryInfo();
                long live = Math.Max(info.TotalCommittedBytes, info.HeapSizeBytes);
                if (live > 0)
                    return (ulong)live;

                long allocated = GC.GetTotalAllocatedBytes(precise: false);
                return allocated > 0 ? (ulong)allocated : 0UL;
            }
            catch
            {
                try
                {
                    long allocated = GC.GetTotalAllocatedBytes(precise: false);
                    return allocated > 0 ? (ulong)allocated : 0UL;
                }
                catch
                {
                    return 0UL;
                }
            }
        }

        public static ulong RamSizeBytes
        {
            get
            {
                try
                {
                    GCMemoryInfo info = GC.GetGCMemoryInfo();
                    ulong live = ReadLiveManagedBytes();
                    long availableValue = info.TotalAvailableMemoryBytes;
                    ulong available = availableValue > 0 ? (ulong)availableValue : 0UL;
                    return Math.Max(available, live);
                }
                catch
                {
                    return ReadLiveManagedBytes();
                }
            }
        }

        public static ulong TotalPages => RamSizeBytes / PageSizeBytes;

        public static ulong FreePages
        {
            get
            {
                try
                {
                    ulong total = RamSizeBytes;
                    ulong used = Math.Min(ReadLiveManagedBytes(), total);
                    return total > used ? (total - used) / PageSizeBytes : 0UL;
                }
                catch
                {
                    return 0UL;
                }
            }
        }

        public static int TotalCollections
        {
            get
            {
                try
                {
                    return GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
                }
                catch
                {
                    return 0;
                }
            }
        }
    }

    public static class SchedulerInfo
    {
        private const int MaxTrackedThreads = 256;
        private const int MaxTrackedCpus = 256;

        private static readonly object SnapshotLock = new object();
        private static readonly global::Cosmos.Kernel.Core.Scheduler.SchedulerThread[] KnownThreads =
            new global::Cosmos.Kernel.Core.Scheduler.SchedulerThread[MaxTrackedThreads];

        private static int knownHighWater;
        private static PortableRuntimeTelemetry.CpuUtilizationState cpuState;

        public static bool IsSupported => true;

        public static bool IsInitialized
        {
            get
            {
                try { return global::Cosmos.Kernel.Core.Scheduler.SchedulerManager.IsReady; }
                catch { return false; }
            }
        }

        public static bool IsRunning => IsInitialized && SchedulerName != null;

        public static string SchedulerName
        {
            get
            {
                try
                {
                    return global::Cosmos.Kernel.Core.Scheduler.SchedulerManager.Current?.Name;
                }
                catch
                {
                    return null;
                }
            }
        }

        public static uint CpuCount
        {
            get
            {
                lock (SnapshotLock)
                    return DetectCpuCount();
            }
        }

        public static ulong TickPeriodNs =>
            global::Cosmos.Kernel.Core.Scheduler.SchedulerManager.DefaultQuantumNs;

        public static ulong BusyCpuTimeNs
        {
            get
            {
                lock (SnapshotLock)
                    return PortableRuntimeTelemetry.SampleBusyCpuTimeNs(ref cpuState);
            }
        }

        public static int ThreadCount
        {
            get
            {
                lock (SnapshotLock)
                {
                    RefreshKnownThreads();
                    int count = 0;
                    for (int i = 0; i < knownHighWater; i++)
                    {
                        var thread = KnownThreads[i];
                        if (thread != null && thread.State != global::Cosmos.Kernel.Core.Scheduler.SchedulerThreadState.Dead)
                            count++;
                    }
                    return count;
                }
            }
        }

        public static int ThreadSlotCount
        {
            get
            {
                lock (SnapshotLock)
                {
                    RefreshKnownThreads();
                    return knownHighWater;
                }
            }
        }

        public static bool TryGetThreadInSlot(int slot, out KernelThreadInfo info)
        {
            lock (SnapshotLock)
            {
                if (slot < 0 || slot >= knownHighWater)
                {
                    info = default;
                    return false;
                }

                var thread = KnownThreads[slot];
                if (thread == null || thread.State == global::Cosmos.Kernel.Core.Scheduler.SchedulerThreadState.Dead)
                {
                    info = default;
                    return false;
                }

                bool isIdle = (thread.Flags & global::Cosmos.Kernel.Core.Scheduler.SchedulerThreadFlags.IdleThread) != 0;
                bool isManaged = (thread.Flags & global::Cosmos.Kernel.Core.Scheduler.SchedulerThreadFlags.Managed) != 0;
                long priority = 0;
                bool hasPriority = false;

                try
                {
                    var scheduler = global::Cosmos.Kernel.Core.Scheduler.SchedulerManager.Current;
                    if (scheduler != null)
                    {
                        priority = scheduler.GetPriority(thread);
                        hasPriority = true;
                    }
                }
                catch
                {
                }

                info = new KernelThreadInfo(
                    thread.Id,
                    thread.CpuId,
                    MapState(thread.State),
                    isIdle,
                    isManaged,
                    thread.TotalRuntime,
                    thread.StackSize,
                    priority,
                    hasPriority);
                return true;
            }
        }

        private static uint DetectCpuCount()
        {
            if (!IsInitialized)
                return 0U;

            uint count = 0;
            for (uint cpu = 0; cpu < MaxTrackedCpus; cpu++)
            {
                try
                {
                    var state = global::Cosmos.Kernel.Core.Scheduler.SchedulerManager.GetCpuState(cpu);
                    if (state == null)
                        break;
                    count++;
                }
                catch (IndexOutOfRangeException)
                {
                    break;
                }
                catch
                {
                    break;
                }
            }

            return count;
        }

        private static void RefreshKnownThreads()
        {
            if (!IsInitialized)
                return;

            uint cpuCount = DetectCpuCount();
            var scheduler = global::Cosmos.Kernel.Core.Scheduler.SchedulerManager.Current;

            for (uint cpu = 0; cpu < cpuCount; cpu++)
            {
                global::Cosmos.Kernel.Core.Scheduler.PerCpuState state;
                try
                {
                    state = global::Cosmos.Kernel.Core.Scheduler.SchedulerManager.GetCpuState(cpu);
                }
                catch
                {
                    continue;
                }

                if (state == null)
                    continue;

                Remember(state.IdleThread);
                Remember(state.CurrentThread);

                if (scheduler == null)
                    continue;

                int queued = 0;
                try { queued = scheduler.GetRunQueueCount(state); }
                catch { queued = 0; }

                if (queued < 0) queued = 0;
                if (queued > MaxTrackedThreads) queued = MaxTrackedThreads;

                for (int i = 0; i < queued; i++)
                {
                    try { Remember(scheduler.GetRunQueueThread(state, i)); }
                    catch { }
                }
            }

            for (int i = 0; i < knownHighWater; i++)
            {
                var thread = KnownThreads[i];
                if (thread != null && thread.State == global::Cosmos.Kernel.Core.Scheduler.SchedulerThreadState.Dead)
                    KnownThreads[i] = null;
            }

            while (knownHighWater > 0 && KnownThreads[knownHighWater - 1] == null)
                knownHighWater--;
        }

        private static void Remember(global::Cosmos.Kernel.Core.Scheduler.SchedulerThread thread)
        {
            if (thread == null)
                return;

            int freeSlot = -1;
            for (int i = 0; i < knownHighWater; i++)
            {
                if (ReferenceEquals(KnownThreads[i], thread))
                    return;
                if (freeSlot < 0 && KnownThreads[i] == null)
                    freeSlot = i;
            }

            if (freeSlot >= 0)
            {
                KnownThreads[freeSlot] = thread;
                return;
            }

            if (knownHighWater < KnownThreads.Length)
                KnownThreads[knownHighWater++] = thread;
        }

        private static KernelThreadState MapState(global::Cosmos.Kernel.Core.Scheduler.SchedulerThreadState state)
        {
            switch (state)
            {
                case global::Cosmos.Kernel.Core.Scheduler.SchedulerThreadState.Ready: return KernelThreadState.Ready;
                case global::Cosmos.Kernel.Core.Scheduler.SchedulerThreadState.Running: return KernelThreadState.Running;
                case global::Cosmos.Kernel.Core.Scheduler.SchedulerThreadState.Blocked: return KernelThreadState.Blocked;
                case global::Cosmos.Kernel.Core.Scheduler.SchedulerThreadState.Sleeping: return KernelThreadState.Sleeping;
                case global::Cosmos.Kernel.Core.Scheduler.SchedulerThreadState.Dead: return KernelThreadState.Dead;
                default: return KernelThreadState.Created;
            }
        }
    }
}

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

        private static ulong ReadEarlyAllocationFallback()
        {
            try
            {
                long allocated = GC.GetTotalAllocatedBytes(precise: false);
                return allocated > 0 ? (ulong)allocated : 0UL;
            }
            catch
            {
                return 0UL;
            }
        }

        public static ulong GetHeapSizeBytes()
        {
            try
            {
                long value = GC.GetGCMemoryInfo().HeapSizeBytes;
                return value > 0 ? (ulong)value : ReadEarlyAllocationFallback();
            }
            catch { return ReadEarlyAllocationFallback(); }
        }

        public static ulong GetTotalCommittedBytes()
        {
            try
            {
                GCMemoryInfo info = GC.GetGCMemoryInfo();
                long value = Math.Max(info.TotalCommittedBytes, info.HeapSizeBytes);
                return value > 0 ? (ulong)value : ReadEarlyAllocationFallback();
            }
            catch { return ReadEarlyAllocationFallback(); }
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
