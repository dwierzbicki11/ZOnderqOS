using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ZonderqOS.GUI.Apps
{
    // Only the data type is stubbed. Sorting code is compiled from production.
    public sealed class FileEntry
    {
        public readonly string Name;
        public readonly bool IsDirectory;
        public FileEntry(string name, bool isDirectory)
        {
            Name = name;
            IsDirectory = isDirectory;
        }
    }
}

internal static class Program
{
    private static int checks;
    private static long sink;

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    private static int Compare(ZonderqOS.GUI.Apps.FileEntry a, ZonderqOS.GUI.Apps.FileEntry b)
    {
        if (a.IsDirectory != b.IsDirectory)
            return a.IsDirectory ? -1 : 1;
        return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
    }

    // Exact previous production algorithm, including stable handling of ties.
    private static void LegacySort(List<ZonderqOS.GUI.Apps.FileEntry> entries)
    {
        for (int i = 1; i < entries.Count; i++)
        {
            var value = entries[i];
            int j = i - 1;
            while (j >= 0 && Compare(entries[j], value) > 0)
            {
                entries[j + 1] = entries[j];
                j--;
            }
            entries[j + 1] = value;
        }
    }

    private static ZonderqOS.GUI.Apps.FileEntry[] Make(int n, int seed, bool reversed)
    {
        var random = new Random(seed);
        var result = new ZonderqOS.GUI.Apps.FileEntry[n];
        for (int i = 0; i < n; i++)
        {
            int key = reversed ? n - i : random.Next(0, Math.Max(2, n / 3));
            string name = "file" + key.ToString("D6");
            if (i % 7 == 0) name = name.ToUpperInvariant();
            if (i % 11 == 0) name = "DUPLICATE";
            if (i % 13 == 0) name = "duplicate";
            result[i] = new ZonderqOS.GUI.Apps.FileEntry(name, i % 5 == 0);
        }
        return result;
    }

    private static void Verify()
    {
        int[] sizes = { 0, 1, 2, 3, 7, 16, 31, 32, 33, 34, 63, 64, 65, 127, 128, 255, 256, 512, 1024, 2048 };
        ZonderqOS.GUI.Apps.FileEntry[] scratch = null;
        foreach (int n in sizes)
        {
            int repeats = n >= 1024 ? 2 : 18;
            for (int trial = 0; trial < repeats; trial++)
            {
                var source = Make(n, 3095 + trial * 31 + n, trial % 3 == 0);
                var expected = new List<ZonderqOS.GUI.Apps.FileEntry>(source);
                var actual = new List<ZonderqOS.GUI.Apps.FileEntry>(source);
                LegacySort(expected);
                ZonderqOS.GUI.Apps.FileEntrySorter.Sort(actual, ref scratch);
                Assert(expected.Count == actual.Count, "count n=" + n);
                for (int i = 0; i < n; i++)
                    Assert(object.ReferenceEquals(expected[i], actual[i]), "stable order n=" + n + " trial=" + trial + " i=" + i);
                if (n > ZonderqOS.GUI.Apps.FileEntrySorter.InsertionLimit)
                    for (int i = 0; i < n; i++)
                        Assert(scratch[i] == null, "scratch must not retain old paths n=" + n);
            }
        }

        // Reuse a previously large buffer after navigation to tiny and empty folders.
        var large = new List<ZonderqOS.GUI.Apps.FileEntry>(Make(2048, 1, true));
        ZonderqOS.GUI.Apps.FileEntrySorter.Sort(large, ref scratch);
        var oldScratch = scratch;
        var tiny = new List<ZonderqOS.GUI.Apps.FileEntry>(Make(3, 2, true));
        ZonderqOS.GUI.Apps.FileEntrySorter.Sort(tiny, ref scratch);
        Assert(object.ReferenceEquals(oldScratch, scratch), "scratch reused across folders");
        Assert(scratch[2047] == null, "no retained entries from large folder");

        // A warmed sort should neither allocate nor grow the scratch buffer.
        var warmed = new List<ZonderqOS.GUI.Apps.FileEntry>(Make(512, 77, true));
        ZonderqOS.GUI.Apps.FileEntrySorter.Sort(warmed, ref scratch);
        for (int i = 0; i < warmed.Count / 2; i++)
        {
            var tmp = warmed[i];
            warmed[i] = warmed[warmed.Count - 1 - i];
            warmed[warmed.Count - 1 - i] = tmp;
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        ZonderqOS.GUI.Apps.FileEntrySorter.Sort(warmed, ref scratch);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert(bytes == 0, "warm sort allocated " + bytes + " bytes");
        Console.WriteLine("PASS: " + checks + " exact stable-order/reference checks; warm allocations=" + bytes + " B");
    }

    // Snapshot of the independent GUI branch implementation at 5fe8f293:
    // stable merge sort, but a new FileEntry[] for every directory refresh.
    // This is a comparison baseline, not production code for this branch.
    private static void GuiBranchSort(List<ZonderqOS.GUI.Apps.FileEntry> destination)
    {
        int count = destination.Count;
        if (count < 2) return;
        var scratch = new ZonderqOS.GUI.Apps.FileEntry[count];
        for (int run = 1; run < count; run = run > count / 2 ? count : run * 2)
        {
            int start = 0;
            while (start < count)
            {
                int middle = Math.Min(count, start + run);
                int end = Math.Min(count, middle + run);
                int left = start;
                int right = middle;
                for (int write = start; write < end; write++)
                {
                    if (left < middle && (right >= end ||
                        Compare(destination[left], destination[right]) <= 0))
                        scratch[write] = destination[left++];
                    else
                        scratch[write] = destination[right++];
                }
                start = end;
            }
            for (int i = 0; i < count; i++)
                destination[i] = scratch[i];
        }
    }

    private static void BenchmarkGuiBranch(int n)
    {
        var source = Make(n, 2026 + n, false);
        var work = new List<ZonderqOS.GUI.Apps.FileEntry>(n);
        ZonderqOS.GUI.Apps.FileEntry[] scratch = new ZonderqOS.GUI.Apps.FileEntry[n];
        for (int i = 0; i < 4; i++)
        {
            work.Clear(); work.AddRange(source); GuiBranchSort(work);
            work.Clear(); work.AddRange(source);
            ZonderqOS.GUI.Apps.FileEntrySorter.Sort(work, ref scratch);
        }
        double[] guiMs = new double[7], perfMs = new double[7];
        for (int i = 0; i < 7; i++)
        {
            bool perfFirst = (i & 1) != 0;
            for (int side = 0; side < 2; side++)
            {
                bool perf = side == 0 ? perfFirst : !perfFirst;
                long start = Stopwatch.GetTimestamp();
                for (int rep = 0; rep < 3; rep++)
                {
                    work.Clear();
                    work.AddRange(source);
                    if (perf)
                        ZonderqOS.GUI.Apps.FileEntrySorter.Sort(work, ref scratch);
                    else
                        GuiBranchSort(work);
                    sink += work[0].Name.Length;
                }
                double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 /
                    Stopwatch.Frequency / 3.0;
                if (perf) perfMs[i] = ms;
                else guiMs[i] = ms;
            }
        }
        // Verify exact same order for the independent GUI branch implementation.
        var expected = new List<ZonderqOS.GUI.Apps.FileEntry>(source);
        var actual = new List<ZonderqOS.GUI.Apps.FileEntry>(source);
        GuiBranchSort(expected);
        ZonderqOS.GUI.Apps.FileEntrySorter.Sort(actual, ref scratch);
        for (int i = 0; i < n; i++)
            Assert(object.ReferenceEquals(expected[i], actual[i]), "GUI branch equivalence " + i);

        double a = Median(guiMs), b = Median(perfMs);
        Console.WriteLine("HOST A/B GUI branch 5fe8f293 vs perf n=" + n +
            " gui=" + a.ToString("F4") + " ms perf=" + b.ToString("F4") +
            " ms ratio=" + (a / b).ToString("F2") + "x");
        long before = GC.GetAllocatedBytesForCurrentThread();
        GuiBranchSort(expected);
        long guiBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        ZonderqOS.GUI.Apps.FileEntrySorter.Sort(actual, ref scratch);
        long perfBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert(perfBytes == 0, "perf warm sort allocations");
        Assert(guiBytes >= n * IntPtr.Size, "GUI branch expected temporary array allocation");
        Console.WriteLine("HOST allocations GUI=" + guiBytes + " B perf=" + perfBytes + " B per warm sort");
    }

    private static void VerifyFilter()
    {
        var source = new List<ZonderqOS.GUI.Apps.FileEntry>(Make(2048, 41, false));
        ZonderqOS.GUI.Apps.FileEntry[] scratch = null;
        ZonderqOS.GUI.Apps.FileEntrySorter.Sort(source, ref scratch);
        var visible = new List<ZonderqOS.GUI.Apps.FileEntry>(source.Count);
        string[] filters = { "", "file", "FILE", "file000", "dup", "DUPLICATE", "not-found", "0000" };
        foreach (string filter in filters)
        {
            var expected = new List<ZonderqOS.GUI.Apps.FileEntry>(source);
            if (filter.Length != 0)
            {
                string lowered = filter.ToLowerInvariant();
                for (int i = expected.Count - 1; i >= 0; i--)
                    if (!expected[i].Name.ToLowerInvariant().Contains(lowered))
                        expected.RemoveAt(i);
            }

            ZonderqOS.GUI.Apps.FileEntrySorter.Filter(source, visible, filter);
            Assert(expected.Count == visible.Count, "filter count " + filter);
            for (int i = 0; i < expected.Count; i++)
                Assert(object.ReferenceEquals(expected[i], visible[i]), "filter order " + filter + " i=" + i);
        }

        // Subsequent search edits must reuse list storage and never touch the source.
        ZonderqOS.GUI.Apps.FileEntrySorter.Filter(source, visible, "");
        int capacity = visible.Capacity;
        long before = GC.GetAllocatedBytesForCurrentThread();
        ZonderqOS.GUI.Apps.FileEntrySorter.Filter(source, visible, "file");
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert(bytes == 0, "warm filter allocated " + bytes + " bytes");
        Assert(visible.Capacity == capacity, "filter grew warmed destination");
        Assert(source.Count == 2048, "filter mutated sorted snapshot");
        Console.WriteLine("PASS: production filter stable equivalence, 0 B warm allocations");
    }

    private static void BenchmarkFilter()
    {
        var source = new List<ZonderqOS.GUI.Apps.FileEntry>(Make(2048, 99, false));
        var work = new List<ZonderqOS.GUI.Apps.FileEntry>(2048);
        const string search = "file000";
        for (int i = 0; i < 10; i++)
            ZonderqOS.GUI.Apps.FileEntrySorter.Filter(source, work, search);
        double[] oldMs = new double[7];
        double[] newMs = new double[7];
        for (int i = 0; i < 7; i++)
        {
            // Same source and filter, median of 20 repeats; alternate order.
            bool newFirst = (i & 1) != 0;
            for (int side = 0; side < 2; side++)
            {
                bool optimized = side == 0 ? newFirst : !newFirst;
                long start = Stopwatch.GetTimestamp();
                for (int rep = 0; rep < 20; rep++)
                {
                    if (optimized)
                        ZonderqOS.GUI.Apps.FileEntrySorter.Filter(source, work, search);
                    else
                    {
                        work.Clear();
                        work.AddRange(source);
                        string lowered = search.ToLowerInvariant();
                        for (int j = work.Count - 1; j >= 0; j--)
                            if (!work[j].Name.ToLowerInvariant().Contains(lowered))
                                work.RemoveAt(j);
                    }
                    sink += work.Count;
                }
                double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 /
                    Stopwatch.Frequency / 20.0;
                if (optimized) newMs[i] = ms;
                else oldMs[i] = ms;
            }
        }
        double a = Median(oldMs), b = Median(newMs);
        Console.WriteLine("HOST A/B filter n=2048 search=" + search +
            " old=" + a.ToString("F4") + " ms new=" + b.ToString("F4") +
            " ms speedup=" + (a / b).ToString("F2") + "x");
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }

    private static double Timed(bool optimized, ZonderqOS.GUI.Apps.FileEntry[] source,
        List<ZonderqOS.GUI.Apps.FileEntry> work, ref ZonderqOS.GUI.Apps.FileEntry[] scratch)
    {
        const int iterations = 3;
        long start = Stopwatch.GetTimestamp();
        for (int rep = 0; rep < iterations; rep++)
        {
            work.Clear();
            work.AddRange(source);
            if (optimized)
                ZonderqOS.GUI.Apps.FileEntrySorter.Sort(work, ref scratch);
            else
                LegacySort(work);
            sink += work.Count > 0 ? work[0].Name.Length : 0;
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency / iterations;
    }

    private static void Benchmark(int n, bool reversed)
    {
        var source = Make(n, 881 + n, reversed);
        var work = new List<ZonderqOS.GUI.Apps.FileEntry>(n);
        ZonderqOS.GUI.Apps.FileEntry[] scratch = new ZonderqOS.GUI.Apps.FileEntry[n];
        for (int i = 0; i < 5; i++)
        {
            Timed(false, source, work, ref scratch);
            Timed(true, source, work, ref scratch);
        }
        double[] oldMs = new double[7];
        double[] newMs = new double[7];
        for (int i = 0; i < 7; i++)
        {
            if ((i & 1) == 0)
            {
                oldMs[i] = Timed(false, source, work, ref scratch);
                newMs[i] = Timed(true, source, work, ref scratch);
            }
            else
            {
                newMs[i] = Timed(true, source, work, ref scratch);
                oldMs[i] = Timed(false, source, work, ref scratch);
            }
        }
        double a = Median(oldMs), b = Median(newMs);
        Console.WriteLine("HOST A/B n=" + n + " reversed=" + reversed +
            " old=" + a.ToString("F4") + " ms new=" + b.ToString("F4") +
            " ms speedup=" + (a / b).ToString("F2") + "x");
    }

    private static void Main()
    {
        Verify();
        IconResolverSmoke.Run();
        VerifyFilter();
        BenchmarkFilter();
        Benchmark(32, true);
        Benchmark(128, false);
        Benchmark(512, false);
        Benchmark(2048, true);
        BenchmarkGuiBranch(512);
        BenchmarkGuiBranch(2048);
        Console.WriteLine("PASS: production FileEntrySorter smoke, sink=" + sink);
    }
}
