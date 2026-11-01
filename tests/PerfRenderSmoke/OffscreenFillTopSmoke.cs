using System;
using System.Diagnostics;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;

// Compare against the EXACT production RoundedFill/RoundedTop implementation
// immediately before the new visibility guards. This is test-only C#; the
// production UiPainter is linked into this project without modification.
internal static class OffscreenFillTopSmoke
{
    private static readonly Color Ink = Color.FromArgb(31, 52, 83);

    private static void OldFill(Canvas canvas, int x, int y, int width, int height, int radius)
    {
        if (canvas == null || width <= 0 || height <= 0)
            return;
        int r = Math.Min(Math.Min(radius, width / 2), height / 2);
        if (r <= 0)
        {
            Fill(canvas, x, y, width, height);
            return;
        }

        Fill(canvas, x, y + r, width, height - 2 * r);
        for (int row = 0; row < r; row++)
        {
            int inset = UiPainter.CornerInset(r, row);
            Fill(canvas, x + inset, y + row, width - 2 * inset, 1);
            Fill(canvas, x + inset, y + height - row - 1, width - 2 * inset, 1);
        }
    }

    private static void OldTop(Canvas canvas, int x, int y, int width, int height, int radius)
    {
        if (canvas == null || width <= 0 || height <= 0)
            return;
        int r = Math.Min(Math.Min(radius, width / 2), height);
        Fill(canvas, x, y + r, width, height - r);
        for (int row = 0; row < r; row++)
        {
            int inset = UiPainter.CornerInset(r, row);
            Fill(canvas, x + inset, y + row, width - 2 * inset, 1);
        }
    }

    private static void Fill(Canvas canvas, int x, int y, int width, int height)
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
            canvas.DrawFilledRectangle(Ink, left, top, endX - left, endY - top);
    }

    private static void Draw(bool current, bool top, Canvas canvas,
        int x, int y, int width, int height, int radius)
    {
        if (current)
        {
            if (top) UiPainter.RoundedTop(canvas, x, y, width, height, radius, Ink);
            else UiPainter.RoundedFill(canvas, x, y, width, height, radius, Ink);
        }
        else
        {
            if (top) OldTop(canvas, x, y, width, height, radius);
            else OldFill(canvas, x, y, width, height, radius);
        }
    }

    private static void Compare(bool top, int x, int y, int radius)
    {
        Canvas oldFrame = new Canvas(140, 100);
        Canvas newFrame = new Canvas(140, 100);
        Draw(false, top, oldFrame, x, y, 85, 65, radius);
        Draw(true, top, newFrame, x, y, 85, 65, radius);
        if (oldFrame.DrawCalls != newFrame.DrawCalls)
            throw new Exception("Fill/Top Canvas calls changed for x=" + x +
                " y=" + y + " radius=" + radius);
        for (int i = 0; i < oldFrame.Pixels.Length; i++)
            if (oldFrame.Pixels[i] != newFrame.Pixels[i])
                throw new Exception("Fill/Top pixel mismatch at " + i +
                    " x=" + x + " y=" + y + " radius=" + radius);
    }

    private static double Time(bool current, bool top, Canvas canvas, bool visible, int repeats)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++)
        {
            if (visible)
                Draw(current, top, canvas, 12, 8, 60, 42, 12);
            else
                Draw(current, top, canvas, 400 + (i & 7) * 13, 350, 180, 100, 32);
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    private static void Benchmark(bool top, bool visible)
    {
        const int Samples = 5;
        int repeats = visible ? 2000 : 40000;
        Canvas beforeFrame = new Canvas(140, 100);
        Canvas afterFrame = new Canvas(140, 100);
        Time(false, top, beforeFrame, visible, 200);
        Time(true, top, afterFrame, visible, 200);
        double[] before = new double[Samples];
        double[] after = new double[Samples];
        for (int i = 0; i < Samples; i++)
        {
            if ((i & 1) == 0)
            {
                before[i] = Time(false, top, beforeFrame, visible, repeats);
                after[i] = Time(true, top, afterFrame, visible, repeats);
            }
            else
            {
                after[i] = Time(true, top, afterFrame, visible, repeats);
                before[i] = Time(false, top, beforeFrame, visible, repeats);
            }
        }
        Array.Sort(before);
        Array.Sort(after);
        if (!visible && (beforeFrame.DrawCalls != 0 || afterFrame.DrawCalls != 0))
            throw new Exception("Fully offscreen drawing touched Canvas");
        if (visible && (beforeFrame.DrawCalls == 0 || afterFrame.DrawCalls == 0))
            throw new Exception("Visible benchmark did not paint");
        Console.WriteLine("Host .NET pre-cull -> current " +
            (top ? "RoundedTop" : "RoundedFill") +
            (visible ? " visible: " : " offscreen: ") + repeats +
            " calls, median 5 alternating A/B " +
            before[2].ToString("F2") + " -> " + after[2].ToString("F2") +
            " ms (host fake Canvas, NOT QEMU/FPS).");
    }

    internal static void Verify()
    {
        int[] xs = { -160, -85, -1, 0, 8, 139, 140, 300 };
        int[] ys = { -110, -65, -1, 0, 15, 99, 100, 300 };
        int[] radii = { 0, 1, 4, 12, 32, 48 };
        int checks = 0;
        for (int kind = 0; kind < 2; kind++)
            foreach (int x in xs)
                foreach (int y in ys)
                    foreach (int radius in radii)
                    {
                        Compare(kind == 1, x, y, radius);
                        checks++;
                    }

        Canvas extreme = new Canvas(140, 100);
        foreach (bool top in new[] { false, true })
        {
            Draw(true, top, extreme, int.MinValue, 0, 180, 100, 32);
            Draw(true, top, extreme, int.MaxValue, 0, 180, 100, 32);
            Draw(true, top, extreme, 0, int.MinValue, 180, 100, 32);
            Draw(true, top, extreme, 0, int.MaxValue, 180, 100, 32);
        }
        if (extreme.DrawCalls != 0)
            throw new Exception("Extreme fully offscreen panel reached Canvas");

        Benchmark(false, false);
        Benchmark(true, false);
        Benchmark(false, true);
        Benchmark(true, true);

        Canvas allocationFrame = new Canvas(140, 100);
        Draw(true, false, allocationFrame, 12, 8, 60, 42, 12);
        Draw(true, true, allocationFrame, 12, 8, 60, 42, 12);
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            Draw(true, false, allocationFrame, 400, 350, 180, 100, 32);
            Draw(true, true, allocationFrame, 400, 350, 180, 100, 32);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        if (allocated != 0)
            throw new Exception("Offscreen rounded primitives allocated " + allocated + " bytes");
        Console.WriteLine("Offscreen Fill/Top: " + checks +
            " exact framebuffer+call comparisons, extreme coordinates, 0 B warmed allocations.");
    }
}
