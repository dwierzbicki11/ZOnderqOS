using System;
using System.Diagnostics;
using Cosmos.Kernel.System.Diagnostics;
using ZonderqOS.SystemCore;

internal static class ThreadRowPrefixSmoke
{
    private static long sink;

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    // Previous production formatting, with the new state-label cache on
    // BOTH sides to isolate the stable numeric prefix optimization.
    private static string PreviousRow(KernelThreadInfo thread, int pid, int percent, string name)
    {
        return (pid == 0 ? "-" : pid.ToString()).PadLeft(5) + " " +
            thread.Id.ToString().PadLeft(6) + " " +
            thread.CpuId.ToString().PadLeft(4) + "  " +
            HtopMonitor.ThreadStateText(thread.State) + " " +
            HtopMonitor.PaddedCpuPercentText(percent) + "  " + name;
    }

    private static double Time(Func<long> action, int repeats)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++) sink ^= action();
        return (double)(Stopwatch.GetTimestamp() - start) * 1000d /
            Stopwatch.Frequency / repeats;
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }

    internal static void Run()
    {
        var monitor = new HtopMonitor();
        var random = new Random(3095);
        string[] names = { "worker", "system", "managed thread", "", "kernel thread" };
        int checks = 0;
        for (int i = 0; i < 4000; i++)
        {
            int slot = random.Next(0, 128);
            int pid = i % 9 == 0 ? 0 : random.Next(1, 5000);
            int percent = random.Next(0, 101);
            string name = names[i % names.Length];
            var thread = new KernelThreadInfo
            {
                Id = (uint)random.Next(1, 5000),
                CpuId = (uint)random.Next(0, 64),
                State = (KernelThreadState)random.Next(-3, 24)
            };
            string actual = monitor.GetThreadRowLine(slot, thread, pid, percent, name);
            Assert(actual == PreviousRow(thread, pid, percent, name),
                "row differs from legacy format i=" + i);
            checks++;
            Assert(object.ReferenceEquals(actual,
                monitor.GetThreadRowLine(slot, thread, pid, percent, name)),
                "unchanged row should retain same string i=" + i);
            checks++;
        }

        // Exercise the same slot with stable identity but changing CPU%,
        // followed by changes to each prefix key (PID, TID and CPU).
        var stable = new KernelThreadInfo
        {
            Id = 42, CpuId = 3, State = KernelThreadState.RUNNING
        };
        for (int percent = 0; percent <= 100; percent++)
        {
            string line = monitor.GetThreadRowLine(0, stable, 17, percent, "worker");
            Assert(line == PreviousRow(stable, 17, percent, "worker"),
                "CPU% update changed row formatting " + percent);
            checks++;
        }
        stable.CpuId = 4;
        Assert(monitor.GetThreadRowLine(0, stable, 17, 77, "worker") ==
            PreviousRow(stable, 17, 77, "worker"), "CPU change retained stale prefix");
        checks++;
        stable.Id = 99;
        Assert(monitor.GetThreadRowLine(0, stable, 17, 77, "worker") ==
            PreviousRow(stable, 17, 77, "worker"), "TID change retained stale prefix");
        checks++;
        Assert(monitor.GetThreadRowLine(0, stable, 0, 77, "worker") ==
            PreviousRow(stable, 0, 77, "worker"), "PID change retained stale prefix");
        checks++;
        Console.WriteLine("PASS: " + checks +
            " exact htop numeric-prefix formatting and cache invalidation checks");

        // Each row changes CPU% (17, 83, 17, ...), so the complete-row cache
        // cannot hide work. Only the new stable prefix can be reused.
        var sample = new KernelThreadInfo
        {
            Id = 1234, CpuId = 7, State = KernelThreadState.READY
        };
        var optimized = new HtopMonitor();
        Func<long> old = () =>
        {
            long checksum = 0;
            for (int i = 0; i < 1024; i++)
            {
                string line = PreviousRow(sample, 321, (i & 1) == 0 ? 17 : 83, "worker");
                checksum += line.Length + line[0] + line[line.Length - 1];
            }
            return checksum;
        };
        Func<long> current = () =>
        {
            long checksum = 0;
            for (int i = 0; i < 1024; i++)
            {
                string line = optimized.GetThreadRowLine(0, sample, 321,
                    (i & 1) == 0 ? 17 : 83, "worker");
                checksum += line.Length + line[0] + line[line.Length - 1];
            }
            return checksum;
        };
        Assert(old() == current(), "changing CPU% checksum differs");
        for (int i = 0; i < 15; i++) { sink ^= old(); sink ^= current(); }
        long start = GC.GetAllocatedBytesForCurrentThread();
        sink ^= old();
        long oldBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        sink ^= current();
        long newBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert(oldBytes > newBytes, "prefix reuse did not reduce allocations: " +
            oldBytes + " vs " + newBytes);
        double[] oldMs = new double[7], newMs = new double[7];
        for (int i = 0; i < 7; i++)
        {
            if ((i & 1) == 0)
            {
                oldMs[i] = Time(old, 100);
                newMs[i] = Time(current, 100);
            }
            else
            {
                newMs[i] = Time(current, 100);
                oldMs[i] = Time(old, 100);
            }
        }
        double a = Median(oldMs), b = Median(newMs);
        Console.WriteLine("HOST A/B htop 1024 changing CPU% rows: old=" +
            a.ToString("F5") + " ms cached-prefix=" + b.ToString("F5") +
            " ms ratio=" + (a / b).ToString("F2") + "x allocations=" +
            oldBytes + " B -> " + newBytes +
            " B (7 alternating samples; not guest FPS or CPU idle)");
        GC.KeepAlive(sink);
    }
}
