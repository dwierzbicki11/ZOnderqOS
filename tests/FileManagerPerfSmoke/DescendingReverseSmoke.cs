using System;
using System.Collections.Generic;
using System.Diagnostics;
using ZonderqOS.GUI.Apps;

// Exact host A/B of the descending fast path in production FileEntrySorter:
// test-only flag chooses the previous indexer loop or List<T>.Reverse.
internal static class DescendingReverseSmoke
{
    private static long sink;
    private static int checks;

    private static FileEntry[] Descending(int count, int duplicateGroup)
    {
        var result = new FileEntry[count];
        for (int i = 0; i < count; i++)
        {
            string name = "file" + (i / duplicateGroup).ToString("D6");
            if (i % 3 == 1) name = name.ToUpperInvariant();
            result[i] = new FileEntry(name, false);
        }
        Array.Reverse(result);
        return result;
    }

    private static int Compare(FileEntry a, FileEntry b)
    {
        if (a.IsDirectory != b.IsDirectory)
            return a.IsDirectory ? -1 : 1;
        return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static void Verify()
    {
        foreach (int n in new[] { 33, 64, 127, 512, 2048 })
        {
            foreach (int group in new[] { 1, 3, 7 })
            {
                var input = Descending(n, group);
                var expected = new List<FileEntry>(input);
                for (int i = 1; i < expected.Count; i++)
                {
                    FileEntry item = expected[i];
                    int j = i - 1;
                    while (j >= 0 && Compare(expected[j], item) > 0)
                    {
                        expected[j + 1] = expected[j];
                        j--;
                    }
                    expected[j + 1] = item;
                }

                foreach (bool legacy in new[] { true, false })
                {
                    FileEntrySorter.DisableArrayReverseForBenchmark = legacy;
                    var actual = new List<FileEntry>(input);
                    FileEntry[] scratch = null;
                    FileEntrySorter.Sort(actual, ref scratch);
                    if (scratch != null)
                        throw new Exception("descending allocated scratch n=" + n);
                    for (int i = 0; i < n; i++)
                    {
                        if (!ReferenceEquals(actual[i], expected[i]))
                            throw new Exception("descending stable mismatch n=" + n +
                                " group=" + group + " legacy=" + legacy + " index=" + i);
                        checks++;
                    }
                }
            }
        }
        FileEntrySorter.DisableArrayReverseForBenchmark = false;
        Console.WriteLine("PASS: " + checks +
            " descending reference checks, legacy indexer vs List.Reverse");
    }

    private static double Time(bool optimized, FileEntry[] input,
        List<FileEntry> work, ref FileEntry[] scratch, int repeats)
    {
        FileEntrySorter.DisableArrayReverseForBenchmark = !optimized;
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++)
        {
            work.Clear();
            work.AddRange(input);
            FileEntrySorter.Sort(work, ref scratch);
            sink += work[0].Name.Length;
        }
        return (Stopwatch.GetTimestamp() - start) * 1000d /
            Stopwatch.Frequency / repeats;
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }

    private static void Benchmark(int group)
    {
        const int count = 2048;
        FileEntry[] input = Descending(count, group);
        var work = new List<FileEntry>(count);
        FileEntry[] oldScratch = null, newScratch = null;
        try
        {
            for (int i = 0; i < 12; i++)
            {
                Time(false, input, work, ref oldScratch, 8);
                Time(true, input, work, ref newScratch, 8);
            }
            long start = GC.GetAllocatedBytesForCurrentThread();
            Time(false, input, work, ref oldScratch, 64);
            long oldBytes = GC.GetAllocatedBytesForCurrentThread() - start;
            start = GC.GetAllocatedBytesForCurrentThread();
            Time(true, input, work, ref newScratch, 64);
            long newBytes = GC.GetAllocatedBytesForCurrentThread() - start;
            if (oldBytes != 0 || newBytes != 0 || oldScratch != null || newScratch != null)
                throw new Exception("descending allocation/scratch regression");

            double[] oldMs = new double[7], newMs = new double[7];
            for (int i = 0; i < 7; i++)
            {
                if ((i & 1) == 0)
                {
                    oldMs[i] = Time(false, input, work, ref oldScratch, 32);
                    newMs[i] = Time(true, input, work, ref newScratch, 32);
                }
                else
                {
                    newMs[i] = Time(true, input, work, ref newScratch, 32);
                    oldMs[i] = Time(false, input, work, ref oldScratch, 32);
                }
            }
            double before = Median(oldMs), after = Median(newMs);
            Console.WriteLine("HOST A/B descending n=" + count +
                " duplicate-group=" + group + " legacy-indexer=" +
                before.ToString("F5") + "ms array-backed-Reverse=" +
                after.ToString("F5") + "ms ratio=" +
                (before / after).ToString("F2") + "x allocations=" +
                oldBytes + "B -> " + newBytes +
                "B (7 alternating samples, same production sort; no FAT32 I/O)");
        }
        finally
        {
            FileEntrySorter.DisableArrayReverseForBenchmark = false;
        }
    }

    internal static void Run()
    {
        try
        {
            Verify();
            Benchmark(1);
            Benchmark(3);
        }
        finally
        {
            FileEntrySorter.DisableArrayReverseForBenchmark = false;
        }
    }
}
