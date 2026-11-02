using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Cosmos.Kernel.System.Diagnostics;

namespace ZonderqOS.SystemCore
{
#if ZONDERQ_COSMOS_INPUT_API
    // Upstream 3.0.94's stock scheduler has real global BusyCpuTimeNs but
    // does not expose Stage-23 per-logical-CPU accounting or the private
    // RhScheduler* exports. Do not invent per-core load measurements.
    internal static class SchedulerTelemetry
    {
        internal static bool HasCpuAccounting => false;
        internal static long CpuBusyTicks(uint cpuId) => 0;

        internal static uint CurrentThreadId()
        {
            // The unpatched v3.0.95 test release runs on its BSP.
            return SchedulerDiagnostics.TryGetCurrentThread(0, out KernelThreadInfo thread)
                ? thread.Id : uint.MaxValue;
        }

        internal static void EnableCurrentManagedPreemption()
        {
            // Stock v3.0.95 scheduler owns preemption; private Stage-23
            // managed-AP entry is not present and must not be called.
        }
    }
#else
    internal static partial class SchedulerTelemetry
    {
        [LibraryImport("*", EntryPoint = "RhGetCurrentSchedulerThreadId")]
        [SuppressGCTransition]
        internal static partial uint CurrentThreadId();

        [LibraryImport("*", EntryPoint = "RhSchedulerCpuAccountingAvailable")]
        [SuppressGCTransition]
        private static partial int AccountingAvailable();

        [LibraryImport("*", EntryPoint = "RhGetSchedulerCpuBusyTicks")]
        [SuppressGCTransition]
        internal static partial long CpuBusyTicks(uint cpuId);

        [LibraryImport("*", EntryPoint = "RhEnableCurrentCpuManagedPreemption")]
        [SuppressGCTransition]
        internal static partial void EnableCurrentManagedPreemption();

        internal static bool HasCpuAccounting => AccountingAvailable() == 1;
    }
#endif

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
            internal int Slot;
            internal int Percent;
            internal int Pid;
            internal string Name;
        }

        private readonly List<KernelProcess> processes = new List<KernelProcess>();
        private readonly HtopProcessIndex processIndex = new HtopProcessIndex();
        private readonly List<Row> rows = new List<Row>();
        // An instance method-group conversion allocates a Comparison<Row> delegate.
        // Reuse it across htop refreshes; the callback reads SortByCpu live.
        private readonly Comparison<Row> rowComparison;
        private struct CachedThreadRow
        {
            internal uint Tid;
            internal uint CpuId;
            internal int Pid;
            internal int Percent;
            internal KernelThreadState State;
            internal string Name;
            internal string Line;
        }

        // The scheduler slot is stable independently of display sort order.
        // Reuse complete rows only when every visible field still matches.
        private CachedThreadRow[] cachedThreadRows = Array.Empty<CachedThreadRow>();
        private int lastRowSlotCount;
        private readonly List<string> lines = new List<string>();
        private PreviousThread[] previousThreads = Array.Empty<PreviousThread>();
        private long[] previousCpu = Array.Empty<long>();
        private int[] cpuUsage = Array.Empty<int>();
        private long previousTimestamp;

        // CPU topology and scheduler name are normally stable for the life of
        // a monitor. Reuse the immutable header instead of formatting it on
        // every refresh; invalidate on either value changing.
        private int headerCpuCount = -1;
        private string headerSchedulerName;
        private string headerLine;

        // Physical-page telemetry is often unchanged between adjacent htop
        // refreshes. Avoid rebuilding numeric strings and a combined line
        // when the actual allocator snapshot has not changed. All three
        // inputs participate in invalidation; this is not a stale RAM gauge.
        private ulong memoryTotalPages = ulong.MaxValue;
        private ulong memoryFreePages = ulong.MaxValue;
        private ulong memoryPageSize = ulong.MaxValue;
        private string memoryLine;

        // Process/thread counts are frequently stable even when runtime
        // counters change. Keep the last immutable label, but rebuild on
        // either count changing (including process exits).
        private int processLabelCount = -1;
        private int threadLabelCount = -1;
        private string processLabelLine;

        internal string GetHeaderLine(int count, string schedulerName)
        {
            if (headerLine != null && count == headerCpuCount &&
                string.Equals(schedulerName, headerSchedulerName, StringComparison.Ordinal))
                return headerLine;

            headerCpuCount = count;
            headerSchedulerName = schedulerName;
            headerLine = "ZOnderqOS htop | CPU: " + count + " | " + schedulerName;
            return headerLine;
        }

        internal string GetMemoryLine(ulong totalPages, ulong freePages, ulong pageSize)
        {
            // Preserve the original FreePages clamp even for inconsistent
            // firmware telemetry. Cache the clamped value actually displayed.
            ulong free = Math.Min(totalPages, freePages);
            if (memoryLine != null && totalPages == memoryTotalPages &&
                free == memoryFreePages && pageSize == memoryPageSize)
                return memoryLine;

            memoryTotalPages = totalPages;
            memoryFreePages = free;
            memoryPageSize = pageSize;
            memoryLine = "Mem: " + ((totalPages - free) * pageSize / 1048576UL) +
                " / " + (totalPages * pageSize / 1048576UL) + " MiB";
            return memoryLine;
        }

        internal string GetProcessCountLine(int processCount, int threadCount)
        {
            if (processLabelLine != null && processCount == processLabelCount &&
                threadCount == threadLabelCount)
                return processLabelLine;

            processLabelCount = processCount;
            threadLabelCount = threadCount;
            processLabelLine = "Processes: " + processCount + "  Threads: " + threadCount;
            return processLabelLine;
        }

        // CPU bars are redrawn on every htop refresh. Construct the 21 possible
        // 20-column strings once, rather than allocating one or two strings per
        // CPU on every sample (including the stock BSP's N/A accounting path).
        private static readonly string[] CachedCpuBars = BuildCpuBars();

        private static string[] BuildCpuBars()
        {
            var bars = new string[21];
            for (int filled = 0; filled < bars.Length; filled++)
                bars[filled] = new string('|', filled).PadRight(20);
            return bars;
        }

        // Percent() returns 0..100; every five percent is one column.
        // Exposed internally so the host smoke test uses the production cache.
        internal static string CpuBar(int percent) => CachedCpuBars[percent / 5];

        // Prefix formatting used ToString, PadLeft and concatenation for every
        // CPU on every refresh. Keep the common 0..63 labels immutable and
        // format unusual larger CPU indices on demand without a hard limit.
        private static readonly string[] CachedCpuPrefixes = BuildCpuPrefixes();

        private static string[] BuildCpuPrefixes()
        {
            var labels = new string[64];
            for (int cpu = 0; cpu < labels.Length; cpu++)
                labels[cpu] = "CPU " + cpu.ToString().PadLeft(3) + " [";
            return labels;
        }

        internal static string CpuPrefix(int cpu) =>
            (uint)cpu < (uint)CachedCpuPrefixes.Length
                ? CachedCpuPrefixes[cpu]
                : "CPU " + cpu.ToString().PadLeft(3) + " [";

        // Stock Cosmos 3.0.95 does not expose per-CPU busy accounting.
        // Cache the complete N/A line for the same 0..63 logical CPUs as
        // CachedCpuPrefixes. Previously CPU 16..63 formatted a fresh line
        // on every htop refresh even though their values never change.
        // The small, bounded cache is allocated once, never in Refresh().
        private static readonly string[] CachedCpuUnavailableLines = BuildCpuUnavailableLines();

        private static string[] BuildCpuUnavailableLines()
        {
            var lines = new string[64];
            for (int cpu = 0; cpu < lines.Length; cpu++)
                lines[cpu] = CpuPrefix(cpu) + CpuBar(0) + "] N/A";
            return lines;
        }

        internal static string CpuUnavailableLine(int cpu) =>
            (uint)cpu < (uint)CachedCpuUnavailableLines.Length
                ? CachedCpuUnavailableLines[cpu]
                : CpuPrefix(cpu) + CpuBar(0) + "] N/A";

        // Cached immutable numeric CPU labels avoid allocating strings for each
        // core, process row and total on every monitor refresh. Percent() always
        // returns 0..100, so array indexing is safe for all production callers.
        private static readonly string[] CachedCpuPercents = BuildCpuPercents(false);
        private static readonly string[] CachedPaddedCpuPercents = BuildCpuPercents(true);

        private static string[] BuildCpuPercents(bool padded)
        {
            var values = new string[101];
            for (int percent = 0; percent < values.Length; percent++)
            {
                string digits = percent.ToString();
                values[percent] = (padded ? digits.PadLeft(4) : digits) + "%";
            }
            return values;
        }

        internal static string CpuPercentText(int percent) => CachedCpuPercents[percent];
        internal static string PaddedCpuPercentText(int percent) => CachedPaddedCpuPercents[percent];

        internal HtopMonitor()
        {
            rowComparison = CompareRows;
        }

        internal bool SortByCpu = true;
        internal int Scroll;
        internal int CpuCount { get; private set; }
        internal int TotalCpuPercent { get; private set; }
        internal ulong ActiveCpuMask { get; private set; }
        internal IReadOnlyList<int> CpuUsagePercent => cpuUsage;
        internal IReadOnlyList<string> Lines => lines;

        internal string GetThreadRowLine(int slot, KernelThreadInfo thread, int pid, int percent, string name)
        {
            if (slot < 0)
                throw new ArgumentOutOfRangeException(nameof(slot));
            if (slot >= cachedThreadRows.Length)
                Array.Resize(ref cachedThreadRows, slot + 1);

            CachedThreadRow cached = cachedThreadRows[slot];
            if (cached.Line != null && cached.Tid == thread.Id &&
                cached.CpuId == thread.CpuId && cached.Pid == pid &&
                cached.Percent == percent &&
                cached.State == thread.State &&
                string.Equals(cached.Name, name, StringComparison.Ordinal))
                return cached.Line;

            string line = (pid == 0 ? "-" : pid.ToString()).PadLeft(5) + " " +
                thread.Id.ToString().PadLeft(6) + " " +
                thread.CpuId.ToString().PadLeft(4) + "  " +
                thread.State.ToString().PadRight(10) + " " +
                PaddedCpuPercentText(percent) + "  " + name;
            cachedThreadRows[slot] = new CachedThreadRow
            {
                Tid = thread.Id, CpuId = thread.CpuId, Pid = pid,
                Percent = percent, State = thread.State, Name = name, Line = line
            };
            return line;
        }

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
            lines.Add(GetHeaderLine(count, SchedulerInfo.SchedulerName));
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
                lines.Add(!hasAccounting
                    ? CpuUnavailableLine(cpu)
                    : CpuPrefix(cpu) + CpuBar(usage) + "] " +
                        (!warm ? "..." : CpuPercentText(usage)));
                previousCpu[cpu] = busy;
            }
            ulong totalCapacity = warm && count > 0 ? (ulong)elapsed * (ulong)count : 0;
            TotalCpuPercent = warm && hasAccounting ? Percent(totalBusyDelta, totalCapacity) : 0;
            // Stock Cosmos exposes no per-core busy accounting. Avoid
            // allocating the same immutable N/A total on every refresh.
            lines.Add(!hasAccounting ? "CPU total: N/A" :
                "CPU total: " + (!warm ? "..." : CpuPercentText(TotalCpuPercent)));

            // These are Cosmos' real physical page-allocator figures, not GC heap estimates.
            ulong total = MemoryInfo.TotalPages;
            ulong free = MemoryInfo.FreePages;
            ulong pageSize = MemoryInfo.PageSizeBytes;
            lines.Add(GetMemoryLine(total, free, pageSize));
            ProcessManager.FillActiveProcesses(processes);
            int slots = SchedulerInfo.ThreadSlotCount;
            bool indexedJoin = HtopProcessIndex.ShouldIndex(processes.Count, slots);
            if (indexedJoin)
                processIndex.Rebuild(processes);
            else
                processIndex.Clear();
            lines.Add(GetProcessCountLine(processes.Count, SchedulerInfo.ThreadCount));
            lines.Add("  PID    TID  CPU  STATE       CPU%  NAME");

            if (previousThreads.Length < slots)
            {
                Array.Resize(ref previousThreads, slots);
            }
            if (cachedThreadRows.Length < slots)
                Array.Resize(ref cachedThreadRows, slots);
            // Release names/lines from slots that disappeared. Do this only
            // when the scheduler shrinks, never on each steady refresh.
            if (slots < lastRowSlotCount)
                Array.Clear(cachedThreadRows, slots, lastRowSlotCount - slots);
            lastRowSlotCount = slots;
            rows.Clear();
            ulong elapsedNs = elapsed > 0 && Stopwatch.Frequency > 0
                ? (ulong)((double)elapsed * 1000000000d / Stopwatch.Frequency) : 0;
            for (int slot = 0; slot < slots; slot++)
            {
                if (!SchedulerInfo.TryGetThreadInSlot(slot, out KernelThreadInfo info))
                {
                    previousThreads[slot].Valid = false;
                    cachedThreadRows[slot] = default;
                    continue;
                }
                PreviousThread previous = previousThreads[slot];
                ulong delta = warm && previous.Valid && previous.Id == info.Id && info.TotalRuntimeNs >= previous.Runtime
                    ? info.TotalRuntimeNs - previous.Runtime : 0;
                previousThreads[slot] = new PreviousThread { Id = info.Id, Runtime = info.TotalRuntimeNs, Valid = true };
                if (info.IsIdle)
                {
                    cachedThreadRows[slot] = default;
                    continue;
                }
                int pid = 0;
                string name = info.IsManaged ? "managed thread" : "kernel thread";
                if (indexedJoin)
                {
                    if (processIndex.TryResolve(info.Id, out KernelProcess process))
                    {
                        pid = process.PID;
                        name = process.Name;
                    }
                }
                else
                {
                    // Keep the original tight List<T> scan for small snapshots.
                    // Building and probing a hash table regresses this hot path.
                    for (int index = 0; index < processes.Count; index++)
                    {
                        if (processes[index].KernelThreadId != info.Id)
                            continue;
                        pid = processes[index].PID;
                        name = processes[index].Name;
                        break;
                    }
                }
                rows.Add(new Row { Thread = info, Slot = slot, Percent = Percent(delta, elapsedNs), Pid = pid, Name = name });
            }
            SortRows();
            foreach (Row row in rows)
                lines.Add(GetThreadRowLine(row.Slot, row.Thread, row.Pid, row.Percent, row.Name));
            foreach (KernelProcess process in processes)
            {
                bool found = indexedJoin && processIndex.WasSeen(process.PID);
                if (!indexedJoin)
                {
                    foreach (Row row in rows)
                    {
                        if (row.Pid != process.PID)
                            continue;
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

        // Internal entry point lets the host smoke test exercise the actual
        // production sort without scheduler sampling or output allocations.
        internal void SortRows()
        {
            rows.Sort(rowComparison);
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

    /// <summary>
    /// Reusable hash index for large htop thread/process joins. Small joins
    /// retain the original linear scans in HtopMonitor (faster on host .NET).
    /// The first process for a TID wins; Clear releases stale references.
    /// </summary>
    internal sealed class HtopProcessIndex
    {
        private readonly Dictionary<uint, KernelProcess> byThread =
            new Dictionary<uint, KernelProcess>();
        private readonly HashSet<int> seenPids = new HashSet<int>();

        internal static bool ShouldIndex(int processCount, int threadSlots) =>
            processCount >= 256 && (long)processCount * Math.Max(0, threadSlots) >= 131072;

        internal void Clear()
        {
            byThread.Clear();
            seenPids.Clear();
        }

        internal void Rebuild(IReadOnlyList<KernelProcess> snapshot)
        {
            Clear();
            for (int i = 0; i < snapshot.Count; i++)
            {
                KernelProcess process = snapshot[i];
                uint tid = process.KernelThreadId;
                // Preserve first-owner semantics for duplicate TIDs with one
                // dictionary probe instead of ContainsKey followed by Add.
                byThread.TryAdd(tid, process);
            }
        }

        internal bool TryResolve(uint threadId, out KernelProcess process)
        {
            if (!byThread.TryGetValue(threadId, out process))
                return false;
            seenPids.Add(process.PID);
            return true;
        }

        internal bool WasSeen(int pid) => seenPids.Contains(pid);
    }
}
