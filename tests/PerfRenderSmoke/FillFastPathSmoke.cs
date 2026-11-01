using System;
using System.Diagnostics;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;

// Isolate the new fully-visible fill shortcut from other panel optimizations.
// UiPainterLegacy is the previous production Fill implementation.
internal static class FillFastPathSmoke
{
    private static readonly Color Ink = Color.FromArgb(51, 104, 166);

    private static void Draw(bool optimized, Canvas canvas, int x, int y,
        int width, int height)
    {
        if (optimized)
            UiPainter.RoundedFill(canvas, x, y, width, height, 0, Ink);
        else
            UiPainterLegacy.RoundedFill(canvas, x, y, width, height, 0, Ink);
    }

    private static double Time(bool optimized, Canvas canvas, int count,
        bool clipped)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < count; i++)
        {
            if (clipped)
                Draw(optimized, canvas, -2, 12, 6, 5);
            else
                Draw(optimized, canvas, 12, 12, 6, 5);
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 /
            Stopwatch.Frequency / count;
    }

    internal static void Verify()
    {
        int checks = 0;
        int[] positions = { int.MinValue, -150, -8, -1, 0, 12, 120, 127, int.MaxValue };
        int[] sizes = { 0, 1, 5, 20, 128, 512, int.MaxValue };
        foreach (int x in positions)
            foreach (int y in positions)
                foreach (int width in sizes)
                    foreach (int height in new int[] { 0, 1, 5, 128 })
                    {
                        Canvas oldCanvas = new Canvas(128, 96);
                        Canvas newCanvas = new Canvas(128, 96);
                        Draw(false, oldCanvas, x, y, width, height);
                        Draw(true, newCanvas, x, y, width, height);
                        for (int i = 0; i < oldCanvas.Pixels.Length; i++)
                            if (oldCanvas.Pixels[i] != newCanvas.Pixels[i])
                                throw new Exception("fill shortcut pixel mismatch x=" + x +
                                    " y=" + y + " width=" + width + " height=" + height +
                                    " pixel=" + i);
                        checks++;
                    }

        Canvas baseline = new Canvas(128, 96);
        Canvas optimized = new Canvas(128, 96);
        for (int mode = 0; mode < 2; mode++)
        {
            bool clipped = mode != 0;
            Time(false, baseline, 1000, clipped);
            Time(true, optimized, 1000, clipped);
            double[] oldMs = new double[7], newMs = new double[7];
            for (int sample = 0; sample < 7; sample++)
            {
                if ((sample & 1) == 0)
                {
                    oldMs[sample] = Time(false, baseline, 30000, clipped);
                    newMs[sample] = Time(true, optimized, 30000, clipped);
                }
                else
                {
                    newMs[sample] = Time(true, optimized, 30000, clipped);
                    oldMs[sample] = Time(false, baseline, 30000, clipped);
                }
            }
            Array.Sort(oldMs);
            Array.Sort(newMs);
            Console.WriteLine("HOST A/B fill " + (clipped ? "clipped" : "fully visible") +
                " 6x5: previous=" + oldMs[3].ToString("F7") +
                " ms current=" + newMs[3].ToString("F7") +
                " ms per call, ratio=" + (oldMs[3] / newMs[3]).ToString("F2") +
                "x (fake Canvas, not QEMU FPS)");
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++)
            Draw(true, optimized, 12, 12, 6, 5);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (bytes != 0)
            throw new Exception("visible fill shortcut allocated " + bytes + " B");
        Console.WriteLine("PASS: fill shortcut " + checks +
            " exact framebuffer cases; warm allocations=" + bytes + " B");
    }
}
