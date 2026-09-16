#if ARCH_ARM64
using System;
using System.Collections.Generic;
using ZonderqOS.SystemCore;

// Cosmos 3.0.85 source contains Cosmos.Kernel.System.Diagnostics, but the
// ARM64 compile asset distributed to the user kit currently does not expose
// that namespace. Keep the ZonderqOS desktop source shared by providing the
// small diagnostic surface it consumes until the packaged ARM64 facade catches
// up. Values below are backed by BCL/Cosmos runtime counters, not random UI data.
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

        public KernelThreadInfo(uint id, uint cpuId, KernelThreadState state,
            bool isIdle, bool isManaged, ulong totalRuntimeNs, nuint stackSize,
            long priority, bool hasPriority)
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
                    long available = info.TotalAvailableMemoryBytes;
                    long committed = info.TotalCommittedBytes;
                    long heap = info.HeapSizeBytes;
                    long value = Math.Max(available, Math.Max(committed, heap));
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

        // Cosmos 3.0.85 ARM64PlatformInitializer.GetCpuCount() currently
        // returns one managed CPU even if QEMU exposes more vCPUs.
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
                {
                    return PortableRuntimeTelemetry.SampleBusyCpuTimeNs(ref cpuState);
                }
            }
        }

        public static int ThreadCount
        {
            get
            {
                lock (SnapshotLock)
                {
                    RefreshProcesses();
                    // Slot zero represents the kernel/main execution context;
                    // every ProcessManager entry owns one managed Thread.
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
                    // ARM64 is single-CPU in current Cosmos. Use the real
                    // cumulative scheduler busy-time as an aggregate CPU0
                    // execution context so Task Manager's logical-CPU graph
                    // still reflects actual utilization.
                    ulong runtime = PortableRuntimeTelemetry.SampleBusyCpuTimeNs(ref cpuState);
                    info = new KernelThreadInfo(
                        uint.MaxValue,
                        0U,
                        KernelThreadState.Running,
                        false,
                        true,
                        runtime,
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
