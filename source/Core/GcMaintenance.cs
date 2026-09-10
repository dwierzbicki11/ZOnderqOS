using System.Diagnostics;
using Cosmos.Kernel.Core.Memory;
using CosmosGc = Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Performs proactive OrionGC maintenance from the GUI/main execution path.
    ///
    /// Cosmos Gen3 normally runs a full collection only when the allocator cannot
    /// refill its allocation context. On a machine with plenty of free RAM this can
    /// let the GC heap high-water mark keep growing for a long time even when most
    /// of the allocations are temporary GUI objects.
    ///
    /// We deliberately do NOT collect on the sys_guardian background thread. Pump()
    /// is called after application updates on the GUI thread, so a collection cannot
    /// race the GUI renderer/input handlers. OrionGC itself disables interrupts for
    /// the mark/sweep phase, making this a short stop-the-world maintenance point.
    /// </summary>
    public static class GcMaintenance
    {
        private const ulong HeapGrowthTriggerBytes = 4UL * 1024UL * 1024UL;
        private const ulong CriticalFreeMemoryPercent = 12UL;
        private const int CheckEveryPumps = 90;
        private const int MinimumSecondsBetweenCollections = 4;

        private static int pumpCounter;
        private static long lastCollectionTimestamp;
        private static ulong baselineHeapBytes;
        private static volatile bool collectionRequested;

        public static int MaintenanceCollections { get; private set; }
        public static int LastFreedObjects { get; private set; }
        public static ulong LastHeapBeforeBytes { get; private set; }
        public static ulong LastHeapAfterBytes { get; private set; }

        /// <summary>
        /// Requests a collection at the next safe GUI maintenance point.
        /// This method is safe to call from a background monitor because it only
        /// flips a flag; the collector itself is never invoked here.
        /// </summary>
        public static void RequestCollection()
        {
            collectionRequested = true;
        }

        /// <summary>
        /// Called from ApplicationManager.Update() after all applications have
        /// completed their Update() call for the current loop iteration.
        /// </summary>
        public static void Pump()
        {
            if (!CosmosGc.IsEnabled)
                return;

            pumpCounter++;
            bool requested = collectionRequested;
            if (!requested && pumpCounter < CheckEveryPumps)
                return;

            pumpCounter = 0;

            long frequency = Stopwatch.Frequency;
            long now = Stopwatch.GetTimestamp();
            if (frequency > 0 && lastCollectionTimestamp != 0)
            {
                long minimumIntervalTicks = frequency * MinimumSecondsBetweenCollections;
                if (now - lastCollectionTimestamp < minimumIntervalTicks)
                    return;
            }

            ulong heapBytes;
            ulong totalPages;
            ulong freePages;

            try
            {
                heapBytes = CosmosGc.GetHeapSizeBytes();
                totalPages = PageAllocator.TotalPageCount;
                freePages = PageAllocator.FreePageCount;
            }
            catch
            {
                return;
            }

            if (baselineHeapBytes == 0)
                baselineHeapBytes = heapBytes;

            bool heapGrewEnough = heapBytes > baselineHeapBytes &&
                                  heapBytes - baselineHeapBytes >= HeapGrowthTriggerBytes;

            bool memoryPressure = false;
            if (totalPages > 0)
            {
                ulong freePercent = (freePages * 100UL) / totalPages;
                memoryPressure = freePercent <= CriticalFreeMemoryPercent;
            }

            if (!requested && !heapGrewEnough && !memoryPressure)
            {
                // If another automatic collection lowered the heap, follow it so the
                // next trigger measures growth from the new stable point.
                if (heapBytes < baselineHeapBytes)
                    baselineHeapBytes = heapBytes;
                return;
            }

            // Consume the request before entering the collector. If collection fails,
            // a future growth/pressure check can request another attempt.
            collectionRequested = false;
            LastHeapBeforeBytes = heapBytes;

            try
            {
                LastFreedObjects = CosmosGc.Collect();
                LastHeapAfterBytes = CosmosGc.GetHeapSizeBytes();
                baselineHeapBytes = LastHeapAfterBytes;
                MaintenanceCollections++;
                lastCollectionTimestamp = Stopwatch.GetTimestamp();
            }
            catch
            {
                // Never let maintenance take down the GUI loop. The allocator still
                // retains its normal last-resort automatic collection path.
                baselineHeapBytes = heapBytes;
                lastCollectionTimestamp = now;
            }
        }
    }
}
