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

    // Snapshot of the preceding performance-branch merge algorithm.
    // Reuses scratch exactly like production did before the ordered-input gate.
    // This is the A/B baseline, not the GUI branch's allocating sorter.
    private static void PreviousPerfSort(List<ZonderqOS.GUI.Apps.FileEntry> entries,
        ref ZonderqOS.GUI.Apps.FileEntry[] scratch)
    {
        int count = entries.Count;
        if (count < 2) return;
        if (scratch == null || scratch.Length < count)
            scratch = new ZonderqOS.GUI.Apps.FileEntry[count];
        bool sourceIsList = true;
        int run = 1;
        while (run < count)
        {
            int start = 0;
            while (start < count)
            {
                int middle = start + Math.Min(run, count - start);
                int end = middle + Math.Min(run, count - middle);
                int left = start, right = middle, output = start;
                while (left < middle && right < end)
                {
                    var a = sourceIsList ? entries[left] : scratch[left];
                    var b = sourceIsList ? entries[right] : scratch[right];
                    if (Compare(a, b) <= 0)
                    {
                        if (sourceIsList) scratch[output] = a;
                        else entries[output] = a;
                        left++;
                    }
                    else
                    {
                        if (sourceIsList) scratch[output] = b;
                        else entries[output] = b;
                        right++;
                    }
                    output++;
                }
                while (left < middle)
                {
                    var item = sourceIsList ? entries[left++] : scratch[left++];
                    if (sourceIsList) scratch[output++] = item;
                    else entries[output++] = item;
                }
                while (right < end)
                {
                    var item = sourceIsList ? entries[right++] : scratch[right++];
                    if (sourceIsList) scratch[output++] = item;
                    else entries[output++] = item;
                }
                start = end;
            }
            sourceIsList = !sourceIsList;
            if (run >= count - run) break;
            run *= 2;
        }
        if (!sourceIsList)
            for (int i = 0; i < count; i++) entries[i] = scratch[i];
        Array.Clear(scratch, 0, count);
    }

    private static void VerifyAlreadySortedFastPath()
    {
        var entries = new List<ZonderqOS.GUI.Apps.FileEntry>(Make(2048, 33095, false));
        LegacySort(entries);
        var references = entries.ToArray();
        ZonderqOS.GUI.Apps.FileEntry[] scratch = null;
        ZonderqOS.GUI.Apps.FileEntrySorter.Sort(entries, ref scratch);
        Assert(scratch == null, "already sorted directory must not allocate scratch");
        for (int i = 0; i < entries.Count; i++)
            Assert(object.ReferenceEquals(entries[i], references[i]),
                "ordered fast path changed stable reference at " + i);
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 64; i++)
            ZonderqOS.GUI.Apps.FileEntrySorter.Sort(entries, ref scratch);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert(bytes == 0, "ordered fast path allocated " + bytes + " B");
        Assert(scratch == null, "ordered fast path created a scratch array");
        Console.WriteLine("PASS: 2048 ordered entries preserve references, scratch=null, 0 B warm allocations");
    }

    // Reproduce directory refreshes that are almost sorted: a renamed entry
    // moving in either direction, two adjacent swaps, and a pathological
    // rotation of two sorted halves. The last case forces the shift budget
    // to fall back to merge sort instead of doing quadratic work.
    private static ZonderqOS.GUI.Apps.FileEntry[] MakeNearlySorted(int mode)
    {
        var entries = new List<ZonderqOS.GUI.Apps.FileEntry>(Make(2048, 309500 + mode, false));
        LegacySort(entries);
        if (mode == 0 || mode == 1)
        {
            int from = mode == 0 ? 250 : 1400;
            int to = mode == 0 ? 1400 : 250;
            var displaced = entries[from];
            entries.RemoveAt(from);
            entries.Insert(to, displaced);
        }
        else if (mode == 2)
        {
            var first = entries[512];
            entries[512] = entries[513];
            entries[513] = first;
            first = entries[1500];
            entries[1500] = entries[1501];
            entries[1501] = first;
        }
        else if (mode == 3)
        {
            var rotated = new List<ZonderqOS.GUI.Apps.FileEntry>(2048);
            for (int i = 1024; i < entries.Count; i++)
                rotated.Add(entries[i]);
            for (int i = 0; i < 1024; i++)
                rotated.Add(entries[i]);
            entries = rotated;
        }
        else if (mode == 4 || mode == 5)
        {
            // FAT32 enumeration can append a newly created file whose name
            // belongs at the opposite end of the sorted view.
            int from = mode == 4 ? 0 : entries.Count - 1;
            int to = mode == 4 ? entries.Count - 1 : 0;
            var displaced = entries[from];
            entries.RemoveAt(from);
            entries.Insert(to, displaced);
        }
        return entries.ToArray();
    }

    private static ZonderqOS.GUI.Apps.FileEntry[] MakeDescending(int n, int duplicateGroup)
    {
        var entries = new List<ZonderqOS.GUI.Apps.FileEntry>(n);
        for (int i = 0; i < n; i++)
        {
            string name = "file" + (i / duplicateGroup).ToString("D6");
            if (i % 3 == 1)
                name = name.ToUpperInvariant();
            entries.Add(new ZonderqOS.GUI.Apps.FileEntry(name, i < n / 4));
        }
        LegacySort(entries);
        entries.Reverse();
        return entries.ToArray();
    }

    private static void VerifyDescendingFastPath()
    {
        foreach (int n in new[] { 33, 64, 127, 512, 2048 })
        {
            foreach (int group in new[] { 1, 3 })
            {
                var source = MakeDescending(n, group);
                var expected = new List<ZonderqOS.GUI.Apps.FileEntry>(source);
                var actual = new List<ZonderqOS.GUI.Apps.FileEntry>(source);
                LegacySort(expected);
                ZonderqOS.GUI.Apps.FileEntry[] scratch = null;
                ZonderqOS.GUI.Apps.FileEntrySorter.Sort(actual, ref scratch);
                Assert(scratch == null,
                    "descending directory unexpectedly allocated scratch n=" + n + " group=" + group);
                for (int i = 0; i < n; i++)
                    Assert(object.ReferenceEquals(expected[i], actual[i]),
                        "descending stable reference n=" + n + " group=" + group + " i=" + i);
            }
        }

        var input = MakeDescending(2048, 3);
        var work = new List<ZonderqOS.GUI.Apps.FileEntry>(input.Length);
        ZonderqOS.GUI.Apps.FileEntry[] scratchBuffer = null;
        for (int i = 0; i < 8; i++)
        {
            work.Clear();
            work.AddRange(input);
            ZonderqOS.GUI.Apps.FileEntrySorter.Sort(work, ref scratchBuffer);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 64; i++)
        {
            work.Clear();
            work.AddRange(input);
            ZonderqOS.GUI.Apps.FileEntrySorter.Sort(work, ref scratchBuffer);
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert(bytes == 0 && scratchBuffer == null,
            "descending warm sort allocated " + bytes + " B");
        Console.WriteLine("PASS: descending directories stable with duplicate names and 0 B warm allocations");
    }

    private static void BenchmarkDescending()
    {
        var source = MakeDescending(2048, 3);
        var work = new List<ZonderqOS.GUI.Apps.FileEntry>(source.Length);
        ZonderqOS.GUI.Apps.FileEntry[] oldScratch =
            new ZonderqOS.GUI.Apps.FileEntry[source.Length];
        ZonderqOS.GUI.Apps.FileEntry[] newScratch = null;
        for (int i = 0; i < 8; i++)
        {
            TimedAdaptive(false, source, work, ref oldScratch);
            TimedAdaptive(true, source, work, ref newScratch);
        }
        double[] before = new double[7], after = new double[7];
        for (int i = 0; i < 7; i++)
        {
            if ((i & 1) == 0)
            {
                before[i] = TimedAdaptive(false, source, work, ref oldScratch);
                after[i] = TimedAdaptive(true, source, work, ref newScratch);
            }
            else
            {
                after[i] = TimedAdaptive(true, source, work, ref newScratch);
                before[i] = TimedAdaptive(false, source, work, ref oldScratch);
            }
        }
        Assert(newScratch == null, "descending benchmark allocated scratch");
        double a = Median(before), b = Median(after);
        Console.WriteLine("HOST A/B descending directory n=2048 duplicates=3" +
            " previous-merge=" + a.ToString("F4") + " ms in-place=" +
            b.ToString("F4") + " ms ratio=" + (a / b).ToString("F2") +
            "x (7 alternating samples, 8 sorts each; no FAT32 I/O)");
    }

    // A rotated directory is two individually sorted runs. Check that
    // merging them preserves the exact reference order of the old stable
    // insertion sort, including case-insensitive duplicate names.
    private static void VerifyTwoRunMerge()
    {
        int[] sizes = { 33, 64, 127, 512, 2048 };
        foreach (int n in sizes)
        {
            for (int duplicates = 0; duplicates <= 1; duplicates++)
            {
                var ordered = new List<ZonderqOS.GUI.Apps.FileEntry>(n);
                for (int i = 0; i < n; i++)
                {
                    string name = "file" + (duplicates == 1 ? i / 3 : i).ToString("D6");
                    if (duplicates == 1 && i % 3 == 1)
                        name = name.ToUpperInvariant();
                    ordered.Add(new ZonderqOS.GUI.Apps.FileEntry(name, false));
                }
                int split = n / 2;
                var rotated = new List<ZonderqOS.GUI.Apps.FileEntry>(n);
                for (int i = split; i < n; i++) rotated.Add(ordered[i]);
                for (int i = 0; i < split; i++) rotated.Add(ordered[i]);

                var expected = new List<ZonderqOS.GUI.Apps.FileEntry>(rotated);
                LegacySort(expected);
                ZonderqOS.GUI.Apps.FileEntry[] scratch = null;
                ZonderqOS.GUI.Apps.FileEntrySorter.Sort(rotated, ref scratch);
                // Strictly separated runs can rotate in place; an equal
                // boundary must still use the stable two-run merge.
                bool strict = Compare(ordered[split - 1], ordered[split]) < 0;
                Assert(strict ? scratch == null : scratch != null,
                    "rotation buffer policy n=" + n + " duplicates=" + duplicates);
                for (int i = 0; i < n; i++)
                {
                    Assert(object.ReferenceEquals(expected[i], rotated[i]),
                        "two-run stable order n=" + n + " duplicates=" + duplicates + " i=" + i);
                    if (scratch != null)
                        Assert(scratch[i] == null,
                            "two-run scratch retained file path n=" + n + " i=" + i);
                }
            }
        }

        // Adversarial input: the last eight suffix pairs look sorted, but
        // there is another inversion earlier in that suffix. The sampling
        // gate must NOT mistake it for two sorted runs.
        var adversarial = new List<ZonderqOS.GUI.Apps.FileEntry>(64);
        for (int i = 32; i < 64; i++)
            adversarial.Add(new ZonderqOS.GUI.Apps.FileEntry("file" + i.ToString("D6"), false));
        for (int i = 0; i < 32; i++)
            adversarial.Add(new ZonderqOS.GUI.Apps.FileEntry("file" + i.ToString("D6"), false));
        var swapped = adversarial[40];
        adversarial[40] = adversarial[41];
        adversarial[41] = swapped;
        var adversarialExpected = new List<ZonderqOS.GUI.Apps.FileEntry>(adversarial);
        LegacySort(adversarialExpected);
        ZonderqOS.GUI.Apps.FileEntry[] adversarialScratch = null;
        ZonderqOS.GUI.Apps.FileEntrySorter.Sort(adversarial, ref adversarialScratch);
        for (int i = 0; i < adversarial.Count; i++)
            Assert(object.ReferenceEquals(adversarialExpected[i], adversarial[i]),
                "sampled-tail false positive at i=" + i);
        Assert(adversarialScratch != null && adversarialScratch[0] == null,
            "sampled-tail fallback did not clear scratch");

        // A warm two-run merge must reuse both the caller's List and its
        // scratch buffer, with no per-refresh allocations.
        const int count = 2048;
        var input = new ZonderqOS.GUI.Apps.FileEntry[count];
        for (int i = 0; i < count; i++)
            input[i] = new ZonderqOS.GUI.Apps.FileEntry("file" + ((i + count / 2) % count).ToString("D6"), false);
        var work = new List<ZonderqOS.GUI.Apps.FileEntry>(count);
        ZonderqOS.GUI.Apps.FileEntry[] reused = new ZonderqOS.GUI.Apps.FileEntry[count];
        for (int repeat = 0; repeat < 8; repeat++)
        {
            work.Clear();
            work.AddRange(input);
            ZonderqOS.GUI.Apps.FileEntrySorter.Sort(work, ref reused);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int repeat = 0; repeat < 64; repeat++)
        {
            work.Clear();
            work.AddRange(input);
            ZonderqOS.GUI.Apps.FileEntrySorter.Sort(work, ref reused);
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert(bytes == 0, "two-run warm merge allocated " + bytes + " B");
        Assert(reused[0] == null && reused[count - 1] == null,
            "two-run warm merge retained file entries");
        Console.WriteLine("PASS: two-run rotated directory stable references, duplicate ties, 0 B warm allocations");
    }

    // A FAT32 directory refresh can contain several independent renames or
    // adjacent insertions. These disjoint inversions must retain exact stable
    // reference order. The baseline deliberately uses the old merge path.
    private static ZonderqOS.GUI.Apps.FileEntry[] MakeSparseSwaps(int swaps)
    {
        const int count = 2048;
        var entries = new ZonderqOS.GUI.Apps.FileEntry[count];
        for (int i = 0; i < count; i++)
            entries[i] = new ZonderqOS.GUI.Apps.FileEntry("file" + i.ToString("D6"), false);
        for (int i = 0; i < swaps; i++)
        {
            int at = 40 + i * (count - 80) / (swaps + 1);
            var previous = entries[at];
            entries[at] = entries[at + 1];
            entries[at + 1] = previous;
        }
        return entries;
    }

    private static void VerifyDenseSwapsFallback()
    {
        foreach (int swaps in new[] { 4, 8, 16 })
        {
            const int count = 2048;
            var source = new ZonderqOS.GUI.Apps.FileEntry[count];
            for (int i = 0; i < count; i++)
                source[i] = new ZonderqOS.GUI.Apps.FileEntry("file" + i.ToString("D6"), false);
            for (int i = 0; i < swaps; i++)
            {
                int at = i * 2 + 2;
                var tmp = source[at];
                source[at] = source[at + 1];
                source[at + 1] = tmp;
            }
            var expected = new List<ZonderqOS.GUI.Apps.FileEntry>(source);
            var actual = new List<ZonderqOS.GUI.Apps.FileEntry>(source);
            LegacySort(expected);
            ZonderqOS.GUI.Apps.FileEntry[] scratch = null;
            ZonderqOS.GUI.Apps.FileEntrySorter.Sort(actual, ref scratch);
            Assert(scratch != null, "dense inversion scan did not fall back to merge");
            for (int i = 0; i < count; i++)
            {
                Assert(object.ReferenceEquals(expected[i], actual[i]),
                    "dense inversion fallback stable reference swaps=" + swaps + " i=" + i);
                Assert(scratch[i] == null, "dense inversion fallback retained old entry");
            }
        }
        Console.WriteLine("PASS: dense-inversion early merge fallback, stable order and cleared scratch");
    }

    private static void VerifySparseSwaps()
    {
        foreach (int swaps in new[] { 5, 8, 12, 16, 32, 64, 65, 128 })
        {
            var source = MakeSparseSwaps(swaps);
            var expected = new List<ZonderqOS.GUI.Apps.FileEntry>(source);
            var actual = new List<ZonderqOS.GUI.Apps.FileEntry>(source);
            LegacySort(expected);
            ZonderqOS.GUI.Apps.FileEntry[] scratch = null;
            ZonderqOS.GUI.Apps.FileEntrySorter.Sort(actual, ref scratch);
            for (int i = 0; i < source.Length; i++)
                Assert(object.ReferenceEquals(expected[i], actual[i]),
                    "sparse inversions stable order swaps=" + swaps + " i=" + i);
            if (swaps <= ZonderqOS.GUI.Apps.FileEntrySorter.MaxLocalInversions)
                Assert(scratch == null, "sparse local repair allocated scratch swaps=" + swaps);
            else
                Assert(scratch != null, "over-budget sparse inversions did not use merge");
            if (scratch != null)
                for (int i = 0; i < source.Length; i++)
                    Assert(scratch[i] == null, "sparse inversion scratch retained entry");
        }
        // Repeated directory refreshes should not allocate after warming,
        // even when sixty-four independent names move in the snapshot.
        var repeated = MakeSparseSwaps(64);
        var work = new List<ZonderqOS.GUI.Apps.FileEntry>(repeated.Length);
        ZonderqOS.GUI.Apps.FileEntry[] warmScratch = null;
        for (int i = 0; i < 8; i++)
        {
            work.Clear();
            work.AddRange(repeated);
            ZonderqOS.GUI.Apps.FileEntrySorter.Sort(work, ref warmScratch);
        }
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 64; i++)
        {
            work.Clear();
            work.AddRange(repeated);
            ZonderqOS.GUI.Apps.FileEntrySorter.Sort(work, ref warmScratch);
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert(bytes == 0, "sixty-four-swap refresh allocated " + bytes + " B");
        Assert(warmScratch == null, "sixteen-swap refresh allocated scratch");
        Console.WriteLine("PASS: 8 sparse-swap cases (5..128), exact stable references; 64-swap refresh 0 B warm allocations");
    }

    private static void BenchmarkSparseSwaps()
    {
        foreach (int swaps in new[] { 8, 16, 32, 64, 128 })
        {
            var source = MakeSparseSwaps(swaps);
            var work = new List<ZonderqOS.GUI.Apps.FileEntry>(source.Length);
            ZonderqOS.GUI.Apps.FileEntry[] baselineScratch =
                new ZonderqOS.GUI.Apps.FileEntry[source.Length];
            ZonderqOS.GUI.Apps.FileEntry[] optimizedScratch =
                new ZonderqOS.GUI.Apps.FileEntry[source.Length];
            for (int i = 0; i < 8; i++)
            {
                TimedAdaptive(false, source, work, ref baselineScratch);
                TimedAdaptive(true, source, work, ref optimizedScratch);
            }
            double[] before = new double[7], after = new double[7];
            for (int sample = 0; sample < 7; sample++)
            {
                if ((sample & 1) == 0)
                {
                    before[sample] = TimedAdaptive(false, source, work, ref baselineScratch);
                    after[sample] = TimedAdaptive(true, source, work, ref optimizedScratch);
                }
                else
                {
                    after[sample] = TimedAdaptive(true, source, work, ref optimizedScratch);
                    before[sample] = TimedAdaptive(false, source, work, ref baselineScratch);
                }
            }
            double a = Median(before), b = Median(after);
            Console.WriteLine("HOST A/B sparse swaps n=2048 swaps=" + swaps +
                " baseline-merge=" + a.ToString("F4") + " ms current=" +
                b.ToString("F4") + " ms ratio=" + (a / b).ToString("F2") +
                "x (7 alternating samples, 8 sorts each; no FAT32 I/O)");
        }
    }

    private static void VerifyAdaptiveRepair()
    {
        for (int mode = 0; mode < 6; mode++)
        {
            var source = MakeNearlySorted(mode);
            var expected = new List<ZonderqOS.GUI.Apps.FileEntry>(source);
            var actual = new List<ZonderqOS.GUI.Apps.FileEntry>(source);
            LegacySort(expected);
            ZonderqOS.GUI.Apps.FileEntry[] scratch = null;
            ZonderqOS.GUI.Apps.FileEntrySorter.Sort(actual, ref scratch);
            for (int i = 0; i < source.Length; i++)
                Assert(object.ReferenceEquals(expected[i], actual[i]),
                    "adaptive stable order mode=" + mode + " i=" + i);
            if (mode != 3)
                Assert(scratch == null, "near-sorted repair allocated merge buffer mode=" + mode);
            else if (Compare(source[source.Length - 1], source[0]) < 0)
                Assert(scratch == null, "strict rotation should not allocate scratch");
            else
                Assert(scratch != null, "overlapping rotation must use stable merge");
            if (scratch != null)
                for (int i = 0; i < source.Length; i++)
                    Assert(scratch[i] == null, "fallback retained file entry i=" + i);
        }

        // Reset the same preallocated list on each iteration, with a cold
        // scratch reference. Neither the repair nor the List reset may
        // allocate after warming up.
        var almostSorted = MakeNearlySorted(0);
        var work = new List<ZonderqOS.GUI.Apps.FileEntry>(almostSorted.Length);
        ZonderqOS.GUI.Apps.FileEntry[] buffer = null;
        for (int i = 0; i < 8; i++)
        {
            work.Clear();
            work.AddRange(almostSorted);
            ZonderqOS.GUI.Apps.FileEntrySorter.Sort(work, ref buffer);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 64; i++)
        {
            work.Clear();
            work.AddRange(almostSorted);
            ZonderqOS.GUI.Apps.FileEntrySorter.Sort(work, ref buffer);
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert(bytes == 0, "adaptive repair allocated " + bytes + " B");
        Assert(buffer == null, "adaptive repair allocated a scratch buffer");
        Console.WriteLine("PASS: 6 adaptive cases including boundary moves and bounded fallback, stable references, 0 B warm allocations");
    }

    // Exact pre-adaptive production behavior for unsorted large folders:
    // scan until the first inversion, then run the existing stable merge.
    private static void PreviousOrderedGateSort(List<ZonderqOS.GUI.Apps.FileEntry> entries,
        ref ZonderqOS.GUI.Apps.FileEntry[] scratch)
    {
        for (int i = 1; i < entries.Count; i++)
            if (Compare(entries[i - 1], entries[i]) > 0)
            {
                PreviousPerfSort(entries, ref scratch);
                return;
            }
    }

    private static double TimedAdaptive(bool optimized,
        ZonderqOS.GUI.Apps.FileEntry[] source,
        List<ZonderqOS.GUI.Apps.FileEntry> work,
        ref ZonderqOS.GUI.Apps.FileEntry[] scratch)
    {
        const int repetitions = 8;
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repetitions; i++)
        {
            work.Clear();
            work.AddRange(source);
            if (optimized)
                ZonderqOS.GUI.Apps.FileEntrySorter.Sort(work, ref scratch);
            else
                PreviousOrderedGateSort(work, ref scratch);
            sink += work[0].Name.Length;
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 /
            Stopwatch.Frequency / repetitions;
    }

    private static void BenchmarkAdaptiveRepair()
    {
        for (int mode = 0; mode < 7; mode++)
        {
            var source = mode == 6 ? Make(2048, 309506, false) : MakeNearlySorted(mode);
            var work = new List<ZonderqOS.GUI.Apps.FileEntry>(source.Length);
            ZonderqOS.GUI.Apps.FileEntry[] oldScratch =
                new ZonderqOS.GUI.Apps.FileEntry[source.Length];
            ZonderqOS.GUI.Apps.FileEntry[] newScratch =
                new ZonderqOS.GUI.Apps.FileEntry[source.Length];
            for (int i = 0; i < 8; i++)
            {
                TimedAdaptive(false, source, work, ref oldScratch);
                TimedAdaptive(true, source, work, ref newScratch);
            }
            double[] before = new double[7], after = new double[7];
            for (int i = 0; i < 7; i++)
            {
                if ((i & 1) == 0)
                {
                    before[i] = TimedAdaptive(false, source, work, ref oldScratch);
                    after[i] = TimedAdaptive(true, source, work, ref newScratch);
                }
                else
                {
                    after[i] = TimedAdaptive(true, source, work, ref newScratch);
                    before[i] = TimedAdaptive(false, source, work, ref oldScratch);
                }
            }
            double a = Median(before), b = Median(after);
            Console.WriteLine("HOST A/B adaptive directory n=2048 mode=" + mode +
                " old=" + a.ToString("F4") + " ms new=" + b.ToString("F4") +
                " ms ratio=" + (a / b).ToString("F2") +
                "x (7 alternating samples, 8 sorts each; no FAT32 I/O)");
        }
    }

    private static double TimedOrdered(bool optimized,
        ZonderqOS.GUI.Apps.FileEntry[] source,
        List<ZonderqOS.GUI.Apps.FileEntry> work,
        ref ZonderqOS.GUI.Apps.FileEntry[] scratch)
    {
        const int repetitions = 8;
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repetitions; i++)
        {
            work.Clear();
            work.AddRange(source);
            if (optimized)
                ZonderqOS.GUI.Apps.FileEntrySorter.Sort(work, ref scratch);
            else
                PreviousPerfSort(work, ref scratch);
            sink += work[0].Name.Length;
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 /
            Stopwatch.Frequency / repetitions;
    }

    private static void BenchmarkAlreadySorted()
    {
        var ordered = new List<ZonderqOS.GUI.Apps.FileEntry>(Make(2048, 44095, false));
        LegacySort(ordered);
        var source = ordered.ToArray();
        var work = new List<ZonderqOS.GUI.Apps.FileEntry>(source.Length);
        ZonderqOS.GUI.Apps.FileEntry[] oldScratch =
            new ZonderqOS.GUI.Apps.FileEntry[source.Length];
        ZonderqOS.GUI.Apps.FileEntry[] newScratch = null;
        for (int i = 0; i < 8; i++)
        {
            TimedOrdered(false, source, work, ref oldScratch);
            TimedOrdered(true, source, work, ref newScratch);
        }
        double[] before = new double[7], after = new double[7];
        for (int i = 0; i < 7; i++)
        {
            if ((i & 1) == 0)
            {
                before[i] = TimedOrdered(false, source, work, ref oldScratch);
                after[i] = TimedOrdered(true, source, work, ref newScratch);
            }
            else
            {
                after[i] = TimedOrdered(true, source, work, ref newScratch);
                before[i] = TimedOrdered(false, source, work, ref oldScratch);
            }
        }
        Assert(newScratch == null, "ordered benchmark allocated scratch");
        double a = Median(before), b = Median(after);
        Console.WriteLine("HOST A/B ordered directory n=2048 previous-merge=" +
            a.ToString("F4") + " ms ordered-gate=" + b.ToString("F4") +
            " ms ratio=" + (a / b).ToString("F2") +
            "x (7 alternating samples, same entries and reused lists; not FAT32 I/O)");
    }

    // Control case: the early ordered-input check must not silently hide a
    // major regression when FAT32 returns a genuinely unsorted directory.
    private static void BenchmarkUnsortedControl()
    {
        var source = Make(2048, 55095, false);
        var work = new List<ZonderqOS.GUI.Apps.FileEntry>(source.Length);
        ZonderqOS.GUI.Apps.FileEntry[] oldScratch =
            new ZonderqOS.GUI.Apps.FileEntry[source.Length];
        ZonderqOS.GUI.Apps.FileEntry[] newScratch =
            new ZonderqOS.GUI.Apps.FileEntry[source.Length];
        for (int i = 0; i < 8; i++)
        {
            TimedOrdered(false, source, work, ref oldScratch);
            TimedOrdered(true, source, work, ref newScratch);
        }
        double[] before = new double[7], after = new double[7];
        for (int i = 0; i < 7; i++)
        {
            if ((i & 1) == 0)
            {
                before[i] = TimedOrdered(false, source, work, ref oldScratch);
                after[i] = TimedOrdered(true, source, work, ref newScratch);
            }
            else
            {
                after[i] = TimedOrdered(true, source, work, ref newScratch);
                before[i] = TimedOrdered(false, source, work, ref oldScratch);
            }
        }
        double a = Median(before), b = Median(after);
        Console.WriteLine("HOST A/B unsorted control n=2048 previous-merge=" +
            a.ToString("F4") + " ms ordered-gate=" + b.ToString("F4") +
            " ms ratio=" + (a / b).ToString("F2") +
            "x (7 alternating samples, warmed scratch; not FAT32 I/O)");
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

    private static void VerifyIncrementalFilter()
    {
        var source = new List<ZonderqOS.GUI.Apps.FileEntry>(Make(4096, 131, false));
        source.Add(new ZonderqOS.GUI.Apps.FileEntry("Résumé.txt", false));
        source.Add(new ZonderqOS.GUI.Apps.FileEntry("RÉSUMÉ.TXT", false));
        ZonderqOS.GUI.Apps.FileEntry[] scratch = null;
        ZonderqOS.GUI.Apps.FileEntrySorter.Sort(source, ref scratch);
        var visible = new List<ZonderqOS.GUI.Apps.FileEntry>(source.Count);
        var expected = new List<ZonderqOS.GUI.Apps.FileEntry>(source.Count);
        string previous = null;
        string[] edits = {
            "", "f", "fi", "fil", "file", "file0", "file00",
            "file000", "file0001", "file00012", "file0001",
            "FILE0001", "file", "x", "xy", "xyz", "",
            "rés", "résu", "RÉSUM", "résumé", "rés", ""
        };
        foreach (string query in edits)
        {
            // Match the production FileManagerApp dispatch, including the
            // fallback on backspace, replacement and a refreshed directory.
            if (previous != null && query.Length > previous.Length &&
                query.StartsWith(previous, StringComparison.OrdinalIgnoreCase))
                ZonderqOS.GUI.Apps.FileEntrySorter.FilterNarrowed(visible, query);
            else
                ZonderqOS.GUI.Apps.FileEntrySorter.Filter(source, visible, query);

            ZonderqOS.GUI.Apps.FileEntrySorter.Filter(source, expected, query);
            Assert(expected.Count == visible.Count, "incremental count " + query);
            for (int i = 0; i < expected.Count; i++)
                Assert(object.ReferenceEquals(expected[i], visible[i]),
                    "incremental stable order " + query + " at " + i);
            previous = query;
        }

        // A fresh directory snapshot must always start from the full list.
        visible.Clear();
        ZonderqOS.GUI.Apps.FileEntrySorter.Filter(source, visible, "file0001");
        long before = GC.GetAllocatedBytesForCurrentThread();
        ZonderqOS.GUI.Apps.FileEntrySorter.FilterNarrowed(visible, "file00012");
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert(allocated == 0, "incremental narrowing allocated " + allocated + " B");
        ZonderqOS.GUI.Apps.FileEntrySorter.Filter(source, expected, "file00012");
        Assert(visible.Count == expected.Count, "incremental allocation check count");
        for (int i = 0; i < expected.Count; i++)
            Assert(object.ReferenceEquals(visible[i], expected[i]), "incremental final order");
        Assert(source.Count == 4098, "incremental filter modified source");
        Console.WriteLine("PASS: production incremental search, reference-equivalent and 0 B warm allocations");
    }

    // Frozen previous production compaction loop. A/B against the real
    // FileEntrySorter.FilterNarrowed without timing List<T> preparation.
    private static void PreviousNarrowedFilter(
        List<ZonderqOS.GUI.Apps.FileEntry> visible, string search)
    {
        if (string.IsNullOrEmpty(search))
            return;
        int write = 0;
        int count = visible.Count;
        for (int read = 0; read < count; read++)
        {
            var item = visible[read];
            if (item.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                visible[write++] = item;
        }
        if (write < count)
            visible.RemoveRange(write, count - write);
    }

    private static void BenchmarkNarrowedCompaction()
    {
        var source = new List<ZonderqOS.GUI.Apps.FileEntry>(8192);
        for (int i = 0; i < 8192; i++)
            source.Add(new ZonderqOS.GUI.Apps.FileEntry("file" + i.ToString("D5"), false));
        var oldWork = new List<ZonderqOS.GUI.Apps.FileEntry>(source.Count);
        var newWork = new List<ZonderqOS.GUI.Apps.FileEntry>(source.Count);
        foreach (string query in new[] { "file", "file1", "notfound" })
        {
            oldWork.Clear(); oldWork.AddRange(source);
            newWork.Clear(); newWork.AddRange(source);
            PreviousNarrowedFilter(oldWork, query);
            ZonderqOS.GUI.Apps.FileEntrySorter.FilterNarrowed(newWork, query);
            Assert(oldWork.Count == newWork.Count, "narrowed compaction count " + query);
            for (int i = 0; i < oldWork.Count; i++)
                Assert(object.ReferenceEquals(oldWork[i], newWork[i]),
                    "narrowed compaction stable order " + query);

            double[] oldTimes = new double[7];
            double[] newTimes = new double[7];
            for (int round = 0; round < 7; round++)
            {
                bool optimizedFirst = (round & 1) != 0;
                for (int side = 0; side < 2; side++)
                {
                    bool optimized = side == 0 ? optimizedFirst : !optimizedFirst;
                    long elapsed = 0;
                    for (int rep = 0; rep < 100; rep++)
                    {
                        var work = optimized ? newWork : oldWork;
                        work.Clear(); work.AddRange(source);
                        long start = Stopwatch.GetTimestamp();
                        if (optimized)
                            ZonderqOS.GUI.Apps.FileEntrySorter.FilterNarrowed(work, query);
                        else
                            PreviousNarrowedFilter(work, query);
                        elapsed += Stopwatch.GetTimestamp() - start;
                        sink += work.Count;
                    }
                    double ms = elapsed * 1000.0 / Stopwatch.Frequency / 100.0;
                    if (optimized) newTimes[round] = ms;
                    else oldTimes[round] = ms;
                }
            }
            double before = Median(oldTimes);
            double after = Median(newTimes);
            Console.WriteLine("HOST A/B narrowed compaction n=8192 query=" + query +
                " old=" + before.ToString("F4") + " ms new=" +
                after.ToString("F4") + " ms ratio=" +
                (before / after).ToString("F2") + "x (.NET Release host)");
        }
    }

    private static void BenchmarkIncrementalFilter()
    {
        var source = new List<ZonderqOS.GUI.Apps.FileEntry>(Make(8192, 229, false));
        var work = new List<ZonderqOS.GUI.Apps.FileEntry>(source.Count);
        const string prefix = "file0001";
        const string query = "file00012";
        double[] oldMs = new double[7];
        double[] newMs = new double[7];
        for (int round = 0; round < 7; round++)
        {
            bool narrowFirst = (round & 1) == 0;
            for (int side = 0; side < 2; side++)
            {
                bool narrowed = side == 0 ? narrowFirst : !narrowFirst;
                long elapsedTicks = 0;
                for (int rep = 0; rep < 100; rep++)
                {
                    // The preceding keystroke was already applied. Exclude
                    // preparing that state from both timed alternatives.
                    ZonderqOS.GUI.Apps.FileEntrySorter.Filter(source, work, prefix);
                    long start = Stopwatch.GetTimestamp();
                    if (narrowed)
                        ZonderqOS.GUI.Apps.FileEntrySorter.FilterNarrowed(work, query);
                    else
                        ZonderqOS.GUI.Apps.FileEntrySorter.Filter(source, work, query);
                    elapsedTicks += Stopwatch.GetTimestamp() - start;
                    sink += work.Count;
                }
                double ms = elapsedTicks * 1000.0 / Stopwatch.Frequency / 100.0;
                if (narrowed) newMs[round] = ms;
                else oldMs[round] = ms;
            }
        }
        double baseline = Median(oldMs);
        double optimized = Median(newMs);
        Console.WriteLine("HOST A/B incremental search n=8192 prefix=" + prefix +
            " query=" + query + " full=" + baseline.ToString("F4") +
            " ms narrowed=" + optimized.ToString("F4") +
            " ms ratio=" + (baseline / optimized).ToString("F2") + "x");
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
        PathMatcherSmoke.Run();
        VerifyFilter();
        VerifyIncrementalFilter();
        VerifyAlreadySortedFastPath();
        VerifyAdaptiveRepair();
        VerifySparseSwaps();
        VerifyDenseSwapsFallback();
        VerifyTwoRunMerge();
        RotatedRunSmoke.Run();
        DescendingReverseSmoke.Run();
        VerifyDescendingFastPath();
        BenchmarkAdaptiveRepair();
        BenchmarkSparseSwaps();
        BenchmarkDescending();
        BenchmarkAlreadySorted();
        BenchmarkUnsortedControl();
        BenchmarkFilter();
        BenchmarkIncrementalFilter();
        BenchmarkNarrowedCompaction();
        Benchmark(32, true);
        Benchmark(128, false);
        Benchmark(512, false);
        Benchmark(2048, true);
        BenchmarkGuiBranch(512);
        BenchmarkGuiBranch(2048);
        Console.WriteLine("PASS: production FileEntrySorter smoke, sink=" + sink);
    }
}
