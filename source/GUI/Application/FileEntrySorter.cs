using System;
using System.Collections.Generic;

namespace ZonderqOS.GUI.Apps
{
    /// <summary>
    /// Stable, allocation-bounded file-manager ordering: directories first, then
    /// case-insensitive names. Preserve the old insertion path for small folders.
    /// The merge buffer is owned by the FileManagerApp instance and reused.
    /// No disk I/O or filesystem mutation occurs here.
    /// </summary>
    internal static class FileEntrySorter
    {
        internal const int InsertionLimit = 32;
        // Up to sixty-four sparse inversions are common after a batch of file
        // renames. The separate 2*n shift budget still bounds worst-case work.
        internal const int MaxLocalInversions = 64;
#if ZONDERQ_PERF_HOST_TEST
        // Host-only A/B switch: compare the same production sort pipeline
        // with its original two-run merge against the in-place rotation.
        internal static bool DisableRotationForBenchmark;
        internal static bool DisableArrayReverseForBenchmark;
#endif

        internal static void Sort(List<FileEntry> entries, ref FileEntry[] scratch)
        {
            int count = entries.Count;
            if (count < 2)
                return;

            if (count <= InsertionLimit)
            {
                // Keep the old small-folder loop in this method to avoid
                // an extra call on the common <=32-entry path.
                for (int i = 1; i < count; i++)
                {
                    FileEntry value = entries[i];
                    int j = i - 1;
                    while (j >= 0 && Compare(entries[j], value) > 0)
                    {
                        entries[j + 1] = entries[j];
                        j--;
                    }
                    entries[j + 1] = value;
                }
                return;
            }

            // Most directory refreshes are already sorted, or differ by only
            // a few moved entries (for example a newly renamed file). Inspect
            // adjacent inversions before allocating a merge buffer.
            int inversions = 0;
            int firstInversion = -1;
            bool cannotRepairLocally = false;
            for (int i = 1; i < count; i++)
            {
                if (Compare(entries[i - 1], entries[i]) <= 0)
                    continue;
                if (inversions++ == 0)
                {
                    firstInversion = i;
                    // If neither side of the first inversion fits past its
                    // opposite neighbor, this is not a single moved entry.
                    // Skip the remaining scan and use merge sort immediately;
                    // this avoids a second half-scan on rotated large runs.
                    if (i > 1 && i + 1 < count &&
                        Compare(entries[i - 2], entries[i]) > 0 &&
                        Compare(entries[i - 1], entries[i + 1]) > 0)
                    {
                        cannotRepairLocally = true;
                        break;
                    }
                }
                // Dense inversions are usually an unsorted enumeration,
                // not independent renames. Stop probing after four inversions
                // in a small fraction of the directory; the stable merge is
                // cheaper than scanning up to 65 inversions. Scale the
                // window for large folders so 128+ clustered swaps do not
                // pay for a long scan only to fall back to merge.
                // Widely spaced renames still reach the bounded O(n) path.
                if (inversions == 4 && i - firstInversion <= Math.Max(12, count / 32))
                {
                    cannotRepairLocally = true;
                    break;
                }
                if (inversions > MaxLocalInversions)
                    break;
            }
            if (inversions == 0)
                return;

            // Check descending snapshots BEFORE insertion repair: a small
            // descending folder can have fewer than MaxLocalInversions
            // inversions. Repairing it first would consume the shift budget
            // and destroy the descending fast path. The first/last samples
            // in IsDescending reject ordinary random folders cheaply.
            if (firstInversion > 0 && firstInversion <= 8 &&
                (cannotRepairLocally || inversions > 1) &&
                IsDescending(entries, count))
            {
                ReverseStableDescending(entries, count);
                return;
            }

            // A rotated pair of large sorted runs also has one inversion,
            // but repairing it with insertion shifts would be quadratic.
            // For one displaced entry, at least one neighbor across the
            // inversion must still fit in the surrounding sorted order.
            // This is only a performance hint; merge sort handles all cases.
            bool likelyLocalDisplacement = inversions != 1 ||
                // A single displaced first/last item costs at most n shifts.
                firstInversion == 1 || firstInversion == count - 1 ||
                (firstInversion >= 2 &&
                    Compare(entries[firstInversion - 2], entries[firstInversion]) <= 0) ||
                (firstInversion + 1 < count &&
                    Compare(entries[firstInversion - 1], entries[firstInversion + 1]) <= 0);

            // A bounded stable insertion repair is O(n) for up to sixty-four
            // sparse displaced entries. Cap total shifts at 2*n to avoid quadratic
            // work on adversarial nearly sorted snapshots. If the budget
            // expires, restore the displaced item before stable merge sort.
            if (inversions <= MaxLocalInversions && !cannotRepairLocally && likelyLocalDisplacement)
            {
                long shifts = 0;
                long budget = (long)count * 2;
                bool repaired = true;
                // Everything before the first inversion is already sorted.
                // Do not rescan that prefix during the insertion repair.
                for (int i = firstInversion; i < count; i++)
                {
                    FileEntry value = entries[i];
                    int j = i - 1;
                    while (j >= 0 && Compare(entries[j], value) > 0)
                    {
                        if (shifts >= budget)
                        {
                            repaired = false;
                            break;
                        }
                        entries[j + 1] = entries[j];
                        j--;
                        shifts++;
                    }
                    entries[j + 1] = value;
                    if (!repaired)
                        break;
                }
                if (repaired)
                    return;
            }

            // Two sorted runs separated by one large inversion (for example,
            // a FAT32 enumeration rotated at a directory boundary) can be
            // merged in O(n), rather than using log2(n) merge passes.
            // Only try this after the local-displacement check rejected the
            // inversion. Sample the tail first: random directories must not
            // pay for another complete O(n) scan.
            if (cannotRepairLocally && inversions == 1 &&
                firstInversion > 1 && firstInversion < count - 1 &&
                HasSortedTail(entries, firstInversion, count))
            {
                // Two ordered runs can be rotated without a merge buffer when
                // EVERY key in the tail is strictly below EVERY key in the
                // head. Because each run is sorted, comparing their boundary
                // keys proves this in O(1). Strictness matters: equal keys
                // spanning the boundary must retain their original stable
                // order, so they continue through the merge path below.
                if (Compare(entries[count - 1], entries[0]) < 0
#if ZONDERQ_PERF_HOST_TEST
                    && !DisableRotationForBenchmark
#endif
                    )
                {
                    ReverseRange(entries, 0, firstInversion);
                    ReverseRange(entries, firstInversion, count);
                    ReverseRange(entries, 0, count);
                    return;
                }

                if (scratch == null || scratch.Length < count)
                    scratch = new FileEntry[count];

                int left = 0;
                int right = firstInversion;
                int output = 0;
                while (left < firstInversion && right < count)
                {
                    // Left-first on equality preserves the exact stable
                    // ordering of case-insensitive duplicate file names.
                    scratch[output++] = Compare(entries[left], entries[right]) <= 0
                        ? entries[left++] : entries[right++];
                }
                while (left < firstInversion)
                    scratch[output++] = entries[left++];
                while (right < count)
                    scratch[output++] = entries[right++];
                for (int i = 0; i < count; i++)
                    entries[i] = scratch[i];

                // A reused scratch buffer must never keep references to
                // entries from a directory that has since been unmounted.
                Array.Clear(scratch, 0, count);
                return;
            }

            if (scratch == null || scratch.Length < count)
                scratch = new FileEntry[count];

            // Bottom-up stable merge sort. Alternate between List and scratch,
            // avoiding one allocation per refresh and O(n^2) shifts on large FAT32 directories.
            bool sourceIsList = true;
            int run = 1;
            while (run < count)
            {
                int start = 0;
                while (start < count)
                {
                    int middle = start + Math.Min(run, count - start);
                    int end = middle + Math.Min(run, count - middle);
                    int left = start;
                    int right = middle;
                    int output = start;

                    while (left < middle && right < end)
                    {
                        FileEntry a = sourceIsList ? entries[left] : scratch[left];
                        FileEntry b = sourceIsList ? entries[right] : scratch[right];
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
                        FileEntry item = sourceIsList ? entries[left++] : scratch[left++];
                        if (sourceIsList) scratch[output++] = item;
                        else entries[output++] = item;
                    }

                    while (right < end)
                    {
                        FileEntry item = sourceIsList ? entries[right++] : scratch[right++];
                        if (sourceIsList) scratch[output++] = item;
                        else entries[output++] = item;
                    }

                    start = end;
                }

                sourceIsList = !sourceIsList;
                // Avoid overflow in 2 * run for an extremely large folder.
                if (run >= count - run)
                    break;
                run *= 2;
            }

            if (!sourceIsList)
            {
                for (int i = 0; i < count; i++)
                    entries[i] = scratch[i];
            }

            // Otherwise old entries would remain strongly referenced after
            // navigating to another (smaller) folder.
            Array.Clear(scratch, 0, count);
        }

        // Three reversals rotate two strictly separated, already ordered
        // runs. Reversal is by reference and does not allocate or touch FAT32.
        private static void ReverseRange(List<FileEntry> entries, int start, int end)
        {
            // List<T>.Reverse delegates to the backing array instead of
            // repeatedly paying List<T> indexer checks on each swap.
            entries.Reverse(start, end - start);
        }

        // First and last eight pairs reject almost every random folder
        // without paying for a full scan. The middle is checked only when
        // the cheap samples agree with descending order.
        private static bool IsDescending(List<FileEntry> entries, int count)
        {
            int headEnd = Math.Min(count, 9);
            for (int i = 1; i < headEnd; i++)
                if (Compare(entries[i - 1], entries[i]) < 0)
                    return false;
            int tailStart = Math.Max(headEnd, count - 8);
            for (int i = tailStart; i < count; i++)
                if (Compare(entries[i - 1], entries[i]) < 0)
                    return false;
            for (int i = headEnd; i < tailStart; i++)
                if (Compare(entries[i - 1], entries[i]) < 0)
                    return false;
            return true;
        }

        private static void ReverseStableDescending(List<FileEntry> entries, int count)
        {
            // Array-backed List.Reverse avoids repeated bounds/indexer work
            // when FAT32 returns an entirely descending enumeration.
#if ZONDERQ_PERF_HOST_TEST
            if (DisableArrayReverseForBenchmark)
                ReverseRangeLegacy(entries, 0, count);
            else
#endif
                entries.Reverse();

            // Reversing the whole list also reverses ties. Restore each
            // contiguous tie group to match a stable ascending sort.
            for (int start = 0; start < count;)
            {
                int end = start + 1;
                while (end < count && Compare(entries[start], entries[end]) == 0)
                    end++;
                if (end - start > 1)
                {
#if ZONDERQ_PERF_HOST_TEST
                    if (DisableArrayReverseForBenchmark)
                        ReverseRangeLegacy(entries, start, end);
                    else
#endif
                        entries.Reverse(start, end - start);
                }
                start = end;
            }
        }

#if ZONDERQ_PERF_HOST_TEST
        // Original indexer-based reverse, retained ONLY for an exact host
        // A/B baseline. Never compiled into the production kernel.
        private static void ReverseRangeLegacy(List<FileEntry> entries, int start, int end)
        {
            for (int left = start, right = end - 1; left < right; left++, right--)
            {
                FileEntry item = entries[left];
                entries[left] = entries[right];
                entries[right] = item;
            }
        }
#endif

        // A full suffix scan is only worthwhile when its final few pairs
        // already look ordered. Prefix [0, split) is known sorted because
        // split is the first inversion found by Sort().
        private static bool HasSortedTail(List<FileEntry> entries, int split, int count)
        {
            int sampleStart = Math.Max(split + 1, count - 8);
            for (int i = sampleStart; i < count; i++)
                if (Compare(entries[i - 1], entries[i]) > 0)
                    return false;
            for (int i = split + 1; i < sampleStart; i++)
                if (Compare(entries[i - 1], entries[i]) > 0)
                    return false;
            return true;
        }

        /// <summary>
        /// Rebuild a visible view from an already sorted directory snapshot.
        /// Single forward pass, stable order, no lowercase string allocations
        /// or repeated RemoveAt shifts. The destination list is reused.
        /// </summary>
        internal static void Filter(List<FileEntry> sorted, List<FileEntry> visible, string search)
        {
            visible.Clear();
            if (string.IsNullOrEmpty(search))
            {
                visible.AddRange(sorted);
                return;
            }

            for (int i = 0; i < sorted.Count; i++)
            {
                FileEntry item = sorted[i];
                if (item.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    visible.Add(item);
            }
        }

        /// <summary>
        /// Narrow an existing filtered view when the new query extends the
        /// previous query (OrdinalIgnoreCase). Each new match must already
        /// exist in the old view, so only the visible subset needs scanning.
        /// Compact in place: preserve reference order, reuse capacity and
        /// release discarded entries via List.RemoveRange.
        /// The caller must fall back to Filter on deletion, replacement or
        /// directory refresh; this method never enumerates the filesystem.
        /// </summary>
        internal static void FilterNarrowed(List<FileEntry> visible, string search)
        {
            if (string.IsNullOrEmpty(search))
                return;

            int write = 0;
            int count = visible.Count;
            for (int read = 0; read < count; read++)
            {
                FileEntry item = visible[read];
                if (item.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Most incremental keystrokes leave a long matching
                    // prefix unchanged. Avoid writing those same references
                    // back into the list until the first discarded entry.
                    if (write != read)
                        visible[write] = item;
                    write++;
                }
            }
            if (write < count)
                visible.RemoveRange(write, count - write);
        }

        private static int Compare(FileEntry a, FileEntry b)
        {
            if (a.IsDirectory != b.IsDirectory)
                return a.IsDirectory ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }
    }
}
