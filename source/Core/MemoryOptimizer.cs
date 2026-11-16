using System;
using System.Diagnostics;
using System.Threading;
using Cosmos.Kernel.Core.Memory;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Allocation-conscious, opt-in cache maintenance policy.
    ///
    /// Samples PageAllocator every 10 seconds on the EXISTING sys_guardian thread.
    /// Requests a bounded cache sweep only after substantial growth (or critical
    /// pressure), with a cooldown. A GUI request is executed on the GUI thread;
    /// no background thread ever edits live renderer buffers.
    ///
    /// IMPORTANT: This intentionally does not call GC.Collect, Heap.Collect or
    /// free unmanaged pages. Dropping a managed reference only makes an array
    /// eligible for a future GC; it does not mean RAM has been returned.
    /// </summary>
    public static class MemoryOptimizer
    {
        private const ulong GrowthThresholdBytes = 8UL * 1024UL * 1024UL;
        private const int SampleIntervalSeconds = 10;
        private const int TrimCooldownSeconds = 120;
        private const int MaxColdBuffersPerSweep = 16;

        private static readonly object Gate = new object();
        private static int guiReady;
        private static int pendingTrim;

        private static long lastSampleTicks;
        private static long lastRequestTicks;
        private static bool hasBaseline;
        private static ulong baselineUsedPages;
        private static ulong lastUsedPages;
        private static long lastDeltaPages;
        private static long samples;
        private static long requests;
        private static long sweeps;
        private static long releasedBuffers;
        private static ulong unrootedCandidateBytes;

        public static int SweepBufferLimit => MaxColdBuffersPerSweep;

        /// <summary>Mark GUI availability; never retain a reference to GUI objects.</summary>
        public static void SetGuiReady(bool ready)
        {
            Volatile.Write(ref guiReady, ready ? 1 : 0);
            if (!ready)
                Interlocked.Exchange(ref pendingTrim, 0);

            lock (Gate)
            {
                hasBaseline = false;
                // Reset the startup baseline. Boot allocations must not cause a
                // spurious trim after the authenticated desktop becomes ready.
                lastRequestTicks = 0;
            }
        }

        /// <summary>
        /// Called by sys_guardian on its existing 2s wakeup; actual sampling
        /// is gated to one per 10s. No strings, disk writes, or dynamic buffers.
        /// </summary>
        public static void SampleIfDue(long nowTicks, long frequency)
        {
            if (nowTicks <= 0 || frequency <= 0)
                return;

            long sampleInterval = SampleIntervalSeconds * frequency;
            long cooldown = TrimCooldownSeconds * frequency;

            lock (Gate)
            {
                if (lastSampleTicks > 0 && nowTicks >= lastSampleTicks &&
                    nowTicks - lastSampleTicks < sampleInterval)
                    return;

                lastSampleTicks = nowTicks;

                ulong totalPages;
                ulong freePages;
                ulong pageSize;
                try
                {
                    totalPages = PageAllocator.TotalPageCount;
                    freePages = PageAllocator.FreePageCount;
                    pageSize = PageAllocator.PageSize;
                }
                catch
                {
                    return;
                }

                if (totalPages == 0 || pageSize == 0)
                    return;

                if (freePages > totalPages)
                    freePages = totalPages;

                ulong usedPages = totalPages - freePages;
                lastDeltaPages = samples > 0 ? (long)usedPages - (long)lastUsedPages : 0;
                lastUsedPages = usedPages;
                samples++;

                if (!hasBaseline)
                {
                    baselineUsedPages = usedPages;
                    hasBaseline = true;
                    return;
                }

                if (usedPages < baselineUsedPages)
                    baselineUsedPages = usedPages;

                ulong thresholdPages = (GrowthThresholdBytes + pageSize - 1) / pageSize;
                bool growing = usedPages - baselineUsedPages >= thresholdPages;
                bool criticalPressure = freePages <= totalPages / 8UL;

                if (Volatile.Read(ref guiReady) == 0 || (!growing && !criticalPressure) ||
                    Volatile.Read(ref pendingTrim) != 0)
                    return;

                if (lastRequestTicks > 0 && nowTicks >= lastRequestTicks &&
                    nowTicks - lastRequestTicks < cooldown)
                    return;

                if (Interlocked.CompareExchange(ref pendingTrim, 1, 0) != 0)
                    return;

                lastRequestTicks = nowTicks;
                baselineUsedPages = usedPages;
                requests++;
            }
        }

        /// <summary>
        /// Dequeue on GUI thread only, between application updates and rendering.
        /// No GC or arbitrary cache clearing is done by this method.
        /// </summary>
        public static bool TryTakeGuiTrimRequest()
        {
            return Volatile.Read(ref guiReady) != 0 &&
                   Volatile.Read(ref pendingTrim) != 0 &&
                   Interlocked.CompareExchange(ref pendingTrim, 0, 1) == 1;
        }

        /// <summary>
        /// Report actual cache entries detached from roots by the GUI.
        /// Candidate bytes are NOT a claim of physical pages reclaimed.
        /// </summary>
        public static void RecordGuiSweep(int detachedBuffers, ulong candidateBytes)
        {
            if (detachedBuffers < 0)
                detachedBuffers = 0;

            lock (Gate)
            {
                sweeps++;
                releasedBuffers += detachedBuffers;
                ulong remaining = ulong.MaxValue - unrootedCandidateBytes;
                unrootedCandidateBytes += candidateBytes <= remaining ? candidateBytes : remaining;
            }
        }

        public static MemoryOptimizerSnapshot GetSnapshot()
        {
            lock (Gate)
            {
                return new MemoryOptimizerSnapshot(
                    samples, requests, sweeps, releasedBuffers,
                    unrootedCandidateBytes, lastUsedPages, lastDeltaPages,
                    Volatile.Read(ref pendingTrim) != 0);
            }
        }
    }

    public readonly struct MemoryOptimizerSnapshot
    {
        public readonly long Samples;
        public readonly long Requests;
        public readonly long Sweeps;
        public readonly long DetachedBuffers;
        public readonly ulong CandidateBytes;
        public readonly ulong LastUsedPages;
        public readonly long LastDeltaPages;
        public readonly bool Pending;

        public MemoryOptimizerSnapshot(long samples, long requests, long sweeps,
            long detachedBuffers, ulong candidateBytes, ulong lastUsedPages,
            long lastDeltaPages, bool pending)
        {
            Samples = samples;
            Requests = requests;
            Sweeps = sweeps;
            DetachedBuffers = detachedBuffers;
            CandidateBytes = candidateBytes;
            LastUsedPages = lastUsedPages;
            LastDeltaPages = lastDeltaPages;
            Pending = pending;
        }
    }
}
