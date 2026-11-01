using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI.Icons;

// Compiles and exercises the production cache-exhaustion fallback. Unlike a
// mathematical model, the framebuffer tests execute the actual C# method.
internal static class IconDirectScaleSmoke
{
    private static readonly Action<Canvas, int[], int, int, int, int, int, int> Production =
        (Action<Canvas, int[], int, int, int, int, int, int>)typeof(IconManager)
            .GetMethod("BlitScaledDirect", BindingFlags.Static | BindingFlags.NonPublic)
            .CreateDelegate(typeof(Action<Canvas, int[], int, int, int, int, int, int>));

    // Independent pre-optimization implementation: division inside the inner loop.
    private static void Baseline(Canvas canvas, int[] source, int sourceWidth, int sourceHeight,
        int x, int y, int width, int height)
    {
        int startX = x < 0 ? -x : 0;
        int startY = y < 0 ? -y : 0;
        int endX = width;
        int endY = height;
        if (x + endX > canvas.Width) endX = canvas.Width - x;
        if (y + endY > canvas.Height) endY = canvas.Height - y;
        if (startX >= endX || startY >= endY) return;

        for (int dy = startY; dy < endY; dy++)
        {
            int sy = (dy * sourceHeight) / height;
            int row = sy * sourceWidth;
            for (int dx = startX; dx < endX; dx++)
            {
                int sx = (dx * sourceWidth) / width;
                int argb = source[row + sx];
                if (((uint)argb >> 24) != 0)
                    canvas.DrawPoint(Color.FromArgb(argb), x + dx, y + dy);
            }
        }
    }

    private static int[] Source(int width, int height, int pattern)
    {
        int[] pixels = new int[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int rgb = ((x * 7919) ^ (y * 65537) ^ (x * y * 71)) & 0xFFFFFF;
                int alpha = pattern == 0 ? 255 : pattern == 1 ? 0 :
                    pattern == 2 ? ((x + y) % 3 == 0 ? 0 : 255) :
                    ((x * 5 + y * 7) % 4) * 64;
                pixels[y * width + x] = (alpha << 24) | rgb;
            }
        return pixels;
    }

    private static void VerifyCase(int sw, int sh, int w, int h, int x, int y, int pattern)
    {
        int[] source = Source(sw, sh, pattern);
        Canvas before = new Canvas(96, 80);
        Canvas after = new Canvas(96, 80);
        Baseline(before, source, sw, sh, x, y, w, h);
        Production(after, source, sw, sh, x, y, w, h);
        if (before.DrawCalls != after.DrawCalls)
            throw new Exception("Direct icon scale changed draw count");
        for (int i = 0; i < before.Pixels.Length; i++)
            if (before.Pixels[i] != after.Pixels[i])
                throw new Exception("Direct icon scale changed pixel " + i +
                    " for " + sw + "x" + sh + " -> " + w + "x" + h +
                    " at " + x + "," + y + " pattern " + pattern);
    }

    private static double Time(bool optimized, Canvas canvas, int[] source, int repeats)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++)
        {
            if (optimized)
                Production(canvas, source, 128, 96, 0, 0, 192, 128);
            else
                Baseline(canvas, source, 128, 96, 0, 0, 192, 128);
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    internal static void Verify()
    {
        int[,] sizes = {
            { 7, 5, 63, 41 }, { 63, 41, 7, 5 },
            { 47, 33, 47, 33 }, { 13, 19, 90, 71 },
            { 90, 71, 13, 19 }, { 1, 1, 65, 63 },
            { 65, 63, 1, 1 }, { 23, 29, 32, 64 },
            { 32, 64, 23, 29 }
        };
        int[] positionsX = { 0, -5, 17, 92, -120 };
        int[] positionsY = { 0, -9, 22, 77, -120 };
        int checks = 0;
        for (int i = 0; i < sizes.GetLength(0); i++)
            for (int pattern = 0; pattern < 4; pattern++)
                foreach (int x in positionsX)
                    foreach (int y in positionsY)
                    {
                        VerifyCase(sizes[i, 0], sizes[i, 1], sizes[i, 2],
                            sizes[i, 3], x, y, pattern);
                        checks++;
                    }
        Console.WriteLine("Direct icon scaling: " + checks +
            " production C# pixel/call equivalence checks, including clipping and alpha.");

        int[] source = Source(128, 96, 0);
        Canvas before = new Canvas(200, 140);
        Canvas after = new Canvas(200, 140);
        Time(false, before, source, 20);
        Time(true, after, source, 20);
        const int iterations = 200;
        double[] baseline = new double[5];
        double[] optimized = new double[5];
        for (int sample = 0; sample < 5; sample++)
        {
            if ((sample & 1) == 0)
            {
                baseline[sample] = Time(false, before, source, iterations);
                optimized[sample] = Time(true, after, source, iterations);
            }
            else
            {
                optimized[sample] = Time(true, after, source, iterations);
                baseline[sample] = Time(false, before, source, iterations);
            }
        }
        Array.Sort(baseline);
        Array.Sort(optimized);
        Console.WriteLine("Host .NET direct icon scaling (cache full): " + iterations +
            " blits, median of 5 alternating A/B; per-pixel divide " +
            baseline[2].ToString("F2") + " ms, quotient/remainder " +
            optimized[2].ToString("F2") + " ms (not QEMU FPS).");
    }
}
