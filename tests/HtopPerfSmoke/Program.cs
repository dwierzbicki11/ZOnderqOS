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

    private static string LegacyCpuBar(int percent) =>
        new string('|', percent / 5).PadRight(20);

    private static void VerifyHeaderCache()
    {
        var monitor = new HtopMonitor();
        string one = monitor.GetHeaderLine(1, "stock");
        Assert(one == "ZOnderqOS htop | CPU: 1 | stock", "header output changed");
        Assert(object.ReferenceEquals(one, monitor.GetHeaderLine(1, "stock")),
            "unchanged header must be reused");
        Assert(object.ReferenceEquals(one, monitor.GetHeaderLine(1, new string("stock".ToCharArray()))),
            "equivalent scheduler names must not reallocate");
        string two = monitor.GetHeaderLine(8, "stock");
        Assert(two == "ZOnderqOS htop | CPU: 8 | stock", "CPU count invalidation failed");
        Assert(!object.ReferenceEquals(one, two), "CPU count change must rebuild header");
        string three = monitor.GetHeaderLine(8, "new-scheduler");
        Assert(three == "ZOnderqOS htop | CPU: 8 | new-scheduler",
            "scheduler name invalidation failed");
        Assert(!object.ReferenceEquals(two, three), "scheduler name change must rebuild header");
        Assert(monitor.GetHeaderLine(1, "stock") == one,
            "returning to previous topology must preserve output");
        // Exercise the production Refresh path, not only the helper.
        monitor.Refresh();
        string firstHeader = monitor.Lines[0];
        monitor.Refresh();
        Assert(object.ReferenceEquals(firstHeader, monitor.Lines[0]),
            "production Refresh allocated an unchanged header");
        Console.WriteLine("PASS: htop header cache, topology/name invalidation and exact text");
    }

    private static void BenchmarkHeaderCache()
    {
        var monitor = new HtopMonitor();
        int count = 8;
        string schedulerName = new string("stock".ToCharArray());
        Func<long> before = () =>
        {
            long checksum = 0;
            for (int i = 0; i < 1024; i++)
            {
                string value = "ZOnderqOS htop | CPU: " + count + " | " + schedulerName;
                checksum += value.Length + value[0] + value[value.Length - 1];
            }
            return checksum;
        };
        Func<long> after = () =>
        {
            long checksum = 0;
            for (int i = 0; i < 1024; i++)
            {
                string value = monitor.GetHeaderLine(count, schedulerName);
                checksum += value.Length + value[0] + value[value.Length - 1];
            }
            return checksum;
        };
        Assert(before() == after(), "cached header checksum mismatch");
        for (int i = 0; i < 15; i++) { sink ^= before(); sink ^= after(); }
        long start = GC.GetAllocatedBytesForCurrentThread();
        sink ^= before();
        long oldBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        sink ^= after();
        long newBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert(oldBytes > 0 && newBytes == 0,
            "header cache allocations old=" + oldBytes + " new=" + newBytes);
        double[] oldMs = new double[7], newMs = new double[7];
        for (int i = 0; i < oldMs.Length; i++)
        {
            if ((i & 1) == 0)
            {
                oldMs[i] = Time(before, 200);
                newMs[i] = Time(after, 200);
            }
            else
            {
                newMs[i] = Time(after, 200);
                oldMs[i] = Time(before, 200);
            }
        }
        double a = Median(oldMs), b = Median(newMs);
        Console.WriteLine("HOST A/B htop 1024 stable header formats: before=" +
            a.ToString("F5") + " ms after=" + b.ToString("F5") +
            " ms ratio=" + (a / b).ToString("F2") + "x allocations=" +
            oldBytes + " B -> " + newBytes + " B per 1024 headers");
    }

    private static void VerifyCpuBars()
    {
        for (int percent = 0; percent <= 100; percent++)
        {
            string expected = LegacyCpuBar(percent);
            string actual = HtopMonitor.CpuBar(percent);
            Assert(expected == actual, "CPU bar pixel columns " + percent);
            Assert(actual.Length == 20, "CPU bar width " + percent);
            Assert(object.ReferenceEquals(actual, HtopMonitor.CpuBar(percent)),
                "CPU bar must reuse cached string " + percent);
        }
        Console.WriteLine("PASS: 303 exact CPU bar output/width/reference checks (0..100%)");
    }

    private static string LegacyCpuUnavailableLine(int cpu) =>
        HtopMonitor.CpuPrefix(cpu) + HtopMonitor.CpuBar(0) + "] N/A";

    private static void VerifyCpuUnavailableLines()
    {
        for (int cpu = 0; cpu < 128; cpu++)
        {
            string expected = LegacyCpuUnavailableLine(cpu);
            string actual = HtopMonitor.CpuUnavailableLine(cpu);
            Assert(actual == expected, "stock N/A CPU row mismatch " + cpu);
            if (cpu < 64)
                Assert(object.ReferenceEquals(actual, HtopMonitor.CpuUnavailableLine(cpu)),
                    "stock N/A CPU row was not cached " + cpu);
        }
        var monitor = new HtopMonitor();
        monitor.Refresh();
        string first = monitor.Lines[3];
        Assert(object.ReferenceEquals(first, HtopMonitor.CpuUnavailableLine(0)),
            "stock CPU row did not use production cache");
        monitor.Refresh();
        Assert(object.ReferenceEquals(first, monitor.Lines[3]),
            "stock CPU row was reallocated between monitor refreshes");

        // Exercise the actual production Refresh loop with a 64-CPU host
        // scheduler snapshot. It must reuse every complete N/A row on
        // successive refreshes, not merely expose a cached helper method.
        SchedulerInfo.CpuCount = 64;
        try
        {
            monitor.Refresh();
            for (int cpu = 0; cpu < 64; cpu++)
                Assert(object.ReferenceEquals(monitor.Lines[3 + cpu],
                    HtopMonitor.CpuUnavailableLine(cpu)),
                    "64-CPU refresh did not reuse N/A line " + cpu);
            monitor.Refresh();
            for (int cpu = 0; cpu < 64; cpu++)
                Assert(object.ReferenceEquals(monitor.Lines[3 + cpu],
                    HtopMonitor.CpuUnavailableLine(cpu)),
                    "second 64-CPU refresh allocated N/A line " + cpu);
        }
        finally
        {
            SchedulerInfo.CpuCount = 1;
        }
        Console.WriteLine("PASS: 322 exact stock CPU N/A row/reference checks across refreshes");
    }

    private static void BenchmarkCpuUnavailableLines()
    {
        int[] indices = new int[1024];
        for (int i = 0; i < indices.Length; i++)
            indices[i] = (i * 13) % 16;
        Func<long> legacy = () =>
        {
            long checksum = 0;
            for (int i = 0; i < indices.Length; i++)
            {
                string line = LegacyCpuUnavailableLine(indices[i]);
                checksum += line.Length + line[0] + line[line.Length - 1];
            }
            return checksum;
        };
        Func<long> cached = () =>
        {
            long checksum = 0;
            for (int i = 0; i < indices.Length; i++)
            {
                string line = HtopMonitor.CpuUnavailableLine(indices[i]);
                checksum += line.Length + line[0] + line[line.Length - 1];
            }
            return checksum;
        };
        Assert(legacy() == cached(), "stock CPU N/A A/B checksum mismatch");
        for (int i = 0; i < 15; i++) { sink ^= legacy(); sink ^= cached(); }
        long start = GC.GetAllocatedBytesForCurrentThread();
        sink ^= legacy();
        long oldBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        sink ^= cached();
        long newBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert(oldBytes > 0, "legacy N/A CPU rows must allocate");
        Assert(newBytes == 0, "cached N/A CPU rows allocated " + newBytes + " B");
        double[] before = new double[7], after = new double[7];
        for (int sample = 0; sample < 7; sample++)
        {
            if ((sample & 1) == 0)
            {
                before[sample] = Time(legacy, 200);
                after[sample] = Time(cached, 200);
            }
            else
            {
                after[sample] = Time(cached, 200);
                before[sample] = Time(legacy, 200);
            }
        }
        double a = Median(before), b = Median(after);
        Console.WriteLine("HOST A/B stock CPU N/A 1024 rows: old=" + a.ToString("F4") +
            "ms cached=" + b.ToString("F4") + "ms ratio=" +
            (a / b).ToString("F2") + "x allocations=" + oldBytes +
            "B -> " + newBytes + "B per 1024 rows (7 alternating samples)");
    }


    // Compare against the exact preceding production behavior: CPU 0..15
    // used immutable lines, while 16..63 allocated on each refresh.
    // This isolates the cache extension from unrelated formatting changes.
    private static void BenchmarkExpandedCpuUnavailableCache()
    {
        var oldCache = new string[16];
        for (int i = 0; i < oldCache.Length; i++)
            oldCache[i] = HtopMonitor.CpuUnavailableLine(i);

        int[] indices = new int[1024];
        for (int i = 0; i < indices.Length; i++)
            indices[i] = 16 + (i * 13) % 48;

        Func<long> before = () =>
        {
            long checksum = 0;
            for (int i = 0; i < indices.Length; i++)
            {
                int cpu = indices[i];
                string line = cpu < oldCache.Length ? oldCache[cpu] :
                    LegacyCpuUnavailableLine(cpu);
                checksum += line.Length + line[0] + line[line.Length - 1];
            }
            return checksum;
        };
        Func<long> after = () =>
        {
            long checksum = 0;
            for (int i = 0; i < indices.Length; i++)
            {
                string line = HtopMonitor.CpuUnavailableLine(indices[i]);
                checksum += line.Length + line[0] + line[line.Length - 1];
            }
            return checksum;
        };
        Assert(before() == after(), "expanded stock N/A cache changed output");
        for (int i = 0; i < 15; i++) { sink ^= before(); sink ^= after(); }
        long start = GC.GetAllocatedBytesForCurrentThread();
        sink ^= before();
        long oldBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        sink ^= after();
        long newBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert(oldBytes > 0, "previous stock CPU 16..63 must allocate");
        Assert(newBytes == 0, "expanded CPU 16..63 cache allocated " + newBytes + " B");
        double[] oldMs = new double[7], newMs = new double[7];
        for (int sample = 0; sample < oldMs.Length; sample++)
        {
            if ((sample & 1) == 0)
            {
                oldMs[sample] = Time(before, 200);
                newMs[sample] = Time(after, 200);
            }
            else
            {
                newMs[sample] = Time(after, 200);
                oldMs[sample] = Time(before, 200);
            }
        }
        double a = Median(oldMs), b = Median(newMs);
        Console.WriteLine("HOST A/B stock CPU 16..63 N/A 1024 rows: old16=" +
            a.ToString("F4") + "ms cached64=" + b.ToString("F4") +
            "ms ratio=" + (a / b).ToString("F2") + "x allocations=" +
            oldBytes + "B -> " + newBytes +
            "B per 1024 rows (7 alternating samples; no kernel CPU measurement)");
    }

    private static string LegacyCpuPrefix(int cpu) =>
        "CPU " + cpu.ToString().PadLeft(3) + " [";

    private static void VerifyCpuPrefixes()
    {
        for (int cpu = 0; cpu <= 127; cpu++)
        {
            string expected = LegacyCpuPrefix(cpu);
            string actual = HtopMonitor.CpuPrefix(cpu);
            Assert(actual == expected, "CPU prefix exact text " + cpu);
            if (cpu < 64)
                Assert(object.ReferenceEquals(actual, HtopMonitor.CpuPrefix(cpu)),
                    "CPU prefix must reuse cached string " + cpu);
        }
        // Unusual CPU indices still follow the original formatting path.
        Assert(HtopMonitor.CpuPrefix(1000) == LegacyCpuPrefix(1000),
            "four-digit CPU index formatting");
        Console.WriteLine("PASS: 193 CPU prefix equivalence/reference checks (0..127 and 1000)");
    }

    private static void VerifyCpuPercentText()
    {
        for (int percent = 0; percent <= 100; percent++)
        {
            string expected = percent.ToString() + "%";
            string padded = percent.ToString().PadLeft(4) + "%";
            Assert(HtopMonitor.CpuPercentText(percent) == expected,
                "CPU percent text mismatch " + percent);
            Assert(HtopMonitor.PaddedCpuPercentText(percent) == padded,
                "padded CPU percent text mismatch " + percent);
            Assert(object.ReferenceEquals(HtopMonitor.CpuPercentText(percent),
                HtopMonitor.CpuPercentText(percent)), "CPU percent text cache " + percent);
            Assert(object.ReferenceEquals(HtopMonitor.PaddedCpuPercentText(percent),
                HtopMonitor.PaddedCpuPercentText(percent)), "padded CPU percent cache " + percent);
        }
        Console.WriteLine("PASS: 404 exact CPU percent text/padding/reference checks (0..100%)");
    }

    // Exercise the real production HtopMonitor row sorter, not an isolated
    // replacement implementation. Sorting must remain allocation-free once
    // the monitor and its row storage have been warmed.
    private static void VerifyProductionRowSortAllocations()
    {
        var threads = new KernelThreadInfo[128];
        for (int i = 0; i < threads.Length; i++)
            threads[i] = new KernelThreadInfo
            {
                Id = (uint)(2000 + i), CpuId = 0, TotalRuntimeNs = (ulong)(i * 37),
                State = KernelThreadState.READY, IsManaged = true
            };
        ProcessManager.TestProcesses = Array.Empty<KernelProcess>();
        SchedulerInfo.Threads = threads;
        var monitor = new HtopMonitor();
        monitor.Refresh();
        for (int i = 0; i < 20; i++)
        {
            monitor.SortByCpu = (i & 1) == 0;
            monitor.SortRows();
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1024; i++)
        {
            monitor.SortByCpu = (i & 1) == 0;
            monitor.SortRows();
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert(allocated == 0, "production htop row sort allocated " + allocated + " B");
        ProcessManager.TestProcesses = Array.Empty<KernelProcess>();
        SchedulerInfo.Threads = Array.Empty<KernelThreadInfo>();
        Console.WriteLine("PASS: production htop 1024 warmed row sorts, allocations=" +
            allocated + " B (host .NET; no QEMU)");
    }

    private sealed class DelegateSortProbe
    {
        internal int Compare(int a, int b) => a.CompareTo(b);
    }

    // Isolate the .NET instance method-group allocation removed by production
    // HtopMonitor. Same List.Sort and same comparator on both sides; the only
    // difference is constructing a delegate per call vs reusing it.
    private static void BenchmarkRowSortDelegate()
    {
        var values = new List<int>(128);
        for (int i = 0; i < 128; i++) values.Add(128 - i);
        var probe = new DelegateSortProbe();
        Comparison<int> cachedComparison = probe.Compare;
        Func<long> old = () =>
        {
            values.Sort(new Comparison<int>(probe.Compare));
            return values[0] + values[127];
        };
        Func<long> optimized = () =>
        {
            values.Sort(cachedComparison);
            return values[0] + values[127];
        };
        Assert(old() == optimized(), "row comparison delegate equivalence");
        for (int i = 0; i < 20; i++) { sink ^= old(); sink ^= optimized(); }
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1024; i++) sink ^= old();
        long oldBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1024; i++) sink ^= optimized();
        long newBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert(oldBytes > 0, "uncached instance delegate unexpectedly allocation-free");
        Assert(newBytes == 0, "cached instance delegate allocated " + newBytes + " B");
        double[] before = new double[7], after = new double[7];
        for (int sample = 0; sample < 7; sample++)
        {
            if ((sample & 1) == 0)
            {
                before[sample] = Time(old, 2048);
                after[sample] = Time(optimized, 2048);
            }
            else
            {
                after[sample] = Time(optimized, 2048);
                before[sample] = Time(old, 2048);
            }
        }
        double a = Median(before), b = Median(after);
        Console.WriteLine("HOST A/B List.Sort delegate 128 items: new-per-sort=" +
            a.ToString("F5") + " ms cached=" + b.ToString("F5") +
            " ms ratio=" + (a / b).ToString("F2") +
            "x, allocations per 1024 sorts=" + oldBytes + " B -> " +
            newBytes + " B (analogue of production delegate path; not kernel CPU)");
    }

    private static void BenchmarkCpuPercentText(bool padded)
    {
        int[] usage = new int[1024];
        for (int i = 0; i < usage.Length; i++)
            usage[i] = (i * 37 + i / 7) % 101;
        Func<long> legacy = () =>
        {
            long checksum = 0;
            for (int i = 0; i < usage.Length; i++)
            {
                string digits = usage[i].ToString();
                string value = (padded ? digits.PadLeft(4) : digits) + "%";
                checksum += value.Length + value[0] + value[value.Length - 1];
            }
            return checksum;
        };
        Func<long> cached = () =>
        {
            long checksum = 0;
            for (int i = 0; i < usage.Length; i++)
            {
                string value = padded ? HtopMonitor.PaddedCpuPercentText(usage[i])
                    : HtopMonitor.CpuPercentText(usage[i]);
                checksum += value.Length + value[0] + value[value.Length - 1];
            }
            return checksum;
        };
        Assert(legacy() == cached(), "CPU percent benchmark checksum mismatch");
        for (int i = 0; i < 15; i++) { sink ^= legacy(); sink ^= cached(); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        sink ^= legacy();
        long oldBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        sink ^= cached();
        long newBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert(oldBytes > 0, "legacy percent formatting must allocate");
        Assert(newBytes == 0, "cached percent formatting allocated " + newBytes + " B");
        double[] oldMs = new double[7], newMs = new double[7];
        for (int sample = 0; sample < 7; sample++)
        {
            if ((sample & 1) == 0)
            {
                oldMs[sample] = Time(legacy, 200);
                newMs[sample] = Time(cached, 200);
            }
            else
            {
                newMs[sample] = Time(cached, 200);
                oldMs[sample] = Time(legacy, 200);
            }
        }
        double a = Median(oldMs), b = Median(newMs);
        Console.WriteLine("HOST A/B CPU percent " + (padded ? "padded" : "plain") +
            " 1024 values: legacy=" + a.ToString("F4") +
            "ms cached=" + b.ToString("F4") + "ms ratio=" +
            (a / b).ToString("F2") + "x allocations=" + oldBytes +
            "B -> " + newBytes + "B per 1024 labels (7 alternating samples)");
    }

    private static void BenchmarkCpuPrefixes()
    {
        int[] indices = new int[1024];
        for (int i = 0; i < indices.Length; i++)
            indices[i] = (i * 13) % 64;
        Func<long> legacy = () =>
        {
            long checksum = 0;
            for (int i = 0; i < indices.Length; i++)
            {
                string label = LegacyCpuPrefix(indices[i]);
                checksum += label.Length + label[0] + label[label.Length - 1];
            }
            return checksum;
        };
        Func<long> cached = () =>
        {
            long checksum = 0;
            for (int i = 0; i < indices.Length; i++)
            {
                string label = HtopMonitor.CpuPrefix(indices[i]);
                checksum += label.Length + label[0] + label[label.Length - 1];
            }
            return checksum;
        };
        Assert(legacy() == cached(), "CPU prefix benchmark checksum mismatch");
        for (int i = 0; i < 15; i++) { sink ^= legacy(); sink ^= cached(); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        sink ^= legacy();
        long oldBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        sink ^= cached();
        long newBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert(oldBytes > 0, "legacy CPU prefixes must allocate");
        Assert(newBytes == 0, "cached CPU prefixes allocated " + newBytes + " B");
        double[] oldMs = new double[7], newMs = new double[7];
        for (int sample = 0; sample < 7; sample++)
        {
            if ((sample & 1) == 0)
            {
                oldMs[sample] = Time(legacy, 200);
                newMs[sample] = Time(cached, 200);
            }
            else
            {
                newMs[sample] = Time(cached, 200);
                oldMs[sample] = Time(legacy, 200);
            }
        }
        double a = Median(oldMs), b = Median(newMs);
        Console.WriteLine("HOST A/B CPU prefix 1024 values: legacy=" + a.ToString("F4") +
            "ms cached=" + b.ToString("F4") + "ms ratio=" +
            (a / b).ToString("F2") + "x allocations=" + oldBytes +
            "B -> " + newBytes + "B per 1024 prefixes (7 alternating samples)");
    }

    private static void BenchmarkCpuBars()
    {
        // The same 1024 deterministic percentages feed both paths. Measure
        // allocations separately from elapsed time; neither result is kernel CPU.
        int[] usage = new int[1024];
        for (int i = 0; i < usage.Length; i++)
            usage[i] = (i * 37 + i / 7) % 101;
        Func<long> legacy = () =>
        {
            long checksum = 0;
            for (int i = 0; i < usage.Length; i++)
            {
                string bar = LegacyCpuBar(usage[i]);
                checksum += bar.Length + bar[0] + bar[19];
            }
            return checksum;
        };
        Func<long> cached = () =>
        {
            long checksum = 0;
            for (int i = 0; i < usage.Length; i++)
            {
                string bar = HtopMonitor.CpuBar(usage[i]);
                checksum += bar.Length + bar[0] + bar[19];
            }
            return checksum;
        };
        Assert(legacy() == cached(), "CPU bar benchmark checksum mismatch");
        for (int i = 0; i < 15; i++) { sink ^= legacy(); sink ^= cached(); }
        long startBytes = GC.GetAllocatedBytesForCurrentThread();
        sink ^= legacy();
        long legacyBytes = GC.GetAllocatedBytesForCurrentThread() - startBytes;
        startBytes = GC.GetAllocatedBytesForCurrentThread();
        sink ^= cached();
        long cachedBytes = GC.GetAllocatedBytesForCurrentThread() - startBytes;
        Assert(legacyBytes > 0, "legacy CPU bars must allocate");
        Assert(cachedBytes == 0, "cached CPU bars allocated " + cachedBytes + " B");
        double[] before = new double[7], after = new double[7];
        for (int sample = 0; sample < before.Length; sample++)
        {
            if ((sample & 1) == 0)
            {
                before[sample] = Time(legacy, 200);
                after[sample] = Time(cached, 200);
            }
            else
            {
                after[sample] = Time(cached, 200);
                before[sample] = Time(legacy, 200);
            }
        }
        double a = Median(before), b = Median(after);
        Console.WriteLine("HOST A/B CPU bar 1024 values: legacy=" + a.ToString("F4") +
            "ms cached=" + b.ToString("F4") + "ms ratio=" +
            (a / b).ToString("F2") + "x allocations=" + legacyBytes +
            "B -> " + cachedBytes + "B per 1024 bars (7 alternating samples)");
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
            index.Rebuild(processes);
            Assert(HtopProcessIndex.ShouldIndex(n, n) == (n >= 512),
                "adaptive threshold n=" + n);
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
        index.Rebuild(Array.Empty<KernelProcess>());
        Assert(!index.TryResolve(0, out _), "stale TID after empty rebuild");
        Assert(!index.WasSeen(1), "stale PID after empty rebuild");
        var large = new KernelProcess[1024];
        for (int i = 0; i < large.Length; i++)
            large[i] = new KernelProcess(i + 1, "p", (uint)(i + 100));
        Assert(!HtopProcessIndex.ShouldIndex(large.Length, 8),
            "few scheduler threads should retain old scan");
        Assert(HtopProcessIndex.ShouldIndex(large.Length, 1024),
            "large join should use dictionary");
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
            new KernelThreadInfo { Id = 101, CpuId = 0, TotalRuntimeNs = 100, State = KernelThreadState.RUNNING, IsManaged = true },
            new KernelThreadInfo { Id = 102, CpuId = 0, TotalRuntimeNs = 100, State = KernelThreadState.READY },
            new KernelThreadInfo { Id = 103, CpuId = 0, TotalRuntimeNs = 100, State = KernelThreadState.READY },
            new KernelThreadInfo { Id = 104, CpuId = 0, TotalRuntimeNs = 100, State = KernelThreadState.IDLE, IsIdle = true }
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
        // Force the actual production monitor onto the indexed path.
        var large = new KernelProcess[512];
        var threads = new KernelThreadInfo[512];
        for (int i = 0; i < large.Length; i++)
        {
            large[i] = new KernelProcess(i + 100, "bulk-" + i, (uint)(1000 + i));
            threads[i] = new KernelThreadInfo
            {
                Id = (uint)(1000 + i), CpuId = 0, TotalRuntimeNs = 100,
                State = KernelThreadState.READY, IsManaged = true
            };
        }
        ProcessManager.TestProcesses = large;
        SchedulerInfo.Threads = threads;
        monitor.Refresh();
        output = string.Join("\n", monitor.Lines);
        Assert(output.Contains("bulk-0") && output.Contains("bulk-511"),
            "indexed monitor must resolve first and last process");
        Assert(!output.Contains("START/EXIT    -   bulk-"),
            "indexed monitor must not misclassify matched processes");
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
        index.Rebuild(processes);
        long checksum = 0;
        for (int t = 0; t < tids.Length; t++)
            if (index.TryResolve(tids[t], out KernelProcess process))
                checksum += process.PID;
        for (int p = 0; p < processes.Length; p++)
            if (!index.WasSeen(processes[p].PID))
                checksum += processes[p].PID;
        return checksum;
    }

    // Compare only dictionary construction: the previous two-probe
    // ContainsKey+Add path against production HtopProcessIndex.Rebuild.
    // Both maps and the process snapshot are reused between samples.
    private static void BenchmarkIndexBuild(int count, int unique)
    {
        var processes = new KernelProcess[count];
        for (int i = 0; i < count; i++)
            processes[i] = new KernelProcess(i + 1, "p", (uint)(10000 + i % unique));
        var legacy = new Dictionary<uint, KernelProcess>();
        var index = new HtopProcessIndex();
        Func<long> before = () =>
        {
            legacy.Clear();
            for (int i = 0; i < processes.Length; i++)
            {
                uint tid = processes[i].KernelThreadId;
                if (!legacy.ContainsKey(tid))
                    legacy.Add(tid, processes[i]);
            }
            return legacy.Count;
        };
        Func<long> after = () =>
        {
            index.Rebuild(processes);
            return unique;
        };

        Assert(before() == after(), "index build count mismatch");
        for (int i = 0; i < 12; i++) { before(); after(); }
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++) sink ^= after();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert(allocated == 0, "index build allocated after warmup: " + allocated);
        double[] oldSamples = new double[7];
        double[] newSamples = new double[7];
        int repetitions = count <= 512 ? 60 : count <= 2048 ? 25 : 8;
        for (int sample = 0; sample < 7; sample++)
        {
            if ((sample & 1) == 0)
            {
                oldSamples[sample] = Time(before, repetitions);
                newSamples[sample] = Time(after, repetitions);
            }
            else
            {
                newSamples[sample] = Time(after, repetitions);
                oldSamples[sample] = Time(before, repetitions);
            }
        }
        double oldMs = Median(oldSamples);
        double newMs = Median(newSamples);
        Console.WriteLine("Host .NET htop index build " + count + " processes / " + unique +
            " distinct TIDs: ContainsKey+Add=" + oldMs.ToString("F4") +
            "ms TryAdd=" + newMs.ToString("F4") + "ms ratio=" +
            (oldMs / newMs).ToString("F2") + "x warm-alloc=" + allocated +
            "B (7 alternating samples; not kernel CPU).");
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
        // Benchmark the selected production path, not a slow linear lookup
        // hidden behind an IReadOnlyList interface for small snapshots.
        Func<long> current = HtopProcessIndex.ShouldIndex(count, threads)
            ? () => IndexedJoin(index, processes, tids)
            : () => LegacyJoin(processes, tids, rowPids);
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
        VerifyCpuPercentText();
        BenchmarkCpuPercentText(false);
        BenchmarkCpuPercentText(true);
        VerifyHeaderCache();
        BenchmarkHeaderCache();
        MemoryLineSmoke.Run();
        StatusLineSmoke.Run();
        ThreadRowSmoke.Run();
        VerifyCpuBars();
        BenchmarkCpuBars();
        VerifyCpuPrefixes();
        BenchmarkCpuPrefixes();
        VerifyCpuUnavailableLines();
        BenchmarkCpuUnavailableLines();
        BenchmarkExpandedCpuUnavailableCache();
        VerifyIndex();
        VerifyMonitorIntegration();
        VerifyProductionRowSortAllocations();
        RowSortFastPathSmoke.Run();
        BenchmarkRowSortDelegate();
        Benchmark(16, 16);
        Benchmark(128, 128);
        Benchmark(512, 512);
        Benchmark(2048, 2048);
        Benchmark(1024, 8);
        Benchmark(64, 4096);
        BenchmarkIndexBuild(512, 512);
        BenchmarkIndexBuild(2048, 1024);
        BenchmarkIndexBuild(8192, 8192);
        GC.KeepAlive(sink);
        Console.WriteLine("PASS: host-only htop index equivalence, allocation and measured A/B; not kernel CPU/idle.");
        return 0;
    }
}
