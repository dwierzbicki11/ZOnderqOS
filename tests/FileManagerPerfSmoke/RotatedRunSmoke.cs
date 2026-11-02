using System;
using System.Collections.Generic;
using System.Diagnostics;
using ZonderqOS.GUI.Apps;

// Production FileEntrySorter is compiled into this project unchanged apart
// from the test-only switch that disables the new rotation fast path.
internal static class RotatedRunSmoke
{
    private static int checks;
    private static long sink;

    private static void Require(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        checks++;
    }

    private static int Compare(FileEntry a, FileEntry b)
    {
        if (a.IsDirectory != b.IsDirectory)
            return a.IsDirectory ? -1 : 1;
        return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static FileEntry[] MakeRotation(int count, int split, int duplicateGroup)
    {
        var sorted = new FileEntry[count];
        for (int i = 0; i < count; i++)
        {
            string name = "file" + (i / duplicateGroup).ToString("D6");
            if (i % 3 == 1) name = name.ToUpperInvariant();
            sorted[i] = new FileEntry(name, false);
        }
        var rotated = new FileEntry[count];
        Array.Copy(sorted, split, rotated, 0, count - split);
        Array.Copy(sorted, 0, rotated, count - split, split);
        return rotated;
    }

    // Independent stable insertion baseline: compare object identities,
    // including case-insensitive equal-key entries spanning the split.
    private static void StableSort(List<FileEntry> entries)
    {
        for (int i = 1; i < entries.Count; i++)
        {
            FileEntry item = entries[i];
            int j = i - 1;
            while (j >= 0 && Compare(entries[j], item) > 0)
            {
                entries[j + 1] = entries[j];
                j--;
            }
            entries[j + 1] = item;
        }
    }

    private static void VerifyRotations()
    {
        foreach (int n in new[] { 33, 64, 127, 512, 2048 })
        {
            foreach (int split in new[] { 8, n / 2, n - 8 })
            {
                foreach (int group in new[] { 1, 3, 7 })
                {
                    FileEntry[] input = MakeRotation(n, split, group);
                    var expected = new List<FileEntry>(input);
                    var actual = new List<FileEntry>(input);
                    StableSort(expected);
                    FileEntry[] scratch = null;
                    FileEntrySorter.Sort(actual, ref scratch);
                    bool strict = Compare(input[input.Length - 1], input[0]) < 0;
                    Require(strict ? scratch == null : scratch != null,
                        "rotation scratch n=" + n + " split=" + split + " group=" + group);
                    for (int i = 0; i < n; i++)
                    {
                        Require(ReferenceEquals(expected[i], actual[i]),
                            "stable rotation n=" + n + " split=" + split +
                            " group=" + group + " index=" + i);
                        if (scratch != null)
                            Require(scratch[i] == null, "scratch retained entry index=" + i);
                    }
                }
            }
        }

        // Two individually sorted but interleaved runs must NOT be rotated.
        const int count = 256;
        var interleaved = new FileEntry[count];
        for (int i = 0; i < count / 2; i++)
        {
            interleaved[i] = new FileEntry("file" + (i * 2 + 2).ToString("D6"), false);
            interleaved[i + count / 2] = new FileEntry("file" + (i * 2 + 1).ToString("D6"), false);
        }
        var control = new List<FileEntry>(interleaved);
        var work = new List<FileEntry>(interleaved);
        StableSort(control);
        FileEntry[] mergeScratch = null;
        FileEntrySorter.Sort(work, ref mergeScratch);
        Require(mergeScratch != null, "interleaved runs must use stable merge");
        for (int i = 0; i < count; i++)
            Require(ReferenceEquals(work[i], control[i]), "interleaved order " + i);
        Console.WriteLine("PASS: " + checks +
            " strict/tied/interleaved two-run reference and scratch checks");
    }

    private static double Time(bool optimized, FileEntry[] source,
        List<FileEntry> work, ref FileEntry[] scratch, int repeats)
    {
        FileEntrySorter.DisableRotationForBenchmark = !optimized;
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++)
        {
            work.Clear();
            work.AddRange(source);
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

    private static void Benchmark()
    {
        const int count = 2048;
        FileEntry[] input = MakeRotation(count, count / 2, 1);
        var work = new List<FileEntry>(count);
        FileEntry[] oldScratch = new FileEntry[count];
        FileEntry[] newScratch = null;
        try
        {
            for (int i = 0; i < 12; i++)
            {
                Time(false, input, work, ref oldScratch, 8);
                Time(true, input, work, ref newScratch, 8);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            Time(false, input, work, ref oldScratch, 64);
            long oldBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            before = GC.GetAllocatedBytesForCurrentThread();
            Time(true, input, work, ref newScratch, 64);
            long newBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Require(oldBytes == 0 && newBytes == 0,
                "warmed two-run sort allocated: " + oldBytes + "/" + newBytes);
            Require(newScratch == null, "strict rotation allocated a merge buffer");

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
            double oldValue = Median(oldMs), newValue = Median(newMs);
            Console.WriteLine("HOST A/B strictly rotated directory n=" + count +
                " before-two-run-merge=" + oldValue.ToString("F5") +
                "ms after-in-place-rotation=" + newValue.ToString("F5") +
                "ms ratio=" + (oldValue / newValue).ToString("F2") +
                "x allocations=" + oldBytes + "B -> " + newBytes +
                "B (7 alternating samples, identical production sort and setup; no FAT32 I/O)");
        }
        finally
        {
            FileEntrySorter.DisableRotationForBenchmark = false;
        }
    }

    internal static void Run()
    {
        VerifyRotations();
        Benchmark();
    }
}
