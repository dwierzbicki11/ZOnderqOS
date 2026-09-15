using System;
using Cosmos.Kernel.System.Diagnostics;

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
