using System;
using System.Collections.Generic;
using Cosmos.Kernel.System.Diagnostics;
using ZonderqOS.SystemCore;

// Keep every Cosmos ABI workaround in this file. Application/UI code may keep
// consuming the small legacy surface below while package-specific differences
// are isolated here. When Cosmos exposes the same diagnostics ABI on every
// architecture, this file is the only place that should need to change.

#if ARCH_ARM64
namespace Cosmos.Kernel.System.Diagnostics
{
    /// <summary>
    /// ARM64 compatibility projection for the diagnostics API currently absent
    /// from the packaged Cosmos.Kernel.System compile asset. Values are sourced
    /// from managed runtime/process data instead of being invented by the UI.
    /// </summary>
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

        public static ulong RamSizeBytes
        {
            get
            {
                try
                {
                    GCMemoryInfo info = GC.GetGCMemoryInfo();
                    long value = Math.Max(
                        info.TotalAvailableMemoryBytes,
                        Math.Max(info.TotalCommittedBytes, info.HeapSizeBytes));
                    return value > 0 ? (ulong)value : 0UL;
                }
                catch
                {
                    return 0UL;
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
                    long committedValue = GC.GetGCMemoryInfo().TotalCommittedBytes;
                    ulong committed = committedValue > 0 ? (ulong)committedValue : 0UL;
                    return total > committed ? (total - committed) / PageSizeBytes : 0UL;
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
        private static readonly object SnapshotLock = new object();
        private static readonly List<KernelProcess> ProcessSnapshot = new List<KernelProcess>(16);
        private static PortableRuntimeTelemetry.CpuUtilizationState cpuState;

        // Current Cosmos ARM64/QEMU bring-up exposes one managed CPU even when
        // QEMU presents more vCPUs. Report what the kernel can actually schedule.
        public static bool IsSupported => true;
        public static bool IsInitialized => true;
        public static bool IsRunning => true;
        public static string SchedulerName => "Stride";
        public static uint CpuCount => 1U;
        public static ulong TickPeriodNs => 10_000_000UL;

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
                    RefreshProcesses();
                    return ProcessSnapshot.Count + 1;
                }
            }
        }

        public static int ThreadSlotCount => ThreadCount;

        public static bool TryGetThreadInSlot(int slot, out KernelThreadInfo info)
        {
            lock (SnapshotLock)
            {
                RefreshProcesses();

                if (slot == 0)
                {
                    info = new KernelThreadInfo(
                        uint.MaxValue,
                        0U,
                        KernelThreadState.Running,
                        false,
                        true,
                        PortableRuntimeTelemetry.SampleBusyCpuTimeNs(ref cpuState),
                        0,
                        0,
                        false);
                    return true;
                }

                int index = slot - 1;
                if (index < 0 || index >= ProcessSnapshot.Count)
                {
                    info = default;
                    return false;
                }

                KernelProcess process = ProcessSnapshot[index];
                uint id = process.PID > 0 ? (uint)process.PID : (uint)(index + 1);
                info = new KernelThreadInfo(
                    id,
                    0U,
                    process.IsRunning ? KernelThreadState.Running : KernelThreadState.Dead,
                    false,
                    true,
                    0UL,
                    0,
                    0,
                    false);
                return true;
            }
        }

        private static void RefreshProcesses()
        {
            try
            {
                ProcessManager.FillActiveProcesses(ProcessSnapshot);
            }
            catch
            {
                ProcessSnapshot.Clear();
            }
        }
    }
}
#endif

namespace Cosmos.Kernel.Core.Memory
{
    /// <summary>
    /// Legacy ZonderqOS memory facade mapped onto the public Gen3 diagnostics API.
    /// </summary>
    public static class PageAllocator
    {
        public static ulong TotalPageCount
        {
            get { try { return MemoryInfo.TotalPages; } catch { return 0UL; } }
        }

        public static ulong FreePageCount
        {
            get { try { return MemoryInfo.FreePages; } catch { return 0UL; } }
        }

        public static ulong PageSize
        {
            get { try { return MemoryInfo.PageSizeBytes; } catch { return 4096UL; } }
        }

        public static ulong RamSize
        {
            get { try { return MemoryInfo.RamSizeBytes; } catch { return 0UL; } }
        }
    }
}

namespace Cosmos.Kernel.Core.Memory.GarbageCollector
{
    /// <summary>
    /// Legacy GC facade backed by System.GC rather than Cosmos private internals.
    /// </summary>
    public static class GarbageCollector
    {
        public static bool IsEnabled => true;

        public static ulong GetHeapSizeBytes()
        {
            try
            {
                long value = GC.GetGCMemoryInfo().HeapSizeBytes;
                return value > 0 ? (ulong)value : 0UL;
            }
            catch
            {
                return 0UL;
            }
        }

        public static ulong GetTotalCommittedBytes()
        {
            try
            {
                long value = GC.GetGCMemoryInfo().TotalCommittedBytes;
                return value > 0 ? (ulong)value : 0UL;
            }
            catch
            {
                return 0UL;
            }
        }

        public static ulong GetFragmentedBytes()
        {
            try
            {
                long value = GC.GetGCMemoryInfo().FragmentedBytes;
                return value > 0 ? (ulong)value : 0UL;
            }
            catch
            {
                return 0UL;
            }
        }

        public static ulong GetPinnedObjectsCount()
        {
            try
            {
                long value = GC.GetGCMemoryInfo().PinnedObjectsCount;
                return value > 0 ? (ulong)value : 0UL;
            }
            catch
            {
                return 0UL;
            }
        }

        public static int GetCollectionIndex()
        {
            try { return MemoryInfo.TotalCollections; }
            catch { return 0; }
        }
    }
}

namespace Cosmos.Kernel.Core.Scheduler
{
    public enum ThreadState
    {
        Created = 0,
        Ready = 1,
        Running = 2,
        Blocked = 3,
        Sleeping = 4,
        Dead = 5
    }

    public sealed class Thread
    {
        public ThreadState State { get; }

        internal Thread(ThreadState state)
        {
            State = state;
        }
    }

    public sealed class SchedulerDescriptor
    {
        public string Name { get; }

        internal SchedulerDescriptor(string name)
        {
            Name = name ?? string.Empty;
        }
    }

    /// <summary>
    /// Legacy scheduler facade. No application code should reach into Cosmos
    /// scheduler internals directly; all version-sensitive mapping stays here.
    /// </summary>
    public static class SchedulerManager
    {
        public static bool IsReady
        {
            get { try { return SchedulerInfo.IsInitialized; } catch { return false; } }
        }

        public static int ThreadCount
        {
            get { try { return SchedulerInfo.ThreadCount; } catch { return 0; } }
        }

        public static uint CpuCount
        {
            get { try { return SchedulerInfo.CpuCount; } catch { return 0U; } }
        }

        public static SchedulerDescriptor Current
        {
            get
            {
                try
                {
                    string name = SchedulerInfo.SchedulerName;
                    return string.IsNullOrEmpty(name) ? null : new SchedulerDescriptor(name);
                }
                catch
                {
                    return null;
                }
            }
        }

        public static ulong GetBusyCpuTimeNs()
        {
            try { return SchedulerInfo.BusyCpuTimeNs; }
            catch { return 0UL; }
        }

        public static Thread[] Threads
        {
            get
            {
                try
                {
                    int slots = SchedulerInfo.ThreadSlotCount;
                    if (slots <= 0)
                        return Array.Empty<Thread>();

                    Thread[] result = new Thread[slots];
                    for (int i = 0; i < slots; i++)
                    {
                        if (SchedulerInfo.TryGetThreadInSlot(i, out KernelThreadInfo info))
                            result[i] = new Thread(MapState(info.State));
                    }

                    return result;
                }
                catch
                {
                    return Array.Empty<Thread>();
                }
            }
        }

        private static ThreadState MapState(KernelThreadState state)
        {
            switch (state)
            {
                case KernelThreadState.Ready: return ThreadState.Ready;
                case KernelThreadState.Running: return ThreadState.Running;
                case KernelThreadState.Blocked: return ThreadState.Blocked;
                case KernelThreadState.Sleeping: return ThreadState.Sleeping;
                case KernelThreadState.Dead: return ThreadState.Dead;
                default: return ThreadState.Created;
            }
        }
    }
}
