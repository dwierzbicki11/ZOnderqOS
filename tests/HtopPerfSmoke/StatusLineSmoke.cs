using System;
using System.Diagnostics;
using Cosmos.Kernel.System.Diagnostics;
using ZonderqOS.SystemCore;

internal static class StatusLineSmoke
{
    private static long sink;

    private static void Assert(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    private static string Legacy(int processes, int threads) =>
        "Processes: " + processes + "  Threads: " + threads;

    private static void Verify()
    {
        var monitor = new HtopMonitor();
        string first = monitor.GetProcessCountLine(3, 8);
        Assert(first == Legacy(3, 8), "initial process/thread label");
        Assert(object.ReferenceEquals(first, monitor.GetProcessCountLine(3, 8)),
            "stable process/thread label must be reused");
        string second = monitor.GetProcessCountLine(4, 8);
        Assert(second == Legacy(4, 8) && !object.ReferenceEquals(first, second),
            "process count invalidation");
        string third = monitor.GetProcessCountLine(4, 9);
        Assert(third == Legacy(4, 9) && !object.ReferenceEquals(second, third),
            "thread count invalidation");
        Assert(monitor.GetProcessCountLine(0, 0) == Legacy(0, 0),
            "zero-count label");

        var random = new Random(3095);
        for (int i = 0; i < 20000; i++)
        {
            int processes = random.Next(0, 10000);
            int threads = random.Next(0, 10000);
            string actual = monitor.GetProcessCountLine(processes, threads);
            Assert(actual == Legacy(processes, threads), "label mismatch " + i);
            Assert(object.ReferenceEquals(actual,
                monitor.GetProcessCountLine(processes, threads)),
                "same-count label reallocated " + i);
        }

        KernelProcess[] originalProcesses = ProcessManager.TestProcesses;
        KernelThreadInfo[] originalThreads = SchedulerInfo.Threads;
        try
        {
            ProcessManager.TestProcesses = Array.Empty<KernelProcess>();
            SchedulerInfo.Threads = Array.Empty<KernelThreadInfo>();
            monitor.Refresh();
            string cpuTotal = monitor.Lines[4];
            string processLine = monitor.Lines[6];
            Assert(cpuTotal == "CPU total: N/A", "stock CPU total text");
            Assert(processLine == Legacy(0, 0), "empty snapshot text");
            monitor.Refresh();
            Assert(object.ReferenceEquals(cpuTotal, monitor.Lines[4]),
                "stock CPU total allocated again in Refresh");
            Assert(object.ReferenceEquals(processLine, monitor.Lines[6]),
                "unchanged process/thread label allocated in Refresh");

            ProcessManager.TestProcesses = new[]
            {
                new KernelProcess(1, "test-process", 123)
            };
            SchedulerInfo.Threads = new[]
            {
                new KernelThreadInfo { Id = 123, State = KernelThreadState.READY, CpuId = 0 }
            };
            monitor.Refresh();
            Assert(monitor.Lines[6] == Legacy(1, 1),
                "production Refresh missed process/thread update");
            string updated = monitor.Lines[6];
            monitor.Refresh();
            Assert(object.ReferenceEquals(updated, monitor.Lines[6]),
                "production Refresh did not reuse updated label");
            SchedulerInfo.Threads = Array.Empty<KernelThreadInfo>();
            monitor.Refresh();
            Assert(monitor.Lines[6] == Legacy(1, 0),
                "production Refresh missed thread exit");
            ProcessManager.TestProcesses = Array.Empty<KernelProcess>();
            monitor.Refresh();
            Assert(monitor.Lines[6] == Legacy(0, 0),
                "production Refresh missed process exit");
        }
        finally
        {
            ProcessManager.TestProcesses = originalProcesses;
            SchedulerInfo.Threads = originalThreads;
        }
        Console.WriteLine("PASS: htop 40000+ status label checks and production Refresh invalidation");
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }

    private static double Time(Func<long> action)
    {
        const int repetitions = 200;
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repetitions; i++) sink ^= action();
        return (Stopwatch.GetTimestamp() - start) * 1000d /
            Stopwatch.Frequency / repetitions;
    }

    private static void Benchmark()
    {
        var monitor = new HtopMonitor();
        int processes = 256, threads = 512;
        Func<long> before = () =>
        {
            long checksum = 0;
            for (int i = 0; i < 1024; i++)
            {
                string value = Legacy(processes, threads);
                checksum += value.Length + value[0] + value[value.Length - 1];
            }
            return checksum;
        };
        Func<long> after = () =>
        {
            long checksum = 0;
            for (int i = 0; i < 1024; i++)
            {
                string value = monitor.GetProcessCountLine(processes, threads);
                checksum += value.Length + value[0] + value[value.Length - 1];
            }
            return checksum;
        };
        Assert(before() == after(), "process label A/B checksum");
        for (int i = 0; i < 16; i++) { sink ^= before(); sink ^= after(); }
        long start = GC.GetAllocatedBytesForCurrentThread();
        sink ^= before();
        long oldBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        sink ^= after();
        long newBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert(oldBytes > 0 && newBytes == 0,
            "process label allocation mismatch " + oldBytes + " -> " + newBytes);
        double[] oldMs = new double[7], newMs = new double[7];
        for (int i = 0; i < 7; i++)
        {
            if ((i & 1) == 0) { oldMs[i] = Time(before); newMs[i] = Time(after); }
            else { newMs[i] = Time(after); oldMs[i] = Time(before); }
        }
        double a = Median(oldMs), b = Median(newMs);
        Console.WriteLine("HOST A/B htop 1024 stable process/thread labels: old=" +
            a.ToString("F5") + " ms cached=" + b.ToString("F5") +
            " ms ratio=" + (a / b).ToString("F2") + "x allocations=" +
            oldBytes + " B -> " + newBytes +
            " B (7 alternating samples; not kernel CPU/idle)");
    }

    internal static void Run()
    {
        Verify();
        Benchmark();
        GC.KeepAlive(sink);
    }
}
