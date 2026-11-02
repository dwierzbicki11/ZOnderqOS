using System;
using System.Diagnostics;
using Cosmos.Kernel.System.Diagnostics;
using ZonderqOS.SystemCore;

internal static class ThreadRowSmoke
{
    private static long sink;
    private static void Check(bool ok, string msg)
    {
        if (!ok) throw new Exception(msg);
    }

    // Exact former production row expression, independent of the cache.
    private static string Legacy(KernelThreadInfo t, int pid, int pct, string name) =>
        (pid == 0 ? "-" : pid.ToString()).PadLeft(5) + " " +
        t.Id.ToString().PadLeft(6) + " " +
        t.CpuId.ToString().PadLeft(4) + "  " +
        t.State.ToString().PadRight(10) + " " +
        HtopMonitor.PaddedCpuPercentText(pct) + "  " + name;

    private static void Verify()
    {
        var m = new HtopMonitor();
        var t = new KernelThreadInfo { Id = 17, CpuId = 0, State = KernelThreadState.READY, IsManaged = true };
        string initial = m.GetThreadRowLine(0, t, 7, 0, "shell");
        Check(initial == Legacy(t, 7, 0, "shell"), "first row output");
        Check(object.ReferenceEquals(initial, m.GetThreadRowLine(0, t, 7, 0, "shell")),
            "stable row reference");
        t.CpuId = 2;
        string moved = m.GetThreadRowLine(0, t, 7, 0, "shell");
        Check(moved == Legacy(t, 7, 0, "shell") && !object.ReferenceEquals(initial, moved),
            "CPU migration invalidation");
        t.State = KernelThreadState.WAITING;
        Check(m.GetThreadRowLine(0, t, 7, 0, "shell") == Legacy(t, 7, 0, "shell"),
            "state invalidation");
        Check(m.GetThreadRowLine(0, t, 7, 33, "shell") == Legacy(t, 7, 33, "shell"),
            "CPU percent invalidation");
        Check(m.GetThreadRowLine(0, t, 8, 33, "shell") == Legacy(t, 8, 33, "shell"),
            "PID invalidation");
        Check(m.GetThreadRowLine(0, t, 8, 33, "renamed") == Legacy(t, 8, 33, "renamed"),
            "name invalidation");
        t.Id++;
        Check(m.GetThreadRowLine(0, t, 8, 33, "renamed") == Legacy(t, 8, 33, "renamed"),
            "TID invalidation");
        Check(m.GetThreadRowLine(1, t, 0, 0, "kernel thread") ==
            Legacy(t, 0, 0, "kernel thread"), "slot and missing PID");
        var rng = new Random(3095);
        for (int i = 0; i < 20000; i++)
        {
            int slot = rng.Next(128);
            t.Id = (uint)rng.Next(10000);
            t.CpuId = (uint)rng.Next(16);
            t.State = i % 2 == 0 ? KernelThreadState.READY : KernelThreadState.WAITING;
            int pid = rng.Next(5000), pct = rng.Next(101);
            string name = i % 3 == 0 ? "shell" : "worker";
            string actual = m.GetThreadRowLine(slot, t, pid, pct, name);
            Check(actual == Legacy(t, pid, pct, name), "random row " + i);
            Check(object.ReferenceEquals(actual, m.GetThreadRowLine(slot, t, pid, pct, name)),
                "random cache hit " + i);
        }

        KernelProcess[] oldP = ProcessManager.TestProcesses;
        KernelThreadInfo[] oldT = SchedulerInfo.Threads;
        try
        {
            ProcessManager.TestProcesses = new[] { new KernelProcess(42, "demo", 900) };
            SchedulerInfo.Threads = new[] { new KernelThreadInfo
            {
                Id = 900, CpuId = 0, State = KernelThreadState.READY, IsManaged = true
            }};
            m.Refresh();
            string row = m.Lines[8];
            Check(row == Legacy(SchedulerInfo.Threads[0], 42, 0, "demo"),
                "production Refresh row");
            m.Refresh();
            Check(object.ReferenceEquals(row, m.Lines[8]),
                "production Refresh row reallocated");
            ProcessManager.TestProcesses = new[] { new KernelProcess(42, "renamed", 900) };
            m.Refresh();
            Check(m.Lines[8] == Legacy(SchedulerInfo.Threads[0], 42, 0, "renamed"),
                "production process rename");
            SchedulerInfo.Threads = new[] { new KernelThreadInfo
            {
                Id = 901, CpuId = 0, State = KernelThreadState.WAITING, IsManaged = true
            }};
            m.Refresh();
            Check(m.Lines[8] == Legacy(SchedulerInfo.Threads[0], 0, 0, "managed thread"),
                "production reused stale slot owner");
            SchedulerInfo.Threads = new[] { new KernelThreadInfo
            {
                Id = 901, CpuId = 0, State = KernelThreadState.WAITING, IsIdle = true
            }};
            m.Refresh();
            Check(m.Lines.Count == 9 && m.Lines[8].Contains("START/EXIT"),
                "idle thread must not render as a thread row");
        }
        finally
        {
            ProcessManager.TestProcesses = oldP;
            SchedulerInfo.Threads = oldT;
        }
        Console.WriteLine("PASS: htop thread rows 40000+ output/reference checks and Refresh invalidation");
    }

    private static double Time(Func<long> action)
    {
        const int repeats = 200;
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++) sink ^= action();
        return (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency / repeats;
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }

    private static void Benchmark()
    {
        var m = new HtopMonitor();
        var t = new KernelThreadInfo { Id = 900, CpuId = 0, State = KernelThreadState.READY };
        int pid = 42, pct = 0;
        string name = "shell";
        Func<long> before = () =>
        {
            long sum = 0;
            for (int i = 0; i < 1024; i++)
            {
                string line = Legacy(t, pid, pct, name);
                sum += line.Length + line[0] + line[line.Length - 1];
            }
            return sum;
        };
        Func<long> after = () =>
        {
            long sum = 0;
            for (int i = 0; i < 1024; i++)
            {
                string line = m.GetThreadRowLine(0, t, pid, pct, name);
                sum += line.Length + line[0] + line[line.Length - 1];
            }
            return sum;
        };
        Check(before() == after(), "A/B row checksum");
        for (int i = 0; i < 16; i++) { sink ^= before(); sink ^= after(); }
        long start = GC.GetAllocatedBytesForCurrentThread();
        sink ^= before();
        long oldBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        sink ^= after();
        long newBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Check(oldBytes > 0 && newBytes == 0, "A/B allocations " + oldBytes + " -> " + newBytes);
        double[] a = new double[7], b = new double[7];
        for (int i = 0; i < 7; i++)
        {
            if ((i & 1) == 0) { a[i] = Time(before); b[i] = Time(after); }
            else { b[i] = Time(after); a[i] = Time(before); }
        }
        double oldMs = Median(a), newMs = Median(b);
        Console.WriteLine("HOST A/B 1024 stable htop thread rows: old=" +
            oldMs.ToString("F5") + "ms cached=" + newMs.ToString("F5") +
            "ms ratio=" + (oldMs / newMs).ToString("F2") +
            "x allocations=" + oldBytes + "B -> " + newBytes +
            "B (7 alternating samples; not kernel CPU/idle)");
    }

    private static void VerifyStableRefreshAllocations()
    {
        KernelProcess[] oldP = ProcessManager.TestProcesses;
        KernelThreadInfo[] oldT = SchedulerInfo.Threads;
        try
        {
            var threads = new KernelThreadInfo[128];
            for (int i = 0; i < threads.Length; i++)
                threads[i] = new KernelThreadInfo
                {
                    Id = (uint)(1000 + i), CpuId = 0,
                    State = i % 2 == 0 ? KernelThreadState.READY : KernelThreadState.WAITING,
                    IsManaged = true
                };
            ProcessManager.TestProcesses = Array.Empty<KernelProcess>();
            SchedulerInfo.Threads = threads;
            var m = new HtopMonitor();
            for (int i = 0; i < 12; i++) m.Refresh();
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) m.Refresh();
            long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
            Check(bytes == 0, "100 stable production Refresh calls allocated " + bytes + " B");
            Check(m.Lines.Count == 136, "128 thread rows and 8 fixed lines");
            Console.WriteLine("HOST htop 100 stable 128-thread production Refresh calls: " +
                bytes + " B allocations (not kernel GC/CPU)");
        }
        finally
        {
            ProcessManager.TestProcesses = oldP;
            SchedulerInfo.Threads = oldT;
        }
    }

    private static void BenchmarkChangingRows()
    {
        var m = new HtopMonitor();
        var t = new KernelThreadInfo { Id = 900, CpuId = 0, State = KernelThreadState.READY };
        int pid = 42;
        string name = "shell";
        Func<long> before = () =>
        {
            long sum = 0;
            for (int i = 0; i < 1024; i++)
            {
                string line = Legacy(t, pid, i % 101, name);
                sum += line.Length + line[0] + line[line.Length - 1];
            }
            return sum;
        };
        Func<long> after = () =>
        {
            long sum = 0;
            for (int i = 0; i < 1024; i++)
            {
                string line = m.GetThreadRowLine(0, t, pid, i % 101, name);
                sum += line.Length + line[0] + line[line.Length - 1];
            }
            return sum;
        };
        Check(before() == after(), "changing row A/B checksum");
        for (int i = 0; i < 12; i++) { sink ^= before(); sink ^= after(); }
        double[] a = new double[7], b = new double[7];
        for (int i = 0; i < 7; i++)
        {
            if ((i & 1) == 0) { a[i] = Time(before); b[i] = Time(after); }
            else { b[i] = Time(after); a[i] = Time(before); }
        }
        double oldMs = Median(a), newMs = Median(b);
        Console.WriteLine("HOST A/B 1024 changing htop thread rows: old=" +
            oldMs.ToString("F5") + "ms cached=" + newMs.ToString("F5") +
            "ms ratio=" + (oldMs / newMs).ToString("F2") +
            "x (7 alternating samples; cache-miss cost, not kernel CPU)");
    }

    internal static void Run()
    {
        Verify();
        VerifyStableRefreshAllocations();
        Benchmark();
        BenchmarkChangingRows();
        GC.KeepAlive(sink);
    }
}
