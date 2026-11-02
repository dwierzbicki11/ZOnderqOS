using System;
using System.Diagnostics;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;

// Production C# in both paths; the old byte->span lookup is host-only.
// Timing is informational, not a claim about QEMU FPS or kernel CPU idle.
internal static class PackedGlyphSmoke
{
    private const string Sample =
        "CPU 100%  MEM 1024 MiB  TASK MANAGER  Zażółć gęślą jaźń  /root/plik.txt";

    private static void Draw(Canvas canvas, bool previous, string text)
    {
        SmallTextRenderer.UsePreviousGlyphRowLookupForBenchmark = previous;
        SmallTextRenderer.Draw(canvas, text, 3, 5, Color.White);
    }

    private static double Time(Canvas canvas, bool previous, int repetitions,
        out long allocations)
    {
        SmallTextRenderer.UsePreviousGlyphRowLookupForBenchmark = previous;
        long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repetitions; i++)
            SmallTextRenderer.Draw(canvas, Sample, 3, 5, Color.White);
        long ticks = Stopwatch.GetTimestamp() - start;
        allocations = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    internal static void Verify()
    {
        try
        {
            int checks = 0;
            string[] samples = {
                Sample,
                "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789",
                "!@#$%^&*()_+-=[]{};:'\\\",.<>/?  \u00A0\uFFFF",
                "Unicode: ąĄćĆęĘłŁńŃóÓśŚźŹżŻ—–“”‘’"
            };
            foreach (string text in samples)
            {
                Canvas previous = new Canvas(800, 28);
                Canvas current = new Canvas(800, 28);
                Draw(previous, true, text);
                Draw(current, false, text);
                if (previous.DrawCalls != current.DrawCalls)
                    throw new Exception("Packed glyph Canvas-call mismatch");
                for (int i = 0; i < previous.Pixels.Length; i++)
                    if (previous.Pixels[i] != current.Pixels[i])
                        throw new Exception("Packed glyph pixel mismatch: " + i);
                checks++;
            }

            Canvas beforeCanvas = new Canvas(800, 28);
            Canvas afterCanvas = new Canvas(800, 28);
            Time(beforeCanvas, true, 200, out _);
            Time(afterCanvas, false, 200, out _);
            const int repetitions = 3000;
            double[] before = new double[7];
            double[] after = new double[7];
            long beforeBytes = 0;
            long afterBytes = 0;
            for (int i = 0; i < 7; i++)
            {
                if ((i & 1) == 0)
                {
                    before[i] = Time(beforeCanvas, true, repetitions, out beforeBytes);
                    after[i] = Time(afterCanvas, false, repetitions, out afterBytes);
                }
                else
                {
                    after[i] = Time(afterCanvas, false, repetitions, out afterBytes);
                    before[i] = Time(beforeCanvas, true, repetitions, out beforeBytes);
                }
            }
            Array.Sort(before);
            Array.Sort(after);
            if (beforeBytes != 0 || afterBytes != 0)
                throw new Exception("Packed glyph render allocated memory: " +
                    beforeBytes + " -> " + afterBytes);
            Console.WriteLine("PackedGlyph: " + checks + " exact pixel/call comparisons; " +
                "host .NET " + repetitions + " renders, median 7 alternating A/B: " +
                before[3].ToString("F3") + " -> " + after[3].ToString("F3") +
                " ms, allocations 0 -> 0 B (not QEMU FPS).");
        }
        finally
        {
            SmallTextRenderer.UsePreviousGlyphRowLookupForBenchmark = false;
        }
    }
}
