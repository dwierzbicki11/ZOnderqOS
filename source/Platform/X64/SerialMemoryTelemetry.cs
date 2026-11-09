using System;
using Cosmos.Kernel.Core.Memory;

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
                WriteChar('\n');
            }
            catch { }
        }
    }
}
