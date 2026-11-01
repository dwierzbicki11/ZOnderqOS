using System;
using System.Diagnostics;
using ZonderqOS.GUI;

// Compares the production WidthUInt/DigitCount path against the original
// division loop on the same .NET host. Not a kernel throughput measurement.
internal static class NumericDigitCountSmoke
{
    private static volatile int sink;

    private static int PreviousWidthUInt(ulong value)
    {
        int count = 1;
        while (value >= 10)
        {
            value /= 10;
            count++;
        }
        return count * 6 - 1;
    }

    private static void Check(ulong value)
    {
        int before = PreviousWidthUInt(value);
        int after = SmallTextRenderer.WidthUInt(value);
        if (before != after)
            throw new Exception("Decimal width mismatch: " + value + " (" +
                before + " != " + after + ")");
    }

    private static double Time(bool optimized, int scenario, int repeats)
    {
        int checksum = 0;
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++)
        {
            ulong value;
            if (scenario == 0)
                value = (ulong)(i % 100);
            else if (scenario == 1)
                value = 12345678UL + (ulong)(i % 1000);
            else if (scenario == 2)
                value = ulong.MaxValue - (ulong)(i % 1000);
            else
                value = unchecked((ulong)i * 11400714819323198485UL);
            int width = optimized ? SmallTextRenderer.WidthUInt(value) :
                PreviousWidthUInt(value);
            checksum = unchecked(checksum + width);
        }
        sink = checksum;
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    internal static void Verify()
    {
        ulong[] powers = { 10UL, 100UL, 1000UL, 10000UL, 100000UL,
            1000000UL, 10000000UL, 100000000UL, 1000000000UL,
            10000000000UL, 100000000000UL, 1000000000000UL,
            10000000000000UL, 100000000000000UL, 1000000000000000UL,
            10000000000000000UL, 100000000000000000UL,
            1000000000000000000UL, 10000000000000000000UL };
        Check(0);
        Check(ulong.MaxValue);
        foreach (ulong power in powers)
        {
            Check(power - 1);
            Check(power);
            if (power < ulong.MaxValue)
                Check(power + 1);
        }
        ulong state = 0xD1B54A32D192ED03UL;
        for (int i = 0; i < 100000; i++)
        {
            state = unchecked(state * 6364136223846793005UL + 1442695040888963407UL);
            Check(state);
        }
        if (SmallTextRenderer.WidthInt(long.MinValue) != 20 * 6 - 1 ||
            SmallTextRenderer.WidthInt(-1) != 2 * 6 - 1)
            throw new Exception("Signed decimal width regression");

        // Allocation checks use only production WidthUInt after JIT warmup.
        for (int i = 0; i < 1000; i++)
            sink = SmallTextRenderer.WidthUInt((ulong)i);
        long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            sink = SmallTextRenderer.WidthUInt((ulong)i);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAlloc;
        if (allocated != 0)
            throw new Exception("Decimal width allocated " + allocated + " B");

        for (int scenario = 0; scenario < 4; scenario++)
        {
            // 0..99 takes sub-millisecond time at 250k calls, so give that
            // scenario a longer sample to reduce timer noise.
            int repetitions = scenario == 0 ? 2500000 : 250000;
            Time(false, scenario, 10000);
            Time(true, scenario, 10000);
            double[] before = new double[5];
            double[] after = new double[5];
            for (int sample = 0; sample < 5; sample++)
            {
                if ((sample & 1) == 0)
                {
                    before[sample] = Time(false, scenario, repetitions);
                    after[sample] = Time(true, scenario, repetitions);
                }
                else
                {
                    after[sample] = Time(true, scenario, repetitions);
                    before[sample] = Time(false, scenario, repetitions);
                }
            }
            Array.Sort(before);
            Array.Sort(after);
            Console.WriteLine("Host .NET decimal digit count " +
                (scenario == 0 ? "0..99" : scenario == 1 ? "8 digits" :
                scenario == 2 ? "20 digits" : "mixed 64-bit") +
                ": " + repetitions + " calls, median 5 alternating A/B; before " +
                before[2].ToString("F2") + " ms, after " +
                after[2].ToString("F2") + " ms (not QEMU FPS).");
        }
        Console.WriteLine("Decimal digit count: 100059 exact widths and 0 B allocations.");
    }
}
