#if ZONDERQ_EXPERIMENTAL_GC_PROBE
using System;
using System.Threading;
using Cosmos.Kernel.Core.Memory;
using ZonderqOS.SystemCore;
using CosmosGc = Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector;

namespace ZonderqOS.Commands
{
    /// <summary>
    /// DANGEROUS, RAM-root QEMU EXPERIMENT ONLY.
    /// Not built into production. Runs exactly one explicit GC collection
    /// per boot after an authenticated-root, opt-in command.
    ///
    /// The stock OrionGC full collection stops all threads. Earlier forced
    /// collections in the real GUI/input workload triggered #PF/#GP.
    /// No claim of stability or memory reclamation is made by this probe.
    /// </summary>
    public sealed class CmdGcProbe : ICommand
    {
        private static int collectedOnce;

        public string Name => "gcprobe";
        public string Description => "EXPERIMENT ONLY: one OrionGC collection on RAM-root QEMU";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length != 3 || args[1] != "collect" ||
                args[2] != "--accept-qemu-crash")
            {
                CommandIO.WriteLine("Usage (QEMU RAM-root ONLY): gcprobe collect --accept-qemu-crash");
                CommandIO.WriteLine("This can #PF/#GP or hang the kernel. It is not part of production.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!SecurityContext.IsAuthenticated || SecurityContext.CurrentUid != 0)
            {
                CommandIO.WriteLine("ERROR: Only authenticated root may run the experimental GC probe.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (Interlocked.CompareExchange(ref collectedOnce, 1, 0) != 0)
            {
                CommandIO.WriteLine("GC probe already executed this boot. Reboot QEMU for another test.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            // All numerical snapshots happen before formatting a console line.
            // The serial snapshot itself does not allocate managed strings.
            ulong beforeUsed = UsedPages();
            ulong beforeCommitted = CosmosGc.GetTotalCommittedBytes();
            ulong beforeHeap = CosmosGc.GetHeapSizeBytes();
            int beforeCollections = CosmosGc.GetCollectionIndex();
            long start = System.Diagnostics.Stopwatch.GetTimestamp();

            Trace("before", beforeUsed, beforeHeap, beforeCommitted, beforeCollections);

            // ONLY explicit collection in this experimental QEMU-only build.
            // Not reachable in the production kernel or sys_guardian loop.
            GC.Collect();

            long end = System.Diagnostics.Stopwatch.GetTimestamp();
            ulong afterUsed = UsedPages();
            ulong afterCommitted = CosmosGc.GetTotalCommittedBytes();
            ulong afterHeap = CosmosGc.GetHeapSizeBytes();
            int afterCollections = CosmosGc.GetCollectionIndex();

            Trace("after", afterUsed, afterHeap, afterCommitted, afterCollections);

            CommandIO.WriteLine("GC experiment finished (this does NOT certify runtime stability).");
            CommandIO.WriteLine("Collections delta: " + (afterCollections - beforeCollections));
            CommandIO.WriteLine("Used pages before/after: " + beforeUsed + " -> " + afterUsed);
            CommandIO.WriteLine("Committed GC bytes before/after: " + beforeCommitted + " -> " + afterCommitted);
            CommandIO.WriteLine("Heap bytes before/after: " + beforeHeap + " -> " + afterHeap);
            long elapsedTicks = end - start;
            long freq = System.Diagnostics.Stopwatch.Frequency;
            if (freq > 0 && elapsedTicks >= 0)
                CommandIO.WriteLine("GC duration ms (approx): " + (elapsedTicks * 1000L / freq));
            CommandIO.WriteLine("Inspect COM1 [GC_PROBE] and full [MEM_SAMPLE] series.");
            CommandIO.LastCommandSuccess = true;
        }

        private static ulong UsedPages()
        {
            ulong total = PageAllocator.TotalPageCount;
            ulong free = PageAllocator.FreePageCount;
            return total > free ? total - free : 0;
        }

        private static void Trace(string stage, ulong usedPages, ulong heapBytes,
            ulong committedBytes, int collections)
        {
            global::ZonderqOS.Platform.X64.SerialMemoryTelemetry.WriteAscii("[GC_PROBE] stage=");
            global::ZonderqOS.Platform.X64.SerialMemoryTelemetry.WriteAscii(stage);
            global::ZonderqOS.Platform.X64.SerialMemoryTelemetry.WriteAscii(" used_p=");
            global::ZonderqOS.Platform.X64.SerialMemoryTelemetry.WriteUInt(usedPages);
            global::ZonderqOS.Platform.X64.SerialMemoryTelemetry.WriteAscii(" heap_kb=");
            global::ZonderqOS.Platform.X64.SerialMemoryTelemetry.WriteUInt(heapBytes / 1024UL);
            global::ZonderqOS.Platform.X64.SerialMemoryTelemetry.WriteAscii(" committed_kb=");
            global::ZonderqOS.Platform.X64.SerialMemoryTelemetry.WriteUInt(committedBytes / 1024UL);
            global::ZonderqOS.Platform.X64.SerialMemoryTelemetry.WriteAscii(" collections=");
            global::ZonderqOS.Platform.X64.SerialMemoryTelemetry.WriteUInt((ulong)Math.Max(0, collections));
            global::ZonderqOS.Platform.X64.SerialMemoryTelemetry.WriteChar('\n');
        }
    }
}
#endif
