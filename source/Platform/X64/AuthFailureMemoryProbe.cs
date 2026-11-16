#if ZONDERQ_AUTH_FAIL_MEM_DIAG
using Cosmos.Kernel.Core.Memory;
using CosmosGc = Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector;

namespace ZonderqOS.Platform.X64
{
    /// <summary>
    /// One-shot COM1 checkpoints, opt-in RAM-root x64 QEMU diagnostic only.
    /// Never formats passwords or usernames and never performs disk IO/GC.
    /// </summary>
    internal static class AuthFailureMemoryProbe
    {
        internal static void Mark(string phase)
        {
            try
            {
                ulong total = PageAllocator.TotalPageCount;
                ulong free = PageAllocator.FreePageCount;
                ulong used = total >= free ? total - free : 0UL;
                ulong pEmpty, pGc, pSmall, pMedium, pLarge, pUnmanaged;
                ulong pDir, pAlloc, pSmt, pExtension, pUnknown;
                PageAllocator.GetPageCountsByType(out pEmpty, out pGc, out pSmall,
                    out pMedium, out pLarge, out pUnmanaged, out pDir,
                    out pAlloc, out pSmt, out pExtension, out pUnknown);

                SerialMemoryTelemetry.WriteAscii("[AUTH_FAIL_MEM] phase=");
                SerialMemoryTelemetry.WriteAscii(phase);
                SerialMemoryTelemetry.WriteAscii(" used_p=");
                SerialMemoryTelemetry.WriteUInt(used);
                SerialMemoryTelemetry.WriteAscii(" p_gc=");
                SerialMemoryTelemetry.WriteUInt(pGc);
                SerialMemoryTelemetry.WriteAscii(" p_rat=");
                SerialMemoryTelemetry.WriteUInt(pSmall + pMedium + pLarge);
                SerialMemoryTelemetry.WriteAscii(" p_sys=");
                SerialMemoryTelemetry.WriteUInt(pUnmanaged + pDir + pAlloc + pSmt + pExtension + pUnknown);
                SerialMemoryTelemetry.WriteAscii(" heap_kb=");
                SerialMemoryTelemetry.WriteUInt(CosmosGc.GetHeapSizeBytes() / 1024UL);
                SerialMemoryTelemetry.WriteAscii(" comm_kb=");
                SerialMemoryTelemetry.WriteUInt(CosmosGc.GetTotalCommittedBytes() / 1024UL);
                SerialMemoryTelemetry.WriteChar('\n');
            }
            catch
            {
                // Diagnostics never affect the authentication decision.
            }
        }
    }
}
#endif
