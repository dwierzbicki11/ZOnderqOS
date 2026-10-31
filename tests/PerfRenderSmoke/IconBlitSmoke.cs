using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI.Icons;

// Host-side exact framebuffer regression for the PRODUCTION IconManager.Blit.
// No fake success markers: the same C# Blit body used by the kernel executes.
internal static class IconBlitSmoke
{
    private static readonly Action<Canvas, int[], int, int, int, int, bool> ProductionBlit =
        (Action<Canvas, int[], int, int, int, int, bool>)typeof(IconManager)
            .GetMethod("Blit", BindingFlags.Static | BindingFlags.NonPublic)
            .CreateDelegate(typeof(Action<Canvas, int[], int, int, int, int, bool>));

    private static readonly Func<int[], int, int, bool> Classify =
        (Func<int[], int, int, bool>)typeof(IconManager)
            .GetMethod("ShouldCoalesce", BindingFlags.Static | BindingFlags.NonPublic)
            .CreateDelegate(typeof(Func<int[], int, int, bool>));

    private static void BaselineBlit(Canvas canvas, int[] pixels, int width, int height, int x, int y)
    {
        int startX = x < 0 ? -x : 0;
        int startY = y < 0 ? -y : 0;
        int endX = width;
        int endY = height;
        if (x + endX > canvas.Width) endX = canvas.Width - x;
        if (y + endY > canvas.Height) endY = canvas.Height - y;
        if (startX >= endX || startY >= endY) return;
        for (int py = startY; py < endY; py++)
        {
            int row = py * width;
            for (int px = startX; px < endX; px++)
            {
                int argb = pixels[row + px];
                if (((uint)argb >> 24) == 0) continue;
                canvas.DrawPoint(Color.FromArgb(argb), x + px, y + py);
            }
        }
    }

    private static int[] MakePixels(int width, int height, int pattern)
    {
        int[] pixels = new int[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int color;
                switch (pattern)
                {
                    case 0: color = unchecked((int)0xFF3478ABu); break;
                    case 1: color = unchecked((int)0xFF000000u) | ((x * 37 + y * 67) & 0xFFFFFF); break;
                    case 2: color = (x % 2 == 0) ? unchecked((int)0xFF123456u) : unchecked((int)0xFFABCDEFu); break;
                    case 3: color = (x % 3 == 0) ? 0 : unchecked((int)0x8044BB22u); break;
                    case 4: color = (x % 7 == 0) ? 0 : (x % 7 == 1) ?
                        unchecked((int)0x8000AAFFu) : unchecked((int)0xFF117733u); break;
                    default: color = unchecked((int)0xFFFFFFFFu); break;
                }
                pixels[y * width + x] = color;
            }
        return pixels;
    }

    private static void Check(int pattern, int x, int y)
    {
        int[] pixels = MakePixels(48, 48, pattern);
        Canvas baseline = new Canvas(64, 64);
        Canvas current = new Canvas(64, 64);
        BaselineBlit(baseline, pixels, 48, 48, x, y);
        ProductionBlit(current, pixels, 48, 48, x, y, Classify(pixels, 48, 48));
        if (baseline.DrawCalls < current.DrawCalls)
            throw new Exception("Icon Blit increased Canvas calls: " + pattern + " at " + x + "," + y);
        for (int i = 0; i < baseline.Pixels.Length; i++)
            if (baseline.Pixels[i] != current.Pixels[i])
                throw new Exception("Icon Blit framebuffer mismatch: " + pattern + " at " + x + "," + y + " pixel " + i);
        if (pattern == 0 && x == 0 && y == 0 && !(current.DrawCalls < baseline.DrawCalls))
            throw new Exception("Opaque icon run was not coalesced");
    }

    private static double Time(Canvas canvas, int[] pixels, bool optimized, int iterations)
    {
        bool useRuns = Classify(pixels, 48, 48); // cached before timed loop
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
        {
            if (optimized) ProductionBlit(canvas, pixels, 48, 48, 0, 0, useRuns);
            else BaselineBlit(canvas, pixels, 48, 48, 0, 0);
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    private static void Benchmark(int pattern, string label)
    {
        int[] pixels = MakePixels(48, 48, pattern);
        Canvas baseline = new Canvas(64, 64);
        Canvas current = new Canvas(64, 64);
        Time(baseline, pixels, false, 200);
        Time(current, pixels, true, 200);
        const int iterations = 1500;
        double[] before = new double[5];
        double[] after = new double[5];
        for (int sample = 0; sample < 5; sample++)
        {
            if ((sample & 1) == 0)
            {
                before[sample] = Time(baseline, pixels, false, iterations);
                after[sample] = Time(current, pixels, true, iterations);
            }
            else
            {
                after[sample] = Time(current, pixels, true, iterations);
                before[sample] = Time(baseline, pixels, false, iterations);
            }
        }
        Array.Sort(before);
        Array.Sort(after);
        Console.WriteLine("Host .NET icon " + label + ": " + iterations +
            " blits, median of 5 alternating A/B; original " +
            before[2].ToString("F2") + " ms, run-coalesced " +
            after[2].ToString("F2") + " ms (not QEMU FPS).");
    }

    internal static void Verify()
    {
        int count = 0;
        for (int pattern = 0; pattern < 5; pattern++)
            foreach (int x in new int[] { 0, -9, 18, 52, -55 })
                foreach (int y in new int[] { 0, -7, 25, 60, -60 })
                {
                    Check(pattern, x, y);
                    count++;
                }
        if (!Classify(MakePixels(48, 48, 0), 48, 48) ||
            Classify(MakePixels(48, 48, 1), 48, 48) ||
            Classify(MakePixels(48, 48, 3), 48, 48))
            throw new Exception("Icon run classification regression");
        Console.WriteLine("Icon Blit: " + count + " exact framebuffer/call-count checks, including clipping and alpha.");
        Benchmark(0, "opaque solid");
        Benchmark(1, "opaque gradient");
        Benchmark(4, "transparent + opaque runs");
    }
}
