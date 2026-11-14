using System;

namespace ZonderqOS.GUI.Apps
{
    /// <summary>
    /// Thread-safe and zero-allocation memory label formatter and cache for Task Manager.
    /// Recomputes formatted labels strictly when the value displayed to the user crosses
    /// a visible precision boundary.
    /// </summary>
    internal sealed class TaskManagerMemoryCache
    {
        private const ulong MiB = 1024UL * 1024UL;
        private const ulong PageSize = 4096UL;

        private ulong lastDisplayedUsedMiB = ulong.MaxValue;
        private ulong lastDisplayedUsedHundredths = ulong.MaxValue;
        private ulong lastDisplayedFreeMiB = ulong.MaxValue;
        private ulong lastDisplayedFreeHundredths = ulong.MaxValue;
        private ulong lastDisplayedTotalMiB = ulong.MaxValue;
        private ulong lastDisplayedTotalHundredths = ulong.MaxValue;
        private ulong lastDisplayedScaledUsage = ulong.MaxValue;

        private ulong lastDisplayedGcHeapBytes = ulong.MaxValue;
        private ulong lastDisplayedGcHeapMiB = ulong.MaxValue;
        private ulong lastDisplayedGcHeapHundredths = ulong.MaxValue;

        private ulong lastDisplayedGcCommittedBytes = ulong.MaxValue;
        private ulong lastDisplayedGcCommittedMiB = ulong.MaxValue;
        private ulong lastDisplayedGcCommittedHundredths = ulong.MaxValue;

        private ulong lastSnapInstalledBytes = ulong.MaxValue;
        private ulong lastSnapUsableBytes = ulong.MaxValue;
        private ulong lastSnapTotalPages = ulong.MaxValue;

        private ulong lastExtendedGcFragBytes = ulong.MaxValue;
        private ulong lastExtendedGcFragMiB = ulong.MaxValue;
        private ulong lastExtendedGcFragHundredths = ulong.MaxValue;
        private int lastCollectionIndex = int.MinValue;
        private int lastObjectsFreed = int.MinValue;
        private ulong lastGcHeapPages = ulong.MaxValue;
        private ulong lastRatHeapPages = ulong.MaxValue;
        private ulong lastSysPages = ulong.MaxValue;
        private ulong lastGfxBufferBytes = ulong.MaxValue;
        private bool hasBaselineDelta = false;
        private long lastDeltaPages = long.MinValue;

        private const string ZeroDeltaLabel = "0 B (0 stron)";

        public string UsedMemoryText { get; private set; } = "0.00 MB";
        public string FreeMemoryText { get; private set; } = "0.00 MB";
        public string TotalMemoryText { get; private set; } = "0.00 MB";
        public string MemoryUsageText { get; private set; } = "0.00%";
        public string GcHeapText { get; private set; } = "N/A";
        public string GcCommittedText { get; private set; } = "N/A";
        public string GcFragmentationText { get; private set; } = "N/A";
        public string GcCollectionsText { get; private set; } = "N/A";
        public string PageBreakdownText { get; private set; } = "N/A";
        public string GraphicsBuffersText { get; private set; } = "0 B";
        public string MemoryDeltaText { get; private set; } = ZeroDeltaLabel;
        public string InstalledRamText { get; private set; } = "0 B";
        public string UsableRamText { get; private set; } = "0 B";
        public string HeapPoolText { get; private set; } = "0 B";
        public string MemoryDetailText { get; private set; } = "0.00 MB (0.00%)";
        public string MemoryPagesText { get; private set; } = "0 (4096 B/strona)";
        public string HardwareRamText { get; private set; } = "0 B / 0 B";

        /// <summary>
        /// Diagnostic count of string allocations performed. Tested by regression contracts.
        /// </summary>
        public int FormattedLabelCount { get; private set; }

        public void Update(ulong usedPages, ulong freePages, ulong totalPages,
                           ulong gcHeapBytes, ulong gcCommittedBytes,
                           ulong physicalInstalledBytes, ulong physicalUsableBytes)
        {
            ulong usedBytes = usedPages * PageSize;
            ulong usedMiB = usedBytes / MiB;
            ulong usedHundredths = (usedBytes % MiB) * 100UL / MiB;
            bool usedChanged = false;

            if (usedMiB != lastDisplayedUsedMiB || usedHundredths != lastDisplayedUsedHundredths)
            {
                lastDisplayedUsedMiB = usedMiB;
                lastDisplayedUsedHundredths = usedHundredths;
                UsedMemoryText = FormatMiBPrecise(usedBytes);
                usedChanged = true;
                FormattedLabelCount++;
            }

            ulong freeBytes = freePages * PageSize;
            ulong freeMiB = freeBytes / MiB;
            ulong freeHundredths = (freeBytes % MiB) * 100UL / MiB;
            if (freeMiB != lastDisplayedFreeMiB || freeHundredths != lastDisplayedFreeHundredths)
            {
                lastDisplayedFreeMiB = freeMiB;
                lastDisplayedFreeHundredths = freeHundredths;
                FreeMemoryText = FormatMiBPrecise(freeBytes);
                FormattedLabelCount++;
            }

            ulong totalBytes = totalPages * PageSize;
            ulong totalMiB = totalBytes / MiB;
            ulong totalHundredths = (totalBytes % MiB) * 100UL / MiB;
            if (totalMiB != lastDisplayedTotalMiB || totalHundredths != lastDisplayedTotalHundredths)
            {
                lastDisplayedTotalMiB = totalMiB;
                lastDisplayedTotalHundredths = totalHundredths;
                TotalMemoryText = FormatMiBPrecise(totalBytes);
                FormattedLabelCount++;
            }

            ulong scaledUsage = totalPages == 0 ? 0 : (usedPages >= totalPages ? 10000UL : Math.Min(9999UL, (ulong)((double)usedPages / totalPages * 10000.0)));
            bool usageChanged = false;
            if (scaledUsage != lastDisplayedScaledUsage)
            {
                lastDisplayedScaledUsage = scaledUsage;
                MemoryUsageText = FormatPercentPrecise(usedPages, totalPages);
                usageChanged = true;
                FormattedLabelCount++;
            }

            if (usedChanged || usageChanged)
            {
                MemoryDetailText = UsedMemoryText + " (" + MemoryUsageText + ")";
                FormattedLabelCount++;
            }

            if (gcHeapBytes == 0)
            {
                if (lastDisplayedGcHeapBytes != 0)
                {
                    lastDisplayedGcHeapBytes = 0;
                    GcHeapText = "N/A";
                }
            }
            else
            {
                ulong heapMiB = gcHeapBytes / MiB;
                ulong heapHundredths = (gcHeapBytes % MiB) * 100UL / MiB;
                if (lastDisplayedGcHeapBytes == 0 || heapMiB != lastDisplayedGcHeapMiB || heapHundredths != lastDisplayedGcHeapHundredths)
                {
                    lastDisplayedGcHeapBytes = gcHeapBytes;
                    lastDisplayedGcHeapMiB = heapMiB;
                    lastDisplayedGcHeapHundredths = heapHundredths;
                    GcHeapText = FormatMiBPrecise(gcHeapBytes);
                    FormattedLabelCount++;
                }
            }

            if (gcCommittedBytes == 0)
            {
                if (lastDisplayedGcCommittedBytes != 0)
                {
                    lastDisplayedGcCommittedBytes = 0;
                    GcCommittedText = "N/A";
                }
            }
            else
            {
                ulong commMiB = gcCommittedBytes / MiB;
                ulong commHundredths = (gcCommittedBytes % MiB) * 100UL / MiB;
                if (lastDisplayedGcCommittedBytes == 0 || commMiB != lastDisplayedGcCommittedMiB || commHundredths != lastDisplayedGcCommittedHundredths)
                {
                    lastDisplayedGcCommittedBytes = gcCommittedBytes;
                    lastDisplayedGcCommittedMiB = commMiB;
                    lastDisplayedGcCommittedHundredths = commHundredths;
                    GcCommittedText = FormatMiBPrecise(gcCommittedBytes);
                    FormattedLabelCount++;
                }
            }

            if (physicalInstalledBytes != lastSnapInstalledBytes || physicalUsableBytes != lastSnapUsableBytes || totalPages != lastSnapTotalPages)
            {
                lastSnapInstalledBytes = physicalInstalledBytes;
                lastSnapUsableBytes = physicalUsableBytes;
                lastSnapTotalPages = totalPages;

                InstalledRamText = FormatBytesSmart(physicalInstalledBytes);
                UsableRamText = FormatBytesSmart(physicalUsableBytes);
                HeapPoolText = FormatBytesSmart(totalPages * PageSize);
                MemoryPagesText = totalPages + " (" + PageSize + " B/strona)";
                HardwareRamText = InstalledRamText + " / " + HeapPoolText;
                FormattedLabelCount += 5;
            }
        }

        public void UpdateExtended(ulong gcHeapPages, ulong ratHeapPages, ulong sysPages,
                                   ulong gcFragmentedBytes, int gcCollectionIndex, int gcObjectsFreed, bool gcEnabled,
                                   ulong gfxBufferBytes, long deltaPagesSincePrev)
        {

            if (!gcEnabled)
            {
                if (lastExtendedGcFragBytes != 0)
                {
                    lastExtendedGcFragBytes = 0;
                    GcFragmentationText = "N/A";
                }
                if (lastCollectionIndex != -1 || lastObjectsFreed != -1)
                {
                    lastCollectionIndex = -1;
                    lastObjectsFreed = -1;
                    GcCollectionsText = "N/A";
                }
            }
            else
            {
                if (gcFragmentedBytes == 0)
                {
                    if (lastExtendedGcFragBytes != 0)
                    {
                        lastExtendedGcFragBytes = 0;
                        GcFragmentationText = "0.00 MB";
                    }
                }
                else
                {
                    ulong fragMiB = gcFragmentedBytes / MiB;
                    ulong fragHundredths = (gcFragmentedBytes % MiB) * 100UL / MiB;
                    if (lastExtendedGcFragBytes == 0 || lastExtendedGcFragBytes == ulong.MaxValue ||
                        fragMiB != lastExtendedGcFragMiB || fragHundredths != lastExtendedGcFragHundredths)
                    {
                        lastExtendedGcFragBytes = gcFragmentedBytes;
                        lastExtendedGcFragMiB = fragMiB;
                        lastExtendedGcFragHundredths = fragHundredths;
                        GcFragmentationText = FormatMiBPrecise(gcFragmentedBytes);
                        FormattedLabelCount++;
                    }
                }

                if (gcCollectionIndex != lastCollectionIndex || gcObjectsFreed != lastObjectsFreed)
                {
                    lastCollectionIndex = gcCollectionIndex;
                    lastObjectsFreed = gcObjectsFreed;
                    GcCollectionsText = gcCollectionIndex + " (" + gcObjectsFreed + " obj)";
                    FormattedLabelCount++;
                }
            }

            if (gcHeapPages != lastGcHeapPages || ratHeapPages != lastRatHeapPages || sysPages != lastSysPages)
            {
                lastGcHeapPages = gcHeapPages;
                lastRatHeapPages = ratHeapPages;
                lastSysPages = sysPages;
                PageBreakdownText = gcHeapPages + " / " + ratHeapPages + " / " + sysPages;
                FormattedLabelCount++;
            }

            if (gfxBufferBytes != lastGfxBufferBytes)
            {
                lastGfxBufferBytes = gfxBufferBytes;
                GraphicsBuffersText = FormatBytesSmart(gfxBufferBytes);
                FormattedLabelCount++;
            }

            if (!hasBaselineDelta || deltaPagesSincePrev != lastDeltaPages)
            {
                hasBaselineDelta = true;
                lastDeltaPages = deltaPagesSincePrev;
                if (deltaPagesSincePrev == 0)
                {
                    MemoryDeltaText = ZeroDeltaLabel;
                }
                else
                {
                    bool negative = deltaPagesSincePrev < 0;
                    ulong absPages = negative ? (ulong)(-deltaPagesSincePrev) : (ulong)deltaPagesSincePrev;
                    string prefix = negative ? "-" : "+";
                    MemoryDeltaText = prefix + FormatBytesSmart(absPages * PageSize) + " (" + prefix + absPages + " p)";
                    FormattedLabelCount++;
                }
            }
        }

        private static readonly string[] Digits2 = PrecomputeDigits2();
        private static string[] PrecomputeDigits2()
        {
            var arr = new string[100];
            for (int i = 0; i < 100; i++)
                arr[i] = i < 10 ? "0" + i : i.ToString();
            return arr;
        }

        private static string D2(int value)
        {
            if ((uint)value < 100)
                return Digits2[value];
            return value.ToString();
        }

        public static string FormatMiBPrecise(ulong bytes)
        {
            ulong whole = bytes / MiB;
            ulong hundredths = (bytes % MiB) * 100UL / MiB;
            return whole + "." + D2((int)hundredths) + " MB";
        }

        public static string FormatPercentPrecise(ulong used, ulong total)
        {
            if (total == 0) return "0.00%";
            if (used >= total) return "100.00%";
            ulong scaled = Math.Min(9999UL, (ulong)((double)used / total * 10000.0));
            return (scaled / 100UL) + "." + D2((int)(scaled % 100UL)) + "%";
        }

        public static string FormatBytesSmart(ulong bytes)
        {
            if (bytes == 0) return "0 B";
            const ulong KiB = 1024UL;
            const ulong MiB = 1024UL * 1024UL;
            const ulong GiB = 1024UL * 1024UL * 1024UL;

            if (bytes >= GiB)
            {
                ulong whole = bytes / GiB;
                ulong hundredths = (bytes % GiB) * 100UL / GiB;
                return whole + "." + D2((int)hundredths) + " GB";
            }
            if (bytes >= MiB)
            {
                ulong whole = bytes / MiB;
                ulong hundredths = (bytes % MiB) * 100UL / MiB;
                return whole + "." + D2((int)hundredths) + " MB";
            }
            if (bytes >= KiB)
            {
                ulong whole = bytes / KiB;
                ulong tenths = (bytes % KiB) * 10UL / KiB;
                return whole + "." + tenths + " KB";
            }
            return bytes + " B";
        }
    }
}
