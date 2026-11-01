using System;
using System.Drawing;
using System.Diagnostics;
using System.Reflection;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;

// Host-only pixel and Canvas-call contract for partial vertical visibility.
// Compare the real production renderer at y=10 against a translated crop.
// No simulated kernel/QEMU result is inferred from these fake Canvas calls.
internal static class TextVerticalClipSmoke
{
    private static void VerifyCrop(string label, string text, int clippedY, int height)
    {
        const int width = 320;
        const int fullY = 10;
        Canvas full = new Canvas(width, 40);
        Canvas clipped = new Canvas(width, height);
        SmallTextRenderer.Draw(full, text, 8, fullY, Color.White);
        SmallTextRenderer.Draw(clipped, text, 8, clippedY, Color.White);

        int visible = 0;
        for (int row = 0; row < height; row++)
        {
            int originalRow = row - clippedY + fullY;
            for (int col = 0; col < width; col++)
            {
                int expected = originalRow >= 0 && originalRow < full.Height
                    ? full.Pixels[originalRow * width + col] : 0;
                int actual = clipped.Pixels[row * width + col];
                if (actual != expected)
                    throw new Exception(label + " pixel mismatch x=" + col + " y=" + row);
                if (actual != 0)
                    visible++;
            }
        }
        if (visible == 0)
            throw new Exception(label + " has no visible pixels");
        if (clipped.DrawCalls >= full.DrawCalls)
            throw new Exception(label + " did not reduce Canvas calls: " +
                full.DrawCalls + " -> " + clipped.DrawCalls);
        Console.WriteLine("PASS: " + label + " exact pixels, Canvas calls " +
            full.DrawCalls + " -> " + clipped.DrawCalls);
    }

    // Snapshot of the previous ASCII glyph raster loop: same cached glyph
    // bits and packed spans, but every row is sent to Canvas even when Y is
    // outside the framebuffer. This is an A/B baseline, not kernel code.
    private static readonly byte[] PreviousRows = (byte[])typeof(SmallTextRenderer)
        .GetField("CachedAsciiGlyphRows", BindingFlags.NonPublic | BindingFlags.Static)
        .GetValue(null);
    private static readonly int[] PreviousRuns = (int[])typeof(SmallTextRenderer)
        .GetField("CachedSpanRuns", BindingFlags.NonPublic | BindingFlags.Static)
        .GetValue(null);
    private static long sink;

    private static void PreviousDraw(Canvas canvas, string text, int x, int y)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == ' ') continue;
            int offset = ch * 7;
            for (int row = 0; row < 7; row++)
            {
                int bits = PreviousRows[offset + row];
                if (bits == 0) continue;
                int packed = PreviousRuns[bits];
                int runs = packed & 3;
                packed >>= 2;
                for (int run = 0; run < runs; run++)
                {
                    int start = packed & 7;
                    int length = (packed >> 3) & 7;
                    canvas.DrawFilledRectangle(Color.White, x + i * 6 + start,
                        y + row, length, 1);
                    packed >>= 6;
                }
            }
        }
    }

    private static double Time(Canvas canvas, string text, int y, bool optimized)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < 1500; i++)
        {
            if (optimized)
                SmallTextRenderer.Draw(canvas, text, 8, y, Color.White);
            else
                PreviousDraw(canvas, text, 8, y);
        }
        sink += canvas.DrawCalls;
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    private static void Benchmark(string label, int y, int height)
    {
        const string text = "WWWWWW123456789";
        Canvas oldCanvas = new Canvas(320, height);
        Canvas newCanvas = new Canvas(320, height);
        PreviousDraw(oldCanvas, text, 8, y);
        SmallTextRenderer.Draw(newCanvas, text, 8, y, Color.White);
        for (int i = 0; i < oldCanvas.Pixels.Length; i++)
            if (oldCanvas.Pixels[i] != newCanvas.Pixels[i])
                throw new Exception(label + " previous/new pixel mismatch " + i);
        int oldCalls = oldCanvas.DrawCalls;
        int newCalls = newCanvas.DrawCalls;
        if (y < 0 || y + 7 > height)
        {
            if (newCalls >= oldCalls)
                throw new Exception(label + " did not reduce clipped Canvas calls");
        }
        else if (newCalls != oldCalls)
            throw new Exception(label + " changed fully visible Canvas calls");

        Time(oldCanvas, text, y, false);
        Time(newCanvas, text, y, true);
        double[] oldMs = new double[7], newMs = new double[7];
        for (int sample = 0; sample < 7; sample++)
        {
            if ((sample & 1) == 0)
            {
                oldMs[sample] = Time(oldCanvas, text, y, false);
                newMs[sample] = Time(newCanvas, text, y, true);
            }
            else
            {
                newMs[sample] = Time(newCanvas, text, y, true);
                oldMs[sample] = Time(oldCanvas, text, y, false);
            }
        }
        Array.Sort(oldMs);
        Array.Sort(newMs);
        Console.WriteLine("HOST A/B vertical glyph " + label + ": old=" +
            oldMs[3].ToString("F3") + "ms new=" + newMs[3].ToString("F3") +
            "ms ratio=" + (oldMs[3] / newMs[3]).ToString("F2") +
            "x, Canvas calls per draw=" + oldCalls + " -> " + newCalls +
            " (7 alternating samples, 1500 draws, fake Canvas; sink=" + sink + ")");
    }

    internal static void Verify()
    {
        // W has lit pixels in both its upper and lower rows.
        VerifyCrop("top partial text", "WWWWWW", -4, 6);
        VerifyCrop("bottom partial text", "WWWWWW", 3, 5);
        VerifyCrop("top partial numeric text", "123456789", -4, 6);
        // The clipping path is exercised by the production DrawUInt as well.
        Canvas full = new Canvas(320, 40);
        Canvas clipped = new Canvas(320, 6);
        SmallTextRenderer.DrawUInt(full, 123456789UL, 8, 10, Color.White);
        SmallTextRenderer.DrawUInt(clipped, 123456789UL, 8, -4, Color.White);
        for (int row = 0; row < clipped.Height; row++)
            for (int col = 0; col < clipped.Width; col++)
                if (clipped.Pixels[row * clipped.Width + col] !=
                    full.Pixels[(row + 14) * full.Width + col])
                    throw new Exception("numeric vertical clipping changed pixel");
        if (clipped.DrawCalls >= full.DrawCalls)
            throw new Exception("numeric clipping did not reduce Canvas calls");

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 128; i++)
            SmallTextRenderer.Draw(clipped, "WWWWWW", 8, -4, Color.White);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (bytes != 0)
            throw new Exception("clipped glyph renderer allocated " + bytes + " bytes");
        Console.WriteLine("PASS: numeric crop exact, clipped glyph allocations=0 B");
        Benchmark("top", -4, 6);
        Benchmark("bottom", 3, 5);
        Benchmark("fully-visible control", 1, 12);
    }
}
