using System;
using System.Diagnostics;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;

// A/B with the exact pre-cull DrawClipped control flow. Both versions call
// production Draw/DrawRange, so the benchmark isolates the new outer guard.
// No synthetic FPS claims: this is host .NET with a fake Canvas only.
internal static class TextClippedVisibilitySmoke
{
    private static void OldClipped(Canvas canvas, string text, int x, int y, int maxWidth, Color color)
    {
        if (string.IsNullOrEmpty(text) || maxWidth < 5)
            return;
        int maxChars = Math.Max(1, (maxWidth + 1) / 6);
        if (text.Length <= maxChars)
        {
            SmallTextRenderer.Draw(canvas, text, x, y, color);
            return;
        }
        if (maxChars < 4)
        {
            SmallTextRenderer.DrawRange(canvas, text, 0, maxChars, x, y, color);
            return;
        }
        int count = maxChars - 3;
        SmallTextRenderer.DrawRange(canvas, text, 0, count, x, y, color);
        SmallTextRenderer.Draw(canvas, "...", x + count * 6, y, color);
    }

    private static void Compare(Canvas expected, Canvas actual, string label)
    {
        if (expected.DrawCalls != actual.DrawCalls)
            throw new Exception(label + ": Canvas call count changed");
        for (int i = 0; i < expected.Pixels.Length; i++)
            if (expected.Pixels[i] != actual.Pixels[i])
                throw new Exception(label + ": framebuffer mismatch at " + i);
    }

    private static double Time(bool optimized, Canvas canvas, string text,
        int x, int y, int maxWidth, int repeats)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++)
        {
            if (optimized)
                SmallTextRenderer.DrawClipped(canvas, text, x, y, maxWidth, Color.White);
            else
                OldClipped(canvas, text, x, y, maxWidth, Color.White);
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    private static void Benchmark(bool visible, string text)
    {
        int x = visible ? 2 : 128;
        int y = 4;
        int maxWidth = 80;
        int repeats = visible ? 2500 : 100000;
        Canvas beforeFrame = new Canvas(128, 48);
        Canvas afterFrame = new Canvas(128, 48);
        Time(false, beforeFrame, text, x, y, maxWidth, 300);
        Time(true, afterFrame, text, x, y, maxWidth, 300);
        double[] before = new double[5];
        double[] after = new double[5];
        for (int i = 0; i < 5; i++)
        {
            if ((i & 1) == 0)
            {
                before[i] = Time(false, beforeFrame, text, x, y, maxWidth, repeats);
                after[i] = Time(true, afterFrame, text, x, y, maxWidth, repeats);
            }
            else
            {
                after[i] = Time(true, afterFrame, text, x, y, maxWidth, repeats);
                before[i] = Time(false, beforeFrame, text, x, y, maxWidth, repeats);
            }
        }
        Array.Sort(before);
        Array.Sort(after);
        Compare(beforeFrame, afterFrame, "Clipped label A/B");
        Console.WriteLine("Host .NET pre-cull -> current DrawClipped " +
            (visible ? "visible: " : "offscreen: ") + repeats +
            " calls, median 5 alternating A/B " +
            before[2].ToString("F2") + " -> " + after[2].ToString("F2") +
            " ms (fake Canvas, NOT QEMU/FPS).");
    }

    internal static void Verify()
    {
        const string text = "ZonderqOS Gen3 file manager / performance";
        int[] xs = { -150, -80, -1, 0, 2, 127, 128 };
        int[] ys = { -8, -7, -6, 0, 42, 47, 48 };
        int[] widths = { 5, 6, 11, 17, 18, 23, 24, 59, 60, 120, 1000 };
        int checks = 0;
        foreach (int x in xs)
            foreach (int y in ys)
                foreach (int width in widths)
                {
                    Canvas before = new Canvas(128, 48);
                    Canvas after = new Canvas(128, 48);
                    OldClipped(before, text, x, y, width, Color.White);
                    SmallTextRenderer.DrawClipped(after, text, x, y, width, Color.White);
                    Compare(before, after, "DrawClipped x=" + x +
                        " y=" + y + " width=" + width);
                    checks++;
                }

        Canvas wide = new Canvas(128, 48);
        Canvas expectedWide = new Canvas(128, 48);
        SmallTextRenderer.DrawClipped(wide, "ABCD", 0, 0, int.MaxValue, Color.White);
        SmallTextRenderer.Draw(expectedWide, "ABCD", 0, 0, Color.White);
        Compare(expectedWide, wide, "int.MaxValue clipping budget");

        Canvas extreme = new Canvas(128, 48);
        SmallTextRenderer.DrawClipped(extreme, text, int.MinValue, 0, 80, Color.White);
        SmallTextRenderer.DrawClipped(extreme, text, int.MaxValue, 0, 80, Color.White);
        SmallTextRenderer.DrawClipped(extreme, text, 0, int.MinValue, 80, Color.White);
        SmallTextRenderer.DrawClipped(extreme, text, 0, int.MaxValue, 80, Color.White);
        SmallTextRenderer.DrawClipped(null, text, 0, 0, 80, Color.White);
        if (extreme.DrawCalls != 0)
            throw new Exception("Extreme offscreen label reached Canvas");

        Benchmark(false, text);
        Benchmark(true, text);

        Canvas allocationFrame = new Canvas(128, 48);
        SmallTextRenderer.DrawClipped(allocationFrame, text, 128, 0, 80, Color.White);
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++)
            SmallTextRenderer.DrawClipped(allocationFrame, text, 128, 0, 80, Color.White);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        if (allocated != 0)
            throw new Exception("Offscreen clipped labels allocated " + allocated + " bytes");

        Console.WriteLine("Clipped labels: " + checks +
            " exact framebuffer+Canvas-call comparisons; wide width and extreme coordinates; " +
            "0 B warmed offscreen allocations.");
    }
}
