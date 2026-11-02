using System;
using System.Diagnostics;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;

// Exact pre-fast-path Fill implementation, retained solely for host A/B.
// The new implementation is linked directly from production UiPainter.cs.
internal static class FillFastPathSmoke
{
    private static readonly Color Ink = Color.FromArgb(28, 67, 101);
    private static long sink;

    private static void PreviousFill(Canvas canvas, Color color,
        int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0 || x >= canvas.Width || y >= canvas.Height)
            return;
        long right = (long)x + width;
        long bottom = (long)y + height;
        if (right <= 0 || bottom <= 0)
            return;
        int left = Math.Max(0, x);
        int top = Math.Max(0, y);
        int endX = (int)Math.Min(canvas.Width, right);
        int endY = (int)Math.Min(canvas.Height, bottom);
        if (endX > left && endY > top)
            canvas.DrawFilledRectangle(color, left, top, endX - left, endY - top);
    }

    private static void Check(int x, int y, int width, int height)
    {
        Canvas before = new Canvas(48, 32);
        Canvas after = new Canvas(48, 32);
        PreviousFill(before, Ink, x, y, width, height);
        UiPainter.Fill(after, Ink, x, y, width, height);
        if (before.DrawCalls != after.DrawCalls)
            throw new Exception("Fill Canvas-call mismatch x=" + x +
                " y=" + y + " w=" + width + " h=" + height);
        for (int i = 0; i < before.Pixels.Length; i++)
            if (before.Pixels[i] != after.Pixels[i])
                throw new Exception("Fill pixel mismatch at " + i +
                    " x=" + x + " y=" + y + " w=" + width + " h=" + height);
    }

    private static double Time(Canvas canvas, bool current, int x, int y,
        int width, int height, int iterations)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
        {
            if (current)
                UiPainter.Fill(canvas, Ink, x, y, width, height);
            else
                PreviousFill(canvas, Ink, x, y, width, height);
        }
        sink += canvas.DrawCalls;
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    private static void Benchmark(string label, int x, int y, int width, int height)
    {
        const int repetitions = 120000;
        Canvas before = new Canvas(64, 48);
        Canvas after = new Canvas(64, 48);
        Time(before, false, x, y, width, height, 2000);
        Time(after, true, x, y, width, height, 2000);
        double[] oldMs = new double[7];
        double[] newMs = new double[7];
        for (int sample = 0; sample < 7; sample++)
        {
            if ((sample & 1) == 0)
            {
                oldMs[sample] = Time(before, false, x, y, width, height, repetitions);
                newMs[sample] = Time(after, true, x, y, width, height, repetitions);
            }
            else
            {
                newMs[sample] = Time(after, true, x, y, width, height, repetitions);
                oldMs[sample] = Time(before, false, x, y, width, height, repetitions);
            }
        }
        Array.Sort(oldMs);
        Array.Sort(newMs);
        if (before.DrawCalls != after.DrawCalls)
            throw new Exception(label + " benchmark changed Canvas call count");
        UiPainter.Fill(after, Ink, x, y, width, height);
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < repetitions; i++)
            UiPainter.Fill(after, Ink, x, y, width, height);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        if (bytes != 0)
            throw new Exception(label + " allocated " + bytes + " bytes");
        Console.WriteLine("HOST A/B Fill " + label + ": " + repetitions +
            " calls, median 7 alternating samples, previous " +
            oldMs[3].ToString("F3") + " ms, current " +
            newMs[3].ToString("F3") + " ms, ratio " +
            (oldMs[3] / newMs[3]).ToString("F2") +
            "x, allocations=" + bytes + " B; fake Canvas, NOT QEMU FPS, sink=" + sink);
    }

    internal static void Verify()
    {
        int[] coordinates = { int.MinValue, -60, -1, 0, 1, 10, 31, 46, 48, int.MaxValue };
        int[] widths = { 0, 1, 4, 48, int.MaxValue };
        int checks = 0;
        foreach (int x in coordinates)
            foreach (int y in coordinates)
                foreach (int width in widths)
                    foreach (int height in widths)
                    {
                        Check(x, y, width, height);
                        checks++;
                    }
        // Cover both horizontal boundaries and combinations with vertical
        // clipping, including widths that span the entire framebuffer.
        Random random = new Random(0x3095);
        for (int i = 0; i < 3000; i++)
        {
            int x = random.Next(-64, 96);
            int y = random.Next(-48, 80);
            int width = random.Next(0, 160);
            int height = random.Next(0, 96);
            Check(x, y, width, height);
            checks++;
        }
        Check(-2, 4, 80, 1);
        Check(-2, -2, 80, 40);
        Check(-2, 31, 80, 4);
        checks += 3;
        Console.WriteLine("PASS: " + checks +
            " exact Fill pixel/Canvas-call comparisons including int extremes and edge drags");
        Benchmark("visible 1x1", 5, 4, 1, 1);
        Benchmark("visible 8x1", 5, 4, 8, 1);
        Benchmark("clipped left", -2, 4, 8, 1);
        Benchmark("clipped right", 60, 4, 8, 1);
        Benchmark("clipped both horizontal edges", -2, 4, 80, 1);
        Benchmark("clipped top and left", -2, -2, 80, 8);
        Benchmark("fully offscreen", 90, 4, 8, 1);
    }
}
