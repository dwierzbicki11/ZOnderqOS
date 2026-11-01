using System;
using System.Collections.Generic;
using System.Diagnostics;
using Cosmos.Kernel.System.Diagnostics;
using ZonderqOS.SystemCore;

internal static class Program
{
    private static long sink;

    private static void Assert(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    private static KernelProcess LegacyResolve(KernelProcess[] processes, uint tid)
    {
        for (int i = 0; i < processes.Length; i++)
            if (processes[i].KernelThreadId == tid)
                return processes[i];
        return null;
    }

    private static void VerifyIndex()
    {
        var index = new HtopProcessIndex();
        var random = new Random(3095);
        int checks = 0;
        for (int scenario = 0; scenario < 132; scenario++)
        {
            int n = scenario < 130 ? scenario : scenario == 130 ? 512 : 1024;
            var processes = new KernelProcess[n];
            for (int i = 0; i < n; i++)
            {
                // Duplicates, zero TID and unbound TID intentionally included.
                uint tid = i % 17 == 0 ? uint.MaxValue :
                    i % 11 == 0 ? 0u : (uint)random.Next(1, 110);
                processes[i] = new KernelProcess(i + 1, "p" + i, tid);
            }
            index.Rebuild(processes, n);
            Assert(index.UsesDictionary == (n >= 512), "adaptive threshold n=" + n);
            var seen = new HashSet<int>();
            for (uint tid = 0; tid <= 111; tid++)
            {
                KernelProcess expected = LegacyResolve(processes, tid);
                bool found = index.TryResolve(tid, out KernelProcess actual);
                Assert(found == (expected != null), "lookup presence n=" + n + " tid=" + tid);
                Assert(object.ReferenceEquals(actual, expected), "first matching process n=" + n + " tid=" + tid);
                if (expected != null) seen.Add(expected.PID);
                checks += 2;
            }
            KernelProcess missing = LegacyResolve(processes, uint.MaxValue);
            Assert(index.TryResolve(uint.MaxValue, out KernelProcess unbound) == (missing != null),
                "unbound TID preservation");
            Assert(object.ReferenceEquals(unbound, missing), "unbound TID first match");
            if (missing != null) seen.Add(missing.PID);
            for (int i = 0; i < n; i++)
            {
                Assert(index.WasSeen(processes[i].PID) == seen.Contains(processes[i].PID),
                    "missing process n=" + n + " i=" + i);
                checks++;
            }
        }
        index.Rebuild(Array.Empty<KernelProcess>(), 0);
        Assert(!index.TryResolve(0, out _), "stale TID after empty rebuild");
        Assert(!index.WasSeen(1), "stale PID after empty rebuild");
        var large = new KernelProcess[1024];
        for (int i = 0; i < large.Length; i++)
            large[i] = new KernelProcess(i + 1, "p", (uint)(i + 100));
        index.Rebuild(large, 8);
        Assert(!index.UsesDictionary, "few scheduler threads should avoid index build");
        index.Rebuild(large, 1024);
        Assert(index.UsesDictionary, "large join should use dictionary");
        Console.WriteLine("PASS: " + checks + " exact legacy first-match and missing-PID checks");
    }

    private static void VerifyMonitorIntegration()
    {
        ProcessManager.TestProcesses = new[]
        {
            new KernelProcess(7, "alpha", 101),
            new KernelProcess(8, "beta", 102),
            new KernelProcess(9, "idle-process", 104),
            new KernelProcess(10, "unbound", uint.MaxValue),
            new KernelProcess(11, "duplicate", 101)
        };
        SchedulerInfo.Threads = new[]
        {
            new KernelThreadInfo { Id = 101, CpuId = 0, TotalRuntimeNs = 100, State = "RUNNING", IsManaged = true },
            new KernelThreadInfo { Id = 102, CpuId = 0, TotalRuntimeNs = 100, State = "READY" },
            new KernelThreadInfo { Id = 103, CpuId = 0, TotalRuntimeNs = 100, State = "READY" },
            new KernelThreadInfo { Id = 104, CpuId = 0, TotalRuntimeNs = 100, State = "IDLE", IsIdle = true }
        };
        var monitor = new HtopMonitor();
        monitor.SortByCpu = false;
        monitor.Refresh();
        monitor.Refresh();
        string output = string.Join("\n", monitor.Lines);
        Assert(output.Contains("  alpha") && output.Contains("  beta"), "mapped thread names");
        Assert(output.Contains("kernel thread"), "unmapped scheduler thread");
        Assert(output.Contains("START/EXIT    -   idle-process"), "idle thread not counted as process row");
        Assert(output.Contains("START/EXIT    -   unbound"), "unbound process row");
        Assert(output.Contains("START/EXIT    -   duplicate"), "duplicate TID keeps first owner");
        Assert(!output.Contains("START/EXIT    -   alpha"), "matched process must not be missing");
        ProcessManager.TestProcesses = Array.Empty<KernelProcess>();
        SchedulerInfo.Threads = Array.Empty<KernelThreadInfo>();
        monitor.Refresh();
        Assert(!string.Join("\n", monitor.Lines).Contains("alpha"), "stale refresh process");
        Console.WriteLine("PASS: production HtopMonitor.Refresh with synthetic scheduler; no QEMU");
    }

    // This is the original two linear scans, including the second process-to-row pass.
    // Arrays are reused so neither side pays for per-sample allocations.
    private static long LegacyJoin(KernelProcess[] processes, uint[] tids, int[] rowPids)
    {
        long checksum = 0;
        for (int t = 0; t < tids.Length; t++)
        {
            int pid = 0;
            for (int p = 0; p < processes.Length; p++)
            {
                if (processes[p].KernelThreadId != tids[t]) continue;
                pid = processes[p].PID;
                break;
            }
            rowPids[t] = pid;
            checksum += pid;
        }
        for (int p = 0; p < processes.Length; p++)
        {
            bool found = false;
            for (int t = 0; t < tids.Length; t++)
            {
                if (rowPids[t] != processes[p].PID) continue;
                found = true;
                break;
            }
            if (!found) checksum += processes[p].PID;
        }
        return checksum;
    }

    private static long IndexedJoin(HtopProcessIndex index, KernelProcess[] processes, uint[] tids)
    {
        index.Rebuild(processes, tids.Length);
        long checksum = 0;
        for (int t = 0; t < tids.Length; t++)
            if (index.TryResolve(tids[t], out KernelProcess process))
                checksum += process.PID;
        for (int p = 0; p < processes.Length; p++)
            if (!index.WasSeen(processes[p].PID))
                checksum += processes[p].PID;
        return checksum;
    }

    private static double Time(Func<long> action, int repetitions)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repetitions; i++)
            sink ^= action();
        long end = Stopwatch.GetTimestamp();
        return (double)(end - start) * 1000d / Stopwatch.Frequency / repetitions;
    }

    private static double Median(double[] samples)
    {
        Array.Sort(samples);
        return samples[samples.Length / 2];
    }

    private static void Benchmark(int count, int threads)
    {
        var processes = new KernelProcess[count];
        var tids = new uint[threads];
        var rowPids = new int[threads];
        for (int i = 0; i < count; i++)
            processes[i] = new KernelProcess(i + 1, "p", (uint)(10000 + i));
        for (int i = 0; i < threads; i++)
            tids[i] = i % 8 == 0 ? (uint)(500000 + i) : (uint)(10000 + i % count);
        var index = new HtopProcessIndex();
        Func<long> old = () => LegacyJoin(processes, tids, rowPids);
        Func<long> current = () => IndexedJoin(index, processes, tids);
        Assert(old() == current(), "A/B checksum mismatch n=" + count + " threads=" + threads);
        for (int i = 0; i < 10; i++) { old(); current(); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++) sink ^= current();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert(allocated == 0, "warm index allocation: " + allocated + " bytes");
        double[] legacy = new double[7], optimized = new double[7];
        int repetitions = count <= 128 ? 50 : count <= 512 ? 10 : 2;
        for (int i = 0; i < legacy.Length; i++)
        {
            legacy[i] = Time(old, repetitions);
            optimized[i] = Time(current, repetitions);
        }
        double a = Median(legacy), b = Median(optimized);
        Console.WriteLine("A/B processes=" + count + " threads=" + threads +
            " legacy=" + a.ToString("F4") +
            "ms indexed=" + b.ToString("F4") + "ms ratio=" + (a / b).ToString("F2") +
            "x warm-index-alloc=" + allocated + "B (median 7 interleaved runs)");
    }

    private static int Main()
    {
        VerifyIndex();
        VerifyMonitorIntegration();
        Benchmark(16, 16);
        Benchmark(128, 128);
        Benchmark(512, 512);
        Benchmark(2048, 2048);
        Benchmark(1024, 8);
        Benchmark(64, 4096);
        GC.KeepAlive(sink);
        Console.WriteLine("PASS: host-only htop index equivalence, allocation and measured A/B; not kernel CPU/idle.");
        return 0;
    }
}
