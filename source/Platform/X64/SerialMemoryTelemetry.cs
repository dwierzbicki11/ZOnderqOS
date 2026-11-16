using System;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.System.Diagnostics;
using CosmosGc = Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector;

namespace ZonderqOS.Platform.X64
{
    /// <summary>
    /// High-efficiency, zero-allocation serial memory telemetry sampler for QEMU and bare metal.
    /// Writes directly to COM1 UART (0x3F8) without allocating strings or heap objects.
    /// </summary>
    public static class SerialMemoryTelemetry
    {
        private const ushort Com1Data = 0x3F8;
        private const ushort Com1LineStatus = 0x3FD;
        private static long lastSampleTicks = -1;
        private static ulong prevUsedPages = 0;
        private static bool hasPrevSample = false;

        public static void WriteChar(char c)
        {
            try
            {
                int spin = 10000;
                while ((X64PortIo.ReadByte(Com1LineStatus) & 0x20) == 0 && --spin > 0) { }
                X64PortIo.WriteByte(Com1Data, (byte)c);
            }
            catch { }
        }

        public static void WriteAscii(string s)
        {
            if (s == null) return;
            for (int i = 0; i < s.Length; i++)
                WriteChar(s[i]);
        }

        public static void WriteInt(long value)
        {
            if (value < 0)
            {
                WriteChar('-');
                WriteUInt((ulong)(-value));
            }
            else
            {
                if (value > 0) WriteChar('+');
                WriteUInt((ulong)value);
            }
        }

        public static void WriteUInt(ulong value)
        {
            if (value == 0)
            {
                WriteChar('0');
                return;
            }
            ulong divisor = 1;
            ulong temp = value;
            while (temp >= 10)
            {
                divisor *= 10;
                temp /= 10;
            }
            while (divisor > 0)
            {
                byte digit = (byte)((value / divisor) % 10);
                WriteChar((char)('0' + digit));
                divisor /= 10;
            }
        }

        public static void SampleIfDue(long nowTicks, long frequency)
        {
            if (frequency <= 0) return;
            long intervalTicks = frequency * 10L; // 10 seconds cadence
            if (lastSampleTicks >= 0 && (nowTicks - lastSampleTicks) < intervalTicks)
                return;

            lastSampleTicks = nowTicks;

            try
            {
                ulong totalPages = PageAllocator.TotalPageCount;
                ulong freePages = PageAllocator.FreePageCount;
                ulong usedPages = totalPages >= freePages ? totalPages - freePages : 0;
                ulong usedBytes = usedPages * PageAllocator.PageSize;
                ulong usedMb = usedBytes / (1024UL * 1024UL);
                ulong usedHundredths = (usedBytes % (1024UL * 1024UL)) * 100UL / (1024UL * 1024UL);
                ulong uptimeSec = (ulong)(nowTicks / frequency);

                long deltaPages = hasPrevSample ? (long)usedPages - (long)prevUsedPages : 0;
                prevUsedPages = usedPages;
                hasPrevSample = true;

                ulong gcHeapKb = 0, gcCommKb = 0, gcFragKb = 0;
                ulong gcGen = 0, gcFreed = 0;
                try
                {
                    if (CosmosGc.IsEnabled)
                    {
                        gcHeapKb = CosmosGc.GetHeapSizeBytes() / 1024UL;
                        gcCommKb = CosmosGc.GetTotalCommittedBytes() / 1024UL;
                        gcFragKb = CosmosGc.GetFragmentedBytes() / 1024UL;
                        int g = CosmosGc.GetCollectionIndex();
                        gcGen = g > 0 ? (ulong)g : 0;
                        int f = CosmosGc.GetTotalObjectsFreed();
                        gcFreed = f > 0 ? (ulong)f : 0;
                    }
                }
                catch { }

                ulong pGc = 0, pRat = 0, pSys = 0;
                try
                {
                    ulong pEmpty, pSmall, pMed, pLarge, pUnman, pDir, pAlloc, pSmt, pExt, pUnk;
                    PageAllocator.GetPageCountsByType(
                        out pEmpty, out pGc, out pSmall, out pMed, out pLarge,
                        out pUnman, out pDir, out pAlloc, out pSmt, out pExt, out pUnk);
                    pRat = pSmall + pMed + pLarge;
                    pSys = pUnman + pDir + pAlloc + pSmt + pExt + pUnk;
                }
                catch { }

                WriteAscii("[MEM_SAMPLE] sec=");
                WriteUInt(uptimeSec);
                WriteAscii(" total_p=");
                WriteUInt(totalPages);
                WriteAscii(" free_p=");
                WriteUInt(freePages);
                WriteAscii(" used_p=");
                WriteUInt(usedPages);
                WriteAscii(" used_mb=");
                WriteUInt(usedMb);
                WriteChar('.');
                if (usedHundredths < 10) WriteChar('0');
                WriteUInt(usedHundredths);
                WriteAscii(" pool_mb=");
                WriteUInt(totalPages * PageAllocator.PageSize / (1024UL * 1024UL));
                WriteAscii(" gc_heap_kb=");
                WriteUInt(gcHeapKb);
                WriteAscii(" gc_comm_kb=");
                WriteUInt(gcCommKb);
                WriteAscii(" gc_frag_kb=");
                WriteUInt(gcFragKb);
                WriteAscii(" gc_gen=");
                WriteUInt(gcGen);
                WriteAscii(" gc_freed=");
                WriteUInt(gcFreed);
                ulong threadCount = 0;
                try { threadCount = (ulong)Math.Max(0, SchedulerDiagnostics.ThreadCount); } catch { }

                WriteAscii(" p_gc=");
                WriteUInt(pGc);
                WriteAscii(" p_rat=");
                WriteUInt(pRat);
                WriteAscii(" p_sys=");
                WriteUInt(pSys);
                WriteAscii(" delta_p=");
                WriteInt(deltaPages);
                WriteAscii(" threads=");
                WriteUInt(threadCount);

                // Optimizer counters are numeric/readonly and do not allocate
                // strings or touch FAT32. Candidate bytes are GC-eligible cache
                // references, NOT proof of returned PageAllocator pages.
                var optimizer = global::ZonderqOS.SystemCore.MemoryOptimizer.GetSnapshot();
                WriteAscii(" mo_checks=");
                WriteUInt((ulong)optimizer.Samples);
                WriteAscii(" mo_req=");
                WriteUInt((ulong)optimizer.Requests);
                WriteAscii(" mo_sweep=");
                WriteUInt((ulong)optimizer.Sweeps);
                WriteAscii(" mo_detached=");
                WriteUInt((ulong)optimizer.DetachedBuffers);
                WriteAscii(" mo_candidate_kb=");
                WriteUInt(optimizer.CandidateBytes / 1024UL);
                WriteChar('\n');
            }
            catch { }
        }
    }
}
