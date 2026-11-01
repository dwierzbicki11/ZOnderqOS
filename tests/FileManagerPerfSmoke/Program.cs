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
        Benchmark(32, true);
        Benchmark(128, false);
        Benchmark(512, false);
        Benchmark(2048, true);
        Console.WriteLine("PASS: production FileEntrySorter smoke, sink=" + sink);
    }
}
