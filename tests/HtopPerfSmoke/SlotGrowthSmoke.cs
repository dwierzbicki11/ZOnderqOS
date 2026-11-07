using System;
using System.Diagnostics;
using Cosmos.Kernel.System.Diagnostics;
using ZonderqOS.SystemCore;

// Tests the real HtopMonitor implementation, not a separate growth helper.
// The host-only switch runs the exact preceding Array.Resize(slot + 1)
// policy through the same GetThreadRowLine / Refresh entry points.
internal static class SlotGrowthSmoke
{
    private static long sink;

    private static void Assert(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    private static double Median(double[] samples)
    {
        Array.Sort(samples);
        return samples[samples.Length / 2];
    }

    private static long GrowDirect(bool oldPolicy)
    {
        HtopMonitor.DisableGeometricSlotGrowthForBenchmark = oldPolicy;
        var monitor = new HtopMonitor();
        var thread = new KernelThreadInfo
        {
            CpuId = 0, State = KernelThreadState.READY, IsManaged = true
        };
        long checksum = 0;
        for (int slot = 0; slot < 512; slot++)
        {
            thread.Id = (uint)(slot + 1000);
            string line = monitor.GetThreadRowLine(slot, thread, 17, 0, "worker");
            checksum += line.Length + line[0] + line[line.Length - 1];
        }
        Assert(monitor.CachedSlotCapacityForTests >= 512, "direct growth lost slots");
        if (oldPolicy)
            Assert(monitor.CachedSlotCapacityForTests == 512,
                "legacy slot growth must allocate exactly the requested size");
        else
            Assert(monitor.CachedSlotCapacityForTests == 512,
                "geometric slot growth must reach 512 without excessive capacity");
        return checksum;
    }

    private static KernelThreadInfo[][] BuildSnapshots()
    {
        var snapshots = new KernelThreadInfo[256][];
        for (int count = 1; count <= snapshots.Length; count++)
        {
            var threads = new KernelThreadInfo[count];
            if (count > 1)
                Array.Copy(snapshots[count - 2], threads, count - 1);
            threads[count - 1] = new KernelThreadInfo
            {
                Id = (uint)(1000 + count), CpuId = 0,
                State = KernelThreadState.READY, IsManaged = true
            };
            snapshots[count - 1] = threads;
        }
        return snapshots;
    }

    private static long GrowRefresh(bool oldPolicy, KernelThreadInfo[][] snapshots)
    {
        HtopMonitor.DisableGeometricSlotGrowthForBenchmark = oldPolicy;
        var monitor = new HtopMonitor();
        long checksum = 0;
        foreach (KernelThreadInfo[] snapshot in snapshots)
        {
            SchedulerInfo.Threads = snapshot;
            monitor.Refresh();
            checksum += monitor.Lines.Count;
        }
        Assert(monitor.CachedSlotCapacityForTests >= 256 &&
            monitor.PreviousSlotCapacityForTests >= 256,
            "Refresh growth lost scheduler slots");
        if (oldPolicy)
            Assert(monitor.CachedSlotCapacityForTests == 256 &&
                monitor.PreviousSlotCapacityForTests == 256,
                "legacy Refresh growth must resize to exact slot count");
        else
            Assert(monitor.CachedSlotCapacityForTests == 256 &&
                monitor.PreviousSlotCapacityForTests == 256,
                "geometric Refresh growth must stop at 256 slots");
        Assert(monitor.Lines.Count == 264, "256 rows and 8 fixed htop lines");
        return checksum;
    }

    private static void VerifyGrowthAndInvalidation(KernelThreadInfo[][] snapshots)
    {
        HtopMonitor.DisableGeometricSlotGrowthForBenchmark = false;
        var monitor = new HtopMonitor();
        var thread = new KernelThreadInfo
        {
            Id = 11, CpuId = 0, State = KernelThreadState.READY
        };
        string first = monitor.GetThreadRowLine(0, thread, 7, 0, "shell");
        for (int slot = 1; slot < 512; slot++)
        {
            thread.Id = (uint)(slot + 11);
            monitor.GetThreadRowLine(slot, thread, 7, 0, "shell");
        }
        thread.Id = 11;
        Assert(object.ReferenceEquals(first,
            monitor.GetThreadRowLine(0, thread, 7, 0, "shell")),
            "cache growth invalidated an unchanged earlier row");
        Assert(monitor.CachedSlotCapacityForTests == 512,
            "direct slot cache should grow in powers of two");

        var refresh = new HtopMonitor();
        foreach (KernelThreadInfo[] snapshot in snapshots)
        {
            SchedulerInfo.Threads = snapshot;
            refresh.Refresh();
        }
        string last = refresh.Lines[refresh.Lines.Count - 1];
        SchedulerInfo.Threads = snapshots[0];
        refresh.Refresh();
        Assert(refresh.Lines.Count == 9, "shrunk scheduler leaked old rows");
        SchedulerInfo.Threads = snapshots[255];
        refresh.Refresh();
        Assert(refresh.Lines.Count == 264, "regrown scheduler lost rows");
        Assert(!object.ReferenceEquals(last, refresh.Lines[refresh.Lines.Count - 1]),
            "slot removed by shrink retained a stale cached line");
        // Reused slot with the same TID must not compare runtime against a
        // sample taken before the slot disappeared. The old stale sample
        // would produce a bogus 100% CPU row after a large runtime jump.
        var t0 = new KernelThreadInfo
        {
            Id = 10, CpuId = 0, State = KernelThreadState.READY,
            IsManaged = true, TotalRuntimeNs = 1000
        };
        var t1 = new KernelThreadInfo
        {
            Id = 20, CpuId = 0, State = KernelThreadState.READY,
            IsManaged = true, TotalRuntimeNs = 1000
        };
        var reuse = new HtopMonitor();
        SchedulerInfo.Threads = new[] { t0, t1 };
        reuse.Refresh();
        SchedulerInfo.Threads = new[] { t0 };
        reuse.Refresh();
        t1.TotalRuntimeNs = 1000000000000UL;
        SchedulerInfo.Threads = new[] { t0, t1 };
        reuse.Refresh();
        Assert(reuse.Lines.Count == 10 &&
            reuse.Lines[8].Contains("   0%  ") &&
            reuse.Lines[9].Contains("   0%  "),
            "reused scheduler slot inherited a stale runtime delta");
        Console.WriteLine("PASS: geometric direct/Refresh growth, cache shrink and runtime-sample invalidation");
    }

    private static (double timeMs, long bytes, long checksum) Measure(
        Func<long> action)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        long checksum = action();
        double ms = (Stopwatch.GetTimestamp() - start) * 1000d /
            Stopwatch.Frequency;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        return (ms, bytes, checksum);
    }

    private static void Benchmark(string name, Func<bool, long> run)
    {
        // Warm JIT and both production branches before the measured samples.
        for (int i = 0; i < 4; i++)
        {
            sink ^= run(true);
            sink ^= run(false);
        }
        var before = new double[7];
        var after = new double[7];
        long oldBytes = 0, newBytes = 0;
        for (int i = 0; i < 7; i++)
        {
            (double timeMs, long bytes, long checksum) a, b;
            if ((i & 1) == 0)
            {
                a = Measure(() => run(true));
                b = Measure(() => run(false));
            }
            else
            {
                b = Measure(() => run(false));
                a = Measure(() => run(true));
            }
            Assert(a.checksum == b.checksum,
                name + " changed visible row checksums");
            before[i] = a.timeMs;
            after[i] = b.timeMs;
            oldBytes = a.bytes;
            newBytes = b.bytes;
            sink ^= a.checksum ^ b.checksum;
        }
        Assert(newBytes < oldBytes, name + " did not reduce cold growth allocations: " +
            oldBytes + " -> " + newBytes);
        double oldMs = Median(before), newMs = Median(after);
        Console.WriteLine("HOST A/B " + name + ": exact-resize=" +
            oldMs.ToString("F4") + " ms geometric=" +
            newMs.ToString("F4") + " ms ratio=" +
            (oldMs / newMs).ToString("F2") + "x; allocations=" +
            oldBytes + " -> " + newBytes +
            " B (7 samples; host .NET 10 Release, not kernel GC/CPU)");
    }

    internal static void Run()
    {
        KernelThreadInfo[] previousThreads = SchedulerInfo.Threads;
        KernelProcess[] previousProcesses = ProcessManager.TestProcesses;
        try
        {
            var snapshots = BuildSnapshots();
            ProcessManager.TestProcesses = Array.Empty<KernelProcess>();
            VerifyGrowthAndInvalidation(snapshots);
            Benchmark("512 sequential GetThreadRowLine slots", GrowDirect);
            Benchmark("256 growing scheduler Refresh snapshots",
                oldPolicy => GrowRefresh(oldPolicy, snapshots));
        }
        finally
        {
            HtopMonitor.DisableGeometricSlotGrowthForBenchmark = false;
            SchedulerInfo.Threads = previousThreads;
            ProcessManager.TestProcesses = previousProcesses;
        }
        GC.KeepAlive(sink);
    }
}
