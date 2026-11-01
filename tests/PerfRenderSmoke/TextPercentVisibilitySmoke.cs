using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;

// Executes the real production DrawPercent, including clipping, with a fake
// framebuffer. The baseline calls the same production numeric/glyph routines
// in the pre-change order; it is not a simulated glyph rasterizer.
internal static class TextPercentVisibilitySmoke
{
    private static readonly Action<Canvas, char, int, int, Color> DrawChar =
        (Action<Canvas, char, int, int, Color>)typeof(SmallTextRenderer)
            .GetMethod("DrawChar", BindingFlags.Static | BindingFlags.NonPublic)
            .CreateDelegate(typeof(Action<Canvas, char, int, int, Color>));

    private static void Baseline(Canvas canvas, int value, int x, int y, Color color)
    {
        if (value < 0) value = 0;
        if (value > 100) value = 100;
        SmallTextRenderer.DrawUInt(canvas, (ulong)value, x, y, color);
        DrawChar(canvas, '%', x + SmallTextRenderer.WidthUInt((ulong)value) + 2, y, color);
    }

    private static double Time(bool optimized, Canvas frame, int repeats)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++)
        {
            if (optimized)
                SmallTextRenderer.DrawPercent(frame, i % 101, 400, 12, Color.White);
            else
                Baseline(frame, i % 101, 400, 12, Color.White);
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    internal static void Verify()
    {
        int[] values = { -50, 0, 1, 9, 10, 25, 99, 100, 150 };
        int[] positionsX = { -30, -18, -5, 0, 7, 110, 125, 128, 400 };
        int[] positionsY = { -8, -6, 0, 15, 62, 64 };
        int checks = 0;
        foreach (int value in values)
            foreach (int x in positionsX)
                foreach (int y in positionsY)
                {
                    Canvas expected = new Canvas(128, 64);
                    Canvas actual = new Canvas(128, 64);
                    Baseline(expected, value, x, y, Color.White);
                    SmallTextRenderer.DrawPercent(actual, value, x, y, Color.White);
                    for (int i = 0; i < expected.Pixels.Length; i++)
                        if (expected.Pixels[i] != actual.Pixels[i])
                            throw new Exception("Percent raster mismatch: " + value +
                                " at " + x + "," + y + " pixel " + i);
                    checks++;
                }

        Canvas extreme = new Canvas(128, 64);
        SmallTextRenderer.DrawPercent(extreme, 100, int.MaxValue, 0, Color.White);
        SmallTextRenderer.DrawPercent(extreme, 100, int.MinValue, 0, Color.White);
        SmallTextRenderer.DrawPercent(extreme, 100, 0, int.MinValue, Color.White);
        SmallTextRenderer.DrawPercent(null, 100, 0, 0, Color.White);
        if (extreme.DrawCalls != 0)
            throw new Exception("Extreme percent coordinates reached Canvas");

        Canvas beforeFrame = new Canvas(128, 64);
        Canvas afterFrame = new Canvas(128, 64);
        Time(false, beforeFrame, 1000);
        Time(true, afterFrame, 1000);
        const int repeats = 100000;
        double[] before = new double[5];
        double[] after = new double[5];
        for (int sample = 0; sample < 5; sample++)
        {
            if ((sample & 1) == 0)
            {
                before[sample] = Time(false, beforeFrame, repeats);
                after[sample] = Time(true, afterFrame, repeats);
            }
            else
            {
                after[sample] = Time(true, afterFrame, repeats);
                before[sample] = Time(false, beforeFrame, repeats);
            }
        }
        Array.Sort(before);
        Array.Sort(after);
        if (afterFrame.DrawCalls != 0 || beforeFrame.DrawCalls == 0)
            throw new Exception("Offscreen percent benchmark did not exercise clipping");

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            SmallTextRenderer.DrawPercent(afterFrame, i % 101, 400, 12, Color.White);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        if (allocated != 0)
            throw new Exception("Offscreen percent rendering allocated " + allocated + " B");

        Console.WriteLine("Percent text: " + checks +
            " exact framebuffer comparisons, 4 extreme/null guards, 0 B allocations.");
        Console.WriteLine("Host .NET offscreen DrawPercent: " + repeats +
            " calls, median of 5 alternating A/B; original " +
            before[2].ToString("F2") + " ms, culled " +
            after[2].ToString("F2") + " ms (not QEMU FPS).");
    }
}
