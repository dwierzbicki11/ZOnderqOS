using System;
using System.Diagnostics;
using ZonderqOS.GUI;

internal static class CornerInsetSmoke
{
    private static long sink;

    private static int OriginalInset(int radius, int row)
    {
        int delta = radius - row - 1;
        int horizontal = (int)Math.Sqrt(radius * radius - delta * delta);
        return radius - horizontal;
    }

    private static double TimeNanosecondsPerCall(bool cached, int[] radii, int[] rows, int repetitions)
    {
        long checksum = 0;
        long start = Stopwatch.GetTimestamp();
        for (int repeat = 0; repeat < repetitions; repeat++)
        {
            for (int i = 0; i < radii.Length; i++)
                checksum += cached
                    ? UiPainter.CornerInset(radii[i], rows[i])
                    : OriginalInset(radii[i], rows[i]);
        }
        long ticks = Stopwatch.GetTimestamp() - start;
        sink = checksum;
        return ticks * 1_000_000_000.0 / Stopwatch.Frequency / (radii.Length * (long)repetitions);
    }

    internal static void Verify()
    {
        int checks = 0;
        // Both the 528 cached entries and the >32-radius fallback must
        // match the pre-optimization production formula bit for bit.
        for (int radius = 1; radius <= 128; radius++)
        {
            for (int row = 0; row < radius; row++)
            {
                int expected = OriginalInset(radius, row);
                int actual = UiPainter.CornerInset(radius, row);
                if (actual != expected)
                    throw new Exception("Corner inset mismatch radius=" + radius + " row=" + row);
                checks++;
            }
        }

        int[] radii = new int[528];
        int[] rows = new int[528];
        int index = 0;
        for (int radius = 1; radius <= 32; radius++)
            for (int row = 0; row < radius; row++)
            {
                radii[index] = radius;
                rows[index++] = row;
            }

        // Warm both paths before measurement. Timing is informational and
        // never a flaky pass/fail gate; this is host .NET, not QEMU/Cosmos.
        TimeNanosecondsPerCall(false, radii, rows, 50);
        TimeNanosecondsPerCall(true, radii, rows, 50);
        double baseline = TimeNanosecondsPerCall(false, radii, rows, 4000);
        double optimized = TimeNanosecondsPerCall(true, radii, rows, 4000);
        Console.WriteLine("CornerInset: " + checks + " exact geometry checks; cache 528 bytes");
        Console.WriteLine("Host .NET corner lookup (informational): baseline sqrt " +
            baseline.ToString("F2") + " ns/call, cached " +
            optimized.ToString("F2") + " ns/call; checksum=" + sink);
    }
}
