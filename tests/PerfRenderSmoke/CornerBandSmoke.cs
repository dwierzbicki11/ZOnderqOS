using System;
using System.Drawing;
using System.Diagnostics;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;

internal static class CornerBandSmoke
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

    private static int OriginalInset(int radius, int row)
    {
        int delta = radius - row - 1;
        return radius - (int)Math.Sqrt(radius * radius - delta * delta);
    }

    private static void Original(Canvas canvas, int x, int y, int width, int height,
        int radius, Color color, bool topOnly)
    {
        if (canvas == null || width <= 0 || height <= 0)
            return;
        int r = Math.Min(Math.Min(radius, width / 2), topOnly ? height : height / 2);
        if (r <= 0)
        {
            Fill(canvas, color, x, y, width, height);
            return;
        }
        Fill(canvas, color, x, y + r, width, height - (topOnly ? r : 2 * r));
        for (int row = 0; row < r; row++)
        {
            int inset = OriginalInset(r, row);
            Fill(canvas, color, x + inset, y + row, width - 2 * inset, 1);
            if (!topOnly)
                Fill(canvas, color, x + inset, y + height - row - 1, width - 2 * inset, 1);
        }
    }

    private static void Check(int x, int y, int width, int height, int radius, bool topOnly)
    {
        Color ink = Color.FromArgb(26, 65, 117);
        Canvas oldCanvas = new Canvas(128, 96);
        Canvas newCanvas = new Canvas(128, 96);
        Original(oldCanvas, x, y, width, height, radius, ink, topOnly);
        if (topOnly)
            UiPainter.RoundedTop(newCanvas, x, y, width, height, radius, ink);
        else
            UiPainter.RoundedFill(newCanvas, x, y, width, height, radius, ink);
        for (int i = 0; i < oldCanvas.Pixels.Length; i++)
            if (oldCanvas.Pixels[i] != newCanvas.Pixels[i])
                throw new Exception("Corner band raster mismatch at " + i +
                    " radius=" + radius + " topOnly=" + topOnly);
    }

    private static double TimeFill(Canvas canvas, bool optimized, int repetitions)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repetitions; i++)
        {
            if (optimized)
                UiPainter.RoundedFill(canvas, 10, 10, 60, 50, 12, Color.White);
            else
                Original(canvas, 10, 10, 60, 50, 12, Color.White, false);
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    internal static void Verify()
    {
        int checks = 0;
        foreach (int x in new int[] { -5, 0, 15, 118 })
        foreach (int y in new int[] { -4, 0, 12, 87 })
        foreach (int radius in new int[] { 0, 1, 2, 4, 8, 12, 20, 33, 48 })
        foreach (bool topOnly in new bool[] { false, true })
        {
            Check(x, y, 45, 34, radius, topOnly);
            checks++;
        }
        Check(0, 0, 80, 80, 40, false);
        Check(0, 0, 80, 80, 40, true);
        checks += 2;

        Color ink = Color.White;
        Canvas oldFill = new Canvas(128, 96);
        Canvas newFill = new Canvas(128, 96);
        Original(oldFill, 10, 10, 60, 50, 12, ink, false);
        UiPainter.RoundedFill(newFill, 10, 10, 60, 50, 12, ink);
        if (oldFill.DrawCalls != 25 || newFill.DrawCalls != 17)
            throw new Exception("RoundedFill Canvas call regression: " +
                oldFill.DrawCalls + " -> " + newFill.DrawCalls);

        Canvas oldTop = new Canvas(128, 96);
        Canvas newTop = new Canvas(128, 96);
        Original(oldTop, 10, 10, 60, 50, 12, ink, true);
        UiPainter.RoundedTop(newTop, 10, 10, 60, 50, 12, ink);
        if (oldTop.DrawCalls != 13 || newTop.DrawCalls != 9)
            throw new Exception("RoundedTop Canvas call regression: " +
                oldTop.DrawCalls + " -> " + newTop.DrawCalls);
        Console.WriteLine("Corner bands: " + checks + " exact raster comparisons; " +
            "radius 12 RoundedFill calls 25 -> 17; RoundedTop calls 13 -> 9");

        // Measure actual host execution of the production C# RoundedFill with
        // a test Canvas, against the original raster algorithm. This is NOT
        // Cosmos/QEMU frame time; never gate CI on noisy wall-clock timings.
        Canvas baselineCanvas = new Canvas(128, 96);
        Canvas optimizedCanvas = new Canvas(128, 96);
        TimeFill(baselineCanvas, false, 200);
        TimeFill(optimizedCanvas, true, 200);
        const int iterations = 10000;
        double baselineMs = TimeFill(baselineCanvas, false, iterations);
        double optimizedMs = TimeFill(optimizedCanvas, true, iterations);
        Console.WriteLine("Host .NET RoundedFill " + iterations + " renders (informational): " +
            "baseline " + baselineMs.ToString("F2") + " ms, optimized " +
            optimizedMs.ToString("F2") + " ms; no QEMU FPS claim.");
    }
}
