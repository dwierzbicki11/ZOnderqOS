using System;
using System.Drawing;
using System.Diagnostics;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;

internal static class RoundedPanelSmoke
{
    private static void Fill(Canvas c, Color color, int x, int y, int w, int h)
    {
        if (w <= 0 || h <= 0 || x >= c.Width || y >= c.Height)
            return;
        long right = (long)x + w;
        long bottom = (long)y + h;
        if (right <= 0 || bottom <= 0)
            return;
        int left = Math.Max(0, x);
        int top = Math.Max(0, y);
        int endX = (int)Math.Min(c.Width, right);
        int endY = (int)Math.Min(c.Height, bottom);
        if (endX > left && endY > top)
            c.DrawFilledRectangle(color, left, top, endX - left, endY - top);
    }

    private static int Inset(int radius, int row)
    {
        int delta = radius - row - 1;
        int horizontal = (int)Math.Sqrt(radius * radius - delta * delta);
        return radius - horizontal;
    }

    // Baseline from main before border-inset reuse, independent of UiPainter.
    private static void OriginalPanel(Canvas c, int x, int y, int w, int h,
        int radius, Color fill, Color border)
    {
        if (c == null || w <= 0 || h <= 0)
            return;
        int r = Math.Min(Math.Min(radius, w / 2), h / 2);
        if (r <= 0)
        {
            Fill(c, fill, x, y, w, h);
            Fill(c, border, x, y, w, 1);
            Fill(c, border, x, y + h - 1, w, 1);
            Fill(c, border, x, y, 1, h);
            Fill(c, border, x + w - 1, y, 1, h);
            return;
        }

        Fill(c, fill, x, y + r, w, h - 2 * r);
        for (int row = 0; row < r; row++)
        {
            int inset = Inset(r, row);
            Fill(c, fill, x + inset, y + row, w - 2 * inset, 1);
            Fill(c, fill, x + inset, y + h - row - 1, w - 2 * inset, 1);
        }

        Fill(c, border, x + r, y, w - 2 * r, 1);
        Fill(c, border, x + r, y + h - 1, w - 2 * r, 1);
        Fill(c, border, x, y + r, 1, h - 2 * r);
        Fill(c, border, x + w - 1, y + r, 1, h - 2 * r);

        for (int row = 0; row < r; row++)
        {
            int inset = Inset(r, row);
            int next = row + 1 < r ? Inset(r, row + 1) : 0;
            int edge = Math.Max(1, inset - next + 1);
            Fill(c, border, x + inset, y + row, edge, 1);
            Fill(c, border, x + w - inset - edge, y + row, edge, 1);
            Fill(c, border, x + inset, y + h - row - 1, edge, 1);
            Fill(c, border, x + w - inset - edge, y + h - row - 1, edge, 1);
        }
    }

    private static double TimePanel(Canvas canvas, bool optimized, int repetitions)
    {
        Color fill = Color.FromArgb(22, 33, 44);
        Color border = Color.FromArgb(200, 120, 50);
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repetitions; i++)
        {
            if (optimized)
                UiPainter.RoundedPanel(canvas, 10, 10, 60, 50, 12, fill, border);
            else
                OriginalPanel(canvas, 10, 10, 60, 50, 12, fill, border);
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    internal static void Verify()
    {
        Color fill = Color.FromArgb(22, 33, 44);
        Color border = Color.FromArgb(200, 120, 50);
        int checks = 0;
        foreach (int x in new int[] { -5, 0, 15, 118 })
        foreach (int y in new int[] { -4, 0, 12, 87 })
        foreach (int radius in new int[] { 0, 1, 2, 4, 8, 12, 20 })
        {
            Canvas oldCanvas = new Canvas(128, 96);
            Canvas newCanvas = new Canvas(128, 96);
            OriginalPanel(oldCanvas, x, y, 45, 34, radius, fill, border);
            UiPainter.RoundedPanel(newCanvas, x, y, 45, 34, radius, fill, border);
            for (int i = 0; i < oldCanvas.Pixels.Length; i++)
                if (oldCanvas.Pixels[i] != newCanvas.Pixels[i])
                    throw new Exception("RoundedPanel framebuffer mismatch x=" + x +
                        " y=" + y + " radius=" + radius + " pixel=" + i);
            checks++;
        }
        Console.WriteLine("RoundedPanel: " + checks + " exact raster comparisons including clipped edges");

        Canvas baseline = new Canvas(128, 96);
        Canvas optimized = new Canvas(128, 96);
        OriginalPanel(baseline, 10, 10, 60, 50, 12, fill, border);
        UiPainter.RoundedPanel(optimized, 10, 10, 60, 50, 12, fill, border);
        if (baseline.DrawCalls != 77 || optimized.DrawCalls != 69)
            throw new Exception("RoundedPanel Canvas call regression: " +
                baseline.DrawCalls + " -> " + optimized.DrawCalls);
        Console.WriteLine("RoundedPanel radius 12: Canvas calls 77 -> 69; exact raster");

        // Host-side comparison only. The fake Canvas is not a Cosmos GPU.
        TimePanel(baseline, false, 2000);
        TimePanel(optimized, true, 2000);
        const int iterations = 10000;
        double[] baselineSamples = new double[5];
        double[] optimizedSamples = new double[5];
        for (int sample = 0; sample < baselineSamples.Length; sample++)
        {
            if ((sample & 1) == 0)
            {
                baselineSamples[sample] = TimePanel(baseline, false, iterations);
                optimizedSamples[sample] = TimePanel(optimized, true, iterations);
            }
            else
            {
                optimizedSamples[sample] = TimePanel(optimized, true, iterations);
                baselineSamples[sample] = TimePanel(baseline, false, iterations);
            }
        }
        Array.Sort(baselineSamples);
        Array.Sort(optimizedSamples);
        Console.WriteLine("Host .NET RoundedPanel " + iterations +
            " renders, median of 5 A/B samples (informational): baseline " +
            baselineSamples[2].ToString("F2") + " ms, optimized " +
            optimizedSamples[2].ToString("F2") + " ms; no QEMU FPS claim.");
    }
}
