using System;
using System.Diagnostics;
using ZonderqOS.SystemCore;

internal static class MemoryLineSmoke
{
    private static long sink;

    // Exact former Refresh formatting, including its FreePages clamp and
    // integer MiB rounding. The A/B benchmark uses this independent baseline.
    private static string Legacy(ulong total, ulong free, ulong pageSize)
    {
        free = Math.Min(total, free);
        return "Mem: " + ((total - free) * pageSize / 1048576UL) +
            " / " + (total * pageSize / 1048576UL) + " MiB";
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Verify()
    {
        var monitor = new HtopMonitor();
        string first = monitor.GetMemoryLine(2048, 1024, 4096);
        Assert(first == Legacy(2048, 1024, 4096), "initial memory output");
        Assert(object.ReferenceEquals(first, monitor.GetMemoryLine(2048, 1024, 4096)),
            "unchanged physical pages must reuse a string");

        string freeChanged = monitor.GetMemoryLine(2048, 1023, 4096);
        Assert(!object.ReferenceEquals(first, freeChanged), "free pages must invalidate");
        string totalChanged = monitor.GetMemoryLine(2049, 1023, 4096);
        Assert(!object.ReferenceEquals(freeChanged, totalChanged), "total pages must invalidate");
        string sizeChanged = monitor.GetMemoryLine(2049, 1023, 8192);
        Assert(!object.ReferenceEquals(totalChanged, sizeChanged), "page size must invalidate");

        // The displayed MiB may be identical for different physical-page
        // snapshots. Reusing an old snapshot in that case would be incorrect.
        string roundedA = monitor.GetMemoryLine(2048, 1024, 4096);
        string roundedB = monitor.GetMemoryLine(2048, 1023, 4096);
        Assert(roundedA == roundedB, "expected identical rounded MiB output");
        Assert(!object.ReferenceEquals(roundedA, roundedB),
            "new physical-page snapshot must invalidate even when MiB is unchanged");

        var random = new Random(3095);
        for (int i = 0; i < 20000; i++)
        {
            ulong total = (ulong)random.Next(0, 100000);
            ulong free = (ulong)random.Next(0, 120000);
            ulong size = (ulong)(1 << random.Next(0, 15));
            string expected = Legacy(total, free, size);
            string actual = monitor.GetMemoryLine(total, free, size);
            Assert(actual == expected, "physical-memory formatting case " + i);
            Assert(object.ReferenceEquals(actual, monitor.GetMemoryLine(total, free, size)),
                "unchanged memory telemetry case " + i);
        }

        // Verify actual production Refresh, not just the extracted helper.
        monitor.Refresh();
        string refreshLine = monitor.Lines[5]; // 1 CPU: header, help, CPU info, CPU0, total, Mem
        Assert(refreshLine == Legacy(2048, 1024, 4096), "production Refresh memory output");
        monitor.Refresh();
        Assert(object.ReferenceEquals(refreshLine, monitor.Lines[5]),
            "production Refresh reallocated unchanged physical-memory telemetry");
        Console.WriteLine("PASS: 40000+ htop memory snapshot/output/cache checks and Refresh integration");
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }

    private static double Time(Func<long> action)
    {
        const int repeats = 200;
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++) sink ^= action();
        return (Stopwatch.GetTimestamp() - start) * 1000d /
            Stopwatch.Frequency / repeats;
    }

    private static void Benchmark()
    {
        var monitor = new HtopMonitor();
        ulong total = 32768, free = 16000, pageSize = 4096;
        Func<long> before = () =>
        {
            long sum = 0;
            for (int i = 0; i < 1024; i++)
            {
                string value = Legacy(total, free, pageSize);
                sum += value.Length + value[0] + value[value.Length - 1];
            }
            return sum;
        };
        Func<long> after = () =>
        {
            long sum = 0;
            for (int i = 0; i < 1024; i++)
            {
                string value = monitor.GetMemoryLine(total, free, pageSize);
                sum += value.Length + value[0] + value[value.Length - 1];
            }
            return sum;
        };
        Assert(before() == after(), "memory baseline/cache checksum mismatch");
        for (int i = 0; i < 16; i++) { sink ^= before(); sink ^= after(); }

        long start = GC.GetAllocatedBytesForCurrentThread();
        sink ^= before();
        long oldBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        sink ^= after();
        long newBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert(oldBytes > 0 && newBytes == 0,
            "memory cache allocations " + oldBytes + " -> " + newBytes);

        double[] oldMs = new double[7], newMs = new double[7];
        for (int i = 0; i < 7; i++)
        {
            if ((i & 1) == 0) { oldMs[i] = Time(before); newMs[i] = Time(after); }
            else { newMs[i] = Time(after); oldMs[i] = Time(before); }
        }
        double a = Median(oldMs), b = Median(newMs);
        Console.WriteLine("HOST A/B htop 1024 stable physical-memory lines: old=" +
            a.ToString("F5") + " ms cached=" + b.ToString("F5") +
            " ms ratio=" + (a / b).ToString("F2") + "x allocations=" +
            oldBytes + " B -> " + newBytes +
            " B (7 alternating samples; not kernel RAM/GC or CPU)");
    }

    internal static void Run()
    {
        Verify();
        Benchmark();
        GC.KeepAlive(sink);
    }
}
