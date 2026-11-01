using System;
using System.Collections.Generic;

namespace Cosmos.Kernel.System.Diagnostics
{
    internal struct KernelThreadInfo
    {
        internal uint Id;
        internal uint CpuId;
        internal ulong TotalRuntimeNs;
        internal bool IsIdle;
        internal bool IsManaged;
        internal string State;
    }

    internal static class SchedulerInfo
    {
        internal static uint CpuCount => 1;
        internal static string SchedulerName => "stub-scheduler";
        internal static KernelThreadInfo[] Threads = Array.Empty<KernelThreadInfo>();
        internal static int ThreadCount => Threads.Length;
        internal static int ThreadSlotCount => Threads.Length;
        internal static bool TryGetThreadInSlot(int slot, out KernelThreadInfo thread)
        {
            if (slot < 0 || slot >= Threads.Length)
            {
                thread = default;
                return false;
            }
            thread = Threads[slot];
            return true;
        }
    }

    internal static class SchedulerDiagnostics
    {
        internal static bool TryGetCurrentThread(uint cpu, out KernelThreadInfo thread)
        {
            thread = default;
            return false;
        }
    }

    internal static class MemoryInfo
    {
        internal static ulong TotalPages => 2048;
        internal static ulong FreePages => 1024;
        internal static ulong PageSizeBytes => 4096;
    }
}

namespace ZonderqOS.SystemCore
{
    internal sealed class KernelProcess
    {
        internal int PID { get; }
        internal string Name { get; }
        internal uint KernelThreadId { get; }
        internal KernelProcess(int pid, string name, uint tid)
        {
            PID = pid;
            Name = name;
            KernelThreadId = tid;
        }
    }

    internal static class ProcessManager
    {
        internal static KernelProcess[] TestProcesses = Array.Empty<KernelProcess>();
        internal static int FillActiveProcesses(List<KernelProcess> destination)
        {
            destination.Clear();
            destination.AddRange(TestProcesses);
            return destination.Count;
        }
    }
}
