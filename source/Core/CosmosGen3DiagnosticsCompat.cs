using System;
using System.Runtime.InteropServices;
using Cosmos.Kernel.System.Diagnostics;

#if ARCH_ARM64
// The ARM64 NuGet surface used by the current Cosmos 3.0.84 toolchain does not
// expose Cosmos.Kernel.System.Diagnostics, even though the corresponding source
// tree contains it. Keep ZonderqOS on one desktop/task-manager code path by
// providing the small diagnostic surface we consume here.
//
// CPU utilization is not fabricated: SystemNative_GetCpuUtilization is exported
// by Cosmos.Kernel.Core and internally samples SchedulerManager.GetBusyCpuTimeNs().
// Cosmos 3.0.84 currently brings up one managed ARM64 CPU, so the thread snapshot
// below is an aggregate scheduler sample for CPU0 until upstream exposes the full
// per-thread diagnostics ring to the ARM64 package.
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
        public ulong TotalRuntimeNs { get; }
        public bool IsIdle { get; }

        public KernelThreadInfo(uint id, uint cpuId, KernelThreadState state, ulong totalRuntimeNs, bool isIdle)
        {
            Id = id;
            CpuId = cpuId;
            State = state;
            TotalRuntimeNs = totalRuntimeNs;
            IsIdle = isIdle;
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
                    long total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                    return total > 0 ? (ulong)total : 0UL;
                }
                catch
                {
                    return 0UL;
                }
            }
        }

        public static ulong TotalPages
        {
            get
            {
                ulong ram = RamSizeBytes;
                return ram == 0 ? 0UL : ram / PageSizeBytes;
            }
        }

        public static ulong FreePages
        {
            get
            {
                try
                {
                    var info = GC.GetGCMemoryInfo();
                    long total = info.TotalAvailableMemoryBytes;
                    if (total <= 0)
                        return 0UL;

                    long used = info.MemoryLoadBytes;
                    if (used <= 0)
                        used = GC.GetTotalMemory(false);
                    if (used < 0)
                        used = 0;
                    if (used > total)
                        used = total;

                    return (ulong)(total - used) / PageSizeBytes;
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
                    int total = 0;
                    for (int generation = 0; generation <= GC.MaxGeneration; generation++)
                        total += GC.CollectionCount(generation);
                    return total;
                }
                catch
                {
                    return 0;
                }
            }
        }
    }

    public static partial class SchedulerInfo
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessCpuInformation
        {
            public ulong LastRecordedCurrentTime;
            public ulong LastRecordedKernelTime;
            public ulong LastRecordedUserTime;
        }

        private static ProcessCpuInformation s_cpuInformation;

        [LibraryImport("*", EntryPoint = "SystemNative_GetCpuUtilization")]
        private static partial double GetCpuUtilization(ref ProcessCpuInformation previousCpuInfo);

        public static bool IsSupported => true;
        public static bool IsInitialized => true;
        public static bool IsRunning => true;
        public static string SchedulerName => "Stride";
        public static uint CpuCount => 1;
        public static ulong TickPeriodNs => 10_000_000UL;
        public static int ThreadCount => 1;
        public static int ThreadSlotCount => 1;

        public static ulong BusyCpuTimeNs
        {
            get
            {
                try
                {
                    _ = GetCpuUtilization(ref s_cpuInformation);
                    return s_cpuInformation.LastRecordedUserTime;
                }
                catch
                {
                    return 0UL;
                }
            }
        }

        public static bool TryGetThreadInSlot(int slot, out KernelThreadInfo info)
        {
            if (slot != 0)
            {
                info = default;
                return false;
            }

            info = new KernelThreadInfo(
                0,
                0,
                KernelThreadState.Running,
                BusyCpuTimeNs,
                false);
            return true;
        }
    }
}
#endif

// Compatibility facade for GUI/system code that was written against pre-ring
// memory, scheduler and GC diagnostic surfaces. Cosmos Gen3 intentionally hides
// raw kernel control structures from applications; stable snapshots are exposed
// through public diagnostic APIs instead.
namespace Cosmos.Kernel.Core.Memory
{
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
            catch { return 0UL; }
        }

        public static ulong GetTotalCommittedBytes()
        {
            try
            {
                long value = GC.GetGCMemoryInfo().TotalCommittedBytes;
                return value > 0 ? (ulong)value : 0UL;
            }
            catch { return 0UL; }
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
        public ThreadState State { get; private set; }

        internal Thread(ThreadState state)
        {
            State = state;
        }
    }

    public sealed class SchedulerDescriptor
    {
        public string Name { get; private set; }

        internal SchedulerDescriptor(string name)
        {
            Name = name ?? string.Empty;
        }
    }

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
            get { try { return SchedulerInfo.CpuCount; } catch { return 0; } }
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
                catch { return null; }
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
                        return new Thread[0];

                    Thread[] result = new Thread[slots];
                    for (int i = 0; i < slots; i++)
                    {
                        KernelThreadInfo info;
                        if (SchedulerInfo.TryGetThreadInSlot(i, out info))
                            result[i] = new Thread(MapState(info.State));
                    }
                    return result;
                }
                catch { return new Thread[0]; }
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
