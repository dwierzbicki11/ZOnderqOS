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

            // FAT32 snapshots and repeated refreshes can already be in the
            // requested order. A linear check avoids the merge passes and
            // scratch allocation entirely in that case. On unsorted input it
            // exits at the first inversion (usually after one comparison).
            bool alreadySorted = true;
            for (int i = 1; i < count; i++)
            {
                if (Compare(entries[i - 1], entries[i]) > 0)
                {
                    alreadySorted = false;
                    break;
                }
            }
            if (alreadySorted)
                return;

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

        private static int Compare(FileEntry a, FileEntry b)
        {
            if (a.IsDirectory != b.IsDirectory)
                return a.IsDirectory ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }
    }
}
