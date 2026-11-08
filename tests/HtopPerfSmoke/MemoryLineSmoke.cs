using System;
using System.Diagnostics;
using Cosmos.Kernel.System.Diagnostics;
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

        // Sub-MiB allocator activity changes raw counters, but not the
        // visible gauge. The same immutable string must be reused.
        string freeChanged = monitor.GetMemoryLine(2048, 1023, 4096);
        Assert(freeChanged == Legacy(2048, 1023, 4096), "sub-MiB free change output");
        Assert(object.ReferenceEquals(first, freeChanged),
            "sub-MiB free-page change reallocated an identical label");
        string totalChanged = monitor.GetMemoryLine(2049, 1023, 4096);
        Assert(totalChanged == Legacy(2049, 1023, 4096), "sub-MiB total change output");
        Assert(object.ReferenceEquals(freeChanged, totalChanged),
            "sub-MiB total-page change reallocated an identical label");
        string sizeChanged = monitor.GetMemoryLine(2049, 1023, 8192);
        Assert(!object.ReferenceEquals(totalChanged, sizeChanged),
            "changed visible MiB must invalidate");

        string roundedA = monitor.GetMemoryLine(2048, 1024, 4096);
        string roundedB = monitor.GetMemoryLine(2048, 1023, 4096);
        Assert(object.ReferenceEquals(roundedA, roundedB),
            "same rounded MiB must reuse the existing label");
        string crossedMiB = monitor.GetMemoryLine(2048, 768, 4096);
        Assert(crossedMiB == Legacy(2048, 768, 4096), "MiB boundary output");
        Assert(!object.ReferenceEquals(roundedB, crossedMiB),
            "crossing a displayed MiB boundary must invalidate");
        string equivalentPageSize = monitor.GetMemoryLine(4096, 2048, 2048);
        Assert(equivalentPageSize == roundedA, "equivalent page-size output");
        Assert(!object.ReferenceEquals(crossedMiB, equivalentPageSize),
            "returning to earlier visible values must invalidate once");
        Assert(object.ReferenceEquals(equivalentPageSize,
            monitor.GetMemoryLine(2048, 1024, 4096)),
            "equivalent visible MiB from different page sizes must reuse label");
        string clamped = monitor.GetMemoryLine(2048, 999999, 4096);
        Assert(clamped == Legacy(2048, 999999, 4096), "clamped free pages");
        Assert(!object.ReferenceEquals(equivalentPageSize, clamped),
            "clamp changes displayed used MiB");

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
        try
        {
            MemoryInfo.FreePages = 1023;
            monitor.Refresh();
            Assert(object.ReferenceEquals(refreshLine, monitor.Lines[5]),
                "production Refresh reallocated unchanged visible MiB after page fluctuation");
            MemoryInfo.FreePages = 768;
            monitor.Refresh();
            Assert(monitor.Lines[5] == Legacy(2048, 768, 4096),
                "production Refresh missed visible MiB boundary");
            Assert(!object.ReferenceEquals(refreshLine, monitor.Lines[5]),
                "production Refresh reused stale visible MiB across boundary");
            MemoryInfo.FreePages = 1024;
            monitor.Refresh();
            Assert(monitor.Lines[5] == refreshLine,
                "production Refresh did not recover original visible MiB");
        }
        finally
        {
            MemoryInfo.FreePages = 1024;
        }
        Console.WriteLine("PASS: 40000+ htop memory snapshot/output/cache checks and fluctuating Refresh integration");
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

    // Reproduces the former raw-snapshot cache, not the uncached formatter.
    // This isolates the benefit of caching visible MiB rather than raw pages.
    private sealed class RawSnapshotCache
    {
        private ulong totalPages = ulong.MaxValue;
        private ulong freePages = ulong.MaxValue;
        private ulong pageSize = ulong.MaxValue;
        private string line;

        internal string Get(ulong total, ulong free, ulong size)
        {
            free = Math.Min(total, free);
            if (line != null && total == totalPages &&
                free == freePages && size == pageSize)
                return line;
            totalPages = total;
            freePages = free;
            pageSize = size;
            return line = Legacy(total, free, size);
        }
    }

    private static void BenchmarkSubMiBFluctuations()
    {
        const ulong total = 32768, size = 4096;
        var previous = new RawSnapshotCache();
        var current = new HtopMonitor();
        Func<long> before = () =>
        {
            long checksum = 0;
            for (int i = 0; i < 1024; i++)
            {
                // 16000 and 15999 pages display the same 65 / 128 MiB.
                string value = previous.Get(total, 16000UL - (ulong)(i & 1), size);
                checksum += value.Length + value[0] + value[value.Length - 1];
            }
            return checksum;
        };
        Func<long> after = () =>
        {
            long checksum = 0;
            for (int i = 0; i < 1024; i++)
            {
                string value = current.GetMemoryLine(total, 16000UL - (ulong)(i & 1), size);
                checksum += value.Length + value[0] + value[value.Length - 1];
            }
            return checksum;
        };
        Assert(before() == after(), "fluctuating memory baseline/cache mismatch");
        for (int i = 0; i < 16; i++) { sink ^= before(); sink ^= after(); }
        long start = GC.GetAllocatedBytesForCurrentThread();
        sink ^= before();
        long oldBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        sink ^= after();
        long newBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert(oldBytes > 0 && newBytes == 0,
            "sub-MiB memory cache allocations " + oldBytes + " -> " + newBytes);

        double[] oldMs = new double[7], newMs = new double[7];
        for (int i = 0; i < 7; i++)
        {
            if ((i & 1) == 0) { oldMs[i] = Time(before); newMs[i] = Time(after); }
            else { newMs[i] = Time(after); oldMs[i] = Time(before); }
        }
        double a = Median(oldMs), b = Median(newMs);
        Console.WriteLine("HOST A/B htop 1024 sub-MiB page fluctuations: raw-snapshot=" +
            a.ToString("F5") + " ms visible-MiB=" + b.ToString("F5") +
            " ms ratio=" + (a / b).ToString("F2") + "x allocations=" +
            oldBytes + " B -> " + newBytes +
            " B (7 alternating samples; host only, not guest RAM/CPU)");
    }

    private static void BenchmarkStableRawSnapshot()
    {
        const ulong total = 32768, free = 16000, size = 4096;
        var previous = new RawSnapshotCache();
        var current = new HtopMonitor();
        Func<long> before = () =>
        {
            long checksum = 0;
            for (int i = 0; i < 1024; i++)
            {
                string value = previous.Get(total, free, size);
                checksum += value.Length + value[0] + value[value.Length - 1];
            }
            return checksum;
        };
        Func<long> after = () =>
        {
            long checksum = 0;
            for (int i = 0; i < 1024; i++)
            {
                string value = current.GetMemoryLine(total, free, size);
                checksum += value.Length + value[0] + value[value.Length - 1];
            }
            return checksum;
        };
        Assert(before() == after(), "stable raw-snapshot checksum mismatch");
        for (int i = 0; i < 16; i++) { sink ^= before(); sink ^= after(); }
        long start = GC.GetAllocatedBytesForCurrentThread();
        sink ^= before();
        long oldBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        sink ^= after();
        long newBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert(oldBytes == 0 && newBytes == 0,
            "stable raw-snapshot path allocated " + oldBytes + " -> " + newBytes);
        double[] oldMs = new double[7], newMs = new double[7];
        for (int i = 0; i < 7; i++)
        {
            if ((i & 1) == 0) { oldMs[i] = Time(before); newMs[i] = Time(after); }
            else { newMs[i] = Time(after); oldMs[i] = Time(before); }
        }
        double a = Median(oldMs), b = Median(newMs);
        Console.WriteLine("HOST A/B htop 1024 stable raw page snapshots: previous=" +
            a.ToString("F5") + " ms dual-cache=" + b.ToString("F5") +
            " ms ratio=" + (a / b).ToString("F2") + "x allocations=" +
            oldBytes + " B -> " + newBytes +
            " B (7 alternating samples; host only)");
    }

    internal static void Run()
    {
        Verify();
        Benchmark();
        BenchmarkSubMiBFluctuations();
        BenchmarkStableRawSnapshot();
        GC.KeepAlive(sink);
    }
}
