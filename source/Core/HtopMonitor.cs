using System;
using System.Collections.Generic;
using System.Diagnostics;
using Cosmos.Kernel.System.Diagnostics;

namespace ZonderqOS.SystemCore
{
    internal static class SchedulerTelemetry
    {
        internal static uint CurrentThreadId() => SchedulerInfo.CurrentThreadId;
        internal static long CpuBusyTicks(uint cpuId) => SchedulerInfo.GetCpuBusyTicks(cpuId);
        internal static bool HasCpuAccounting => SchedulerInfo.CpuAccountingAvailable;
    }

    internal sealed class HtopMonitor
    {
        private struct PreviousThread
        {
            internal uint Id;
            internal ulong Runtime;
            internal bool Valid;
        }

        private struct Row
        {
            internal KernelThreadInfo Thread;
            internal int Percent;
            internal int Pid;
            internal string Name;
        }

        private readonly List<KernelProcess> processes = new List<KernelProcess>();
        private readonly List<Row> rows = new List<Row>();
        private readonly List<string> lines = new List<string>();
        private PreviousThread[] previousThreads = Array.Empty<PreviousThread>();
        private long[] previousCpu = Array.Empty<long>();
        private int[] cpuUsage = Array.Empty<int>();
        private long previousTimestamp;
        internal bool SortByCpu = true;
        internal int Scroll;
        internal int CpuCount { get; private set; }
        internal int TotalCpuPercent { get; private set; }
        internal ulong ActiveCpuMask { get; private set; }
        internal IReadOnlyList<int> CpuUsagePercent => cpuUsage;
        internal IReadOnlyList<string> Lines => lines;

        internal void Refresh()
        {
            int count = checked((int)SchedulerInfo.CpuCount);
            long now = Stopwatch.GetTimestamp();
            long elapsed = previousTimestamp == 0 ? 0 : Math.Max(0, now - previousTimestamp);
            bool warm = elapsed > 0;
            if (previousCpu.Length != count)
            {
                previousCpu = new long[count];
                cpuUsage = new int[count];
                warm = false;
            }
            CpuCount = count;
            lines.Clear();
            lines.Add("ZOnderqOS htop | CPU: " + count + " | " + SchedulerInfo.SchedulerName);
            lines.Add("Q/Esc: exit  C: sort CPU  P: sort PID  Up/Down: scroll");
            lines.Add("CPU usage: scheduled work per logical CPU (1 second samples)");
            ActiveCpuMask = 0;
            ulong totalBusyDelta = 0;
            bool hasAccounting = SchedulerTelemetry.HasCpuAccounting;
            for (int cpu = 0; cpu < count; cpu++)
            {
                long busy = hasAccounting ? SchedulerTelemetry.CpuBusyTicks((uint)cpu) : 0;
                ulong delta = busy >= previousCpu[cpu] ? (ulong)(busy - previousCpu[cpu]) : 0;
                if (warm && delta > 0 && cpu < 64)
                {
                    ActiveCpuMask |= 1UL << cpu;
                }
                if (warm && hasAccounting)
                {
                    totalBusyDelta += delta;
                }
                int usage = warm && hasAccounting ? Percent(delta, (ulong)elapsed) : 0;
                cpuUsage[cpu] = usage;
                string value = !hasAccounting ? "N/A" : !warm ? "..." : usage + "%";
                lines.Add("CPU " + cpu.ToString().PadLeft(3) + " [" + new string('|', usage / 5).PadRight(20) + "] " + value);
                previousCpu[cpu] = busy;
            }
            ulong totalCapacity = warm && count > 0 ? (ulong)elapsed * (ulong)count : 0;
            TotalCpuPercent = warm && hasAccounting ? Percent(totalBusyDelta, totalCapacity) : 0;
            lines.Add("CPU total: " + (!hasAccounting ? "N/A" : !warm ? "..." : TotalCpuPercent + "%"));

            // These are Cosmos' real physical page-allocator figures, not GC heap estimates.
            ulong total = MemoryInfo.TotalPages;
            ulong free = Math.Min(total, MemoryInfo.FreePages);
            ulong pageSize = MemoryInfo.PageSizeBytes;
            lines.Add("Mem: " + ((total - free) * pageSize / 1048576UL) + " / " + (total * pageSize / 1048576UL) + " MiB");
            ProcessManager.FillActiveProcesses(processes);
            lines.Add("Processes: " + processes.Count + "  Threads: " + SchedulerInfo.ThreadCount);
            lines.Add("  PID    TID  CPU  STATE       CPU%  NAME");

            int slots = SchedulerInfo.ThreadSlotCount;
            if (previousThreads.Length < slots)
            {
                Array.Resize(ref previousThreads, slots);
            }
            rows.Clear();
            ulong elapsedNs = elapsed > 0 && Stopwatch.Frequency > 0
                ? (ulong)((double)elapsed * 1000000000d / Stopwatch.Frequency) : 0;
            for (int slot = 0; slot < slots; slot++)
            {
                if (!SchedulerInfo.TryGetThreadInSlot(slot, out KernelThreadInfo info))
                {
                    previousThreads[slot].Valid = false;
                    continue;
                }
                PreviousThread previous = previousThreads[slot];
                ulong delta = warm && previous.Valid && previous.Id == info.Id && info.TotalRuntimeNs >= previous.Runtime
                    ? info.TotalRuntimeNs - previous.Runtime : 0;
                previousThreads[slot] = new PreviousThread { Id = info.Id, Runtime = info.TotalRuntimeNs, Valid = true };
                if (info.IsIdle)
                {
                    continue;
                }
                int pid = 0;
                string name = info.IsManaged ? "managed thread" : "kernel thread";
                for (int index = 0; index < processes.Count; index++)
                {
                    if (processes[index].KernelThreadId != info.Id)
                    {
                        continue;
                    }
                    pid = processes[index].PID;
                    name = processes[index].Name;
                    break;
                }
                rows.Add(new Row { Thread = info, Percent = Percent(delta, elapsedNs), Pid = pid, Name = name });
            }
            rows.Sort(CompareRows);
            foreach (Row row in rows)
            {
                lines.Add((row.Pid == 0 ? "-" : row.Pid.ToString()).PadLeft(5) + " " + row.Thread.Id.ToString().PadLeft(6) + " " +
                    row.Thread.CpuId.ToString().PadLeft(4) + "  " + row.Thread.State.ToString().PadRight(10) + " " +
                    row.Percent.ToString().PadLeft(4) + "%  " + row.Name);
            }
            foreach (KernelProcess process in processes)
            {
                bool found = false;
                foreach (Row row in rows)
                {
                    if (row.Pid == process.PID)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    lines.Add(process.PID.ToString().PadLeft(5) + "      -    -  START/EXIT    -   " + process.Name);
                }
            }
            previousTimestamp = now;
            Scroll = Math.Max(0, Math.Min(Scroll, Math.Max(0, lines.Count - 1)));
        }

        private int CompareRows(Row a, Row b)
        {
            int compare = SortByCpu ? b.Percent.CompareTo(a.Percent) : a.Pid.CompareTo(b.Pid);
            return compare != 0 ? compare : a.Thread.Id.CompareTo(b.Thread.Id);
        }

        internal static int Percent(ulong busy, ulong elapsed)
        {
            if (elapsed == 0)
            {
                return 0;
            }
            if (busy >= elapsed)
            {
                return 100;
            }
            return (int)((double)busy * 100d / elapsed);
        }
    }
}
