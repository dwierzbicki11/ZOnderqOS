using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;

// Host-side A/B for the exact pre-fast-path 5x7 span loop. Both paths use
// production glyph masks; only the empty-glyph/empty-row early exits differ.
// Timings are informational, not a Cosmos/QEMU FPS or latency measurement.
internal static class TextWhitespaceSmoke
{
    private static readonly byte[] AsciiRows = (byte[])typeof(SmallTextRenderer)
        .GetField("CachedAsciiGlyphRows", BindingFlags.NonPublic | BindingFlags.Static)
        .GetValue(null);

    private static void BaselineDrawAscii(Canvas canvas, string text, int x, int y, Color color)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i] == '\u00A0' ? ' ' : text[i];
            if (ch >= 128)
                throw new Exception("Baseline accepts ASCII and NBSP only");
            int offset = ch * 7;
            for (int row = 0; row < 7; row++)
            {
                int bits = AsciiRows[offset + row];
                for (int col = 0; col < 5;)
                {
                    if ((bits & (1 << (4 - col))) == 0)
                    {
                        col++;
                        continue;
                    }
                    int runStart = col++;
                    while (col < 5 && (bits & (1 << (4 - col))) != 0)
                        col++;
                    canvas.DrawFilledRectangle(color, x + i * 6 + runStart,
                        y + row, col - runStart, 1);
                }
            }
        }
    }

    private static void CheckPixels(string label, string text)
    {
        Canvas before = new Canvas(1024, 24);
        Canvas after = new Canvas(1024, 24);
        BaselineDrawAscii(before, text, 2, 3, Color.White);
        SmallTextRenderer.Draw(after, text, 2, 3, Color.White);
        if (before.DrawCalls != after.DrawCalls)
            throw new Exception(label + ": changed draw-call count " +
                before.DrawCalls + " -> " + after.DrawCalls);
        for (int i = 0; i < before.Pixels.Length; i++)
            if (before.Pixels[i] != after.Pixels[i])
                throw new Exception(label + ": framebuffer mismatch at " + i);
    }

    private static void Compare(Canvas baseline, Canvas optimized, string label)
    {
        if (baseline.DrawCalls != optimized.DrawCalls)
            throw new Exception(label + ": changed Canvas call count");
        for (int i = 0; i < baseline.Pixels.Length; i++)
            if (baseline.Pixels[i] != optimized.Pixels[i])
                throw new Exception(label + ": framebuffer mismatch at " + i);
    }

    private static void CheckAlternateEntryPoints()
    {
        const string text = "    ABC   DEF    ";
        Canvas before = new Canvas(512, 24);
        Canvas after = new Canvas(512, 24);
        BaselineDrawAscii(before, text.Substring(2, 9), 2, 3, Color.White);
        SmallTextRenderer.DrawRange(after, text, 2, 9, 2, 3, Color.White);
        Compare(before, after, "DrawRange");

        before = new Canvas(512, 24);
        after = new Canvas(512, 24);
        const int width = 59; // ten glyph slots, seven characters + ellipsis
        BaselineDrawAscii(before, text.Substring(0, 7) + "...", 2, 3, Color.White);
        SmallTextRenderer.DrawClipped(after, text, 2, 3, width, Color.White);
        Compare(before, after, "DrawClipped");

        before = new Canvas(512, 24);
        after = new Canvas(512, 24);
        int offset = Math.Max(0, (200 - SmallTextRenderer.Width(text)) / 2);
        BaselineDrawAscii(before, text, 2 + offset, 3, Color.White);
        SmallTextRenderer.DrawCentered(after, text, 2, 3, 200, Color.White);
        Compare(before, after, "DrawCentered");
        Console.WriteLine("Whitespace text: 3 extra exact entry-point raster checks");
    }

    private static double Time(Canvas canvas, string text, bool optimized, int repetitions)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repetitions; i++)
        {
            if (optimized)
                SmallTextRenderer.Draw(canvas, text, 2, 3, Color.White);
            else
                BaselineDrawAscii(canvas, text, 2, 3, Color.White);
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    private static void Benchmark(string label, string text)
    {
        Canvas before = new Canvas(1024, 24);
        Canvas after = new Canvas(1024, 24);
        Time(before, text, false, 300);
        Time(after, text, true, 300);
        const int iterations = 3000;
        double[] baseline = new double[5];
        double[] optimized = new double[5];
        for (int sample = 0; sample < 5; sample++)
        {
            if ((sample & 1) == 0)
            {
                baseline[sample] = Time(before, text, false, iterations);
                optimized[sample] = Time(after, text, true, iterations);
            }
            else
            {
                optimized[sample] = Time(after, text, true, iterations);
                baseline[sample] = Time(before, text, false, iterations);
            }
        }
        Array.Sort(baseline);
        Array.Sort(optimized);
        Console.WriteLine("Host .NET text " + label + ": " + iterations +
            " renders, median 5 alternating A/B; baseline " +
            baseline[2].ToString("F2") + " ms, current " +
            optimized[2].ToString("F2") + " ms; informational, not QEMU.");

        // After warm-up, report actual thread-local allocations from the
        // production path; do not turn noisy runtime metrics into CI gates.
        SmallTextRenderer.Draw(after, text, 2, 3, Color.White);
        long allocationStart = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
            SmallTextRenderer.Draw(after, text, 2, 3, Color.White);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
        Console.WriteLine("Host .NET text " + label + ": allocated " +
            allocated + " bytes / " + iterations +
            " production Draw calls (informational).");
    }

    internal static void Verify()
    {
        CheckPixels("spaces", "                                  ");
        CheckPixels("mixed", "GUI    file manager    800x600");
        CheckPixels("NBSP", "A\u00A0 B\u00A0C");
        CheckPixels("punctuation", "!!! ... !!!");
        CheckAlternateEntryPoints();
        Console.WriteLine("Whitespace text: 4 exact framebuffer/call-count checks");
        Benchmark("spaces", new string(' ', 120));
        Benchmark("mixed", "CPU     RAM      I/O     GUI     FPS     Task Manager");
        Benchmark("punctuation", "...  !!!  ...  !!!  ...");
    }
}
