using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;

// Host regression for the production text Draw() visibility guard. Baseline
// is the previous Draw() loop calling the same private production DrawChar.
internal static class TextVisibilitySmoke
{
    private static readonly Action<Canvas, char, int, int, Color> DrawChar =
        (Action<Canvas, char, int, int, Color>)typeof(SmallTextRenderer)
            .GetMethod("DrawChar", BindingFlags.NonPublic | BindingFlags.Static)
            .CreateDelegate(typeof(Action<Canvas, char, int, int, Color>));

    private static void Baseline(Canvas canvas, string text, int x, int y, Color color)
    {
        for (int i = 0; i < text.Length; i++)
            DrawChar(canvas, text[i], x + i * 6, y, color);
    }

    private static void SamePixels(Canvas a, Canvas b, string label)
    {
        for (int i = 0; i < a.Pixels.Length; i++)
            if (a.Pixels[i] != b.Pixels[i])
                throw new Exception(label + ": pixel mismatch at " + i);
    }

    private static double Time(Canvas canvas, string text, bool optimized,
        int x, int y, int iterations)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
        {
            if (optimized)
                SmallTextRenderer.Draw(canvas, text, x, y, Color.White);
            else
                Baseline(canvas, text, x, y, Color.White);
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    private static void Benchmark(string label, string text, int x, int y, int iterations)
    {
        Canvas beforeCanvas = new Canvas(128, 48);
        Canvas afterCanvas = new Canvas(128, 48);
        Time(beforeCanvas, text, false, x, y, 100);
        Time(afterCanvas, text, true, x, y, 100);
        double[] before = new double[5];
        double[] after = new double[5];
        for (int sample = 0; sample < 5; sample++)
        {
            if ((sample & 1) == 0)
            {
                before[sample] = Time(beforeCanvas, text, false, x, y, iterations);
                after[sample] = Time(afterCanvas, text, true, x, y, iterations);
            }
            else
            {
                after[sample] = Time(afterCanvas, text, true, x, y, iterations);
                before[sample] = Time(beforeCanvas, text, false, x, y, iterations);
            }
        }
        Array.Sort(before);
        Array.Sort(after);
        SamePixels(beforeCanvas, afterCanvas, label);
        Console.WriteLine("Host .NET " + label + ": " + iterations +
            " labels, median of 5 alternating A/B; previous glyph loop " +
            before[2].ToString("F2") + " ms, visibility guard " +
            after[2].ToString("F2") + " ms (not QEMU FPS).");
    }

    internal static void Verify()
    {
        const string label = "ZonderqOS File Manager";
        int[,] positions = {
            { 0, 0 }, { -3, -3 }, { -60, 1 }, { 100, 40 },
            { 127, 47 }, { 10, 45 }, { 1, 1 }
        };
        for (int i = 0; i < positions.GetLength(0); i++)
        {
            Canvas before = new Canvas(128, 48);
            Canvas after = new Canvas(128, 48);
            Baseline(before, label, positions[i, 0], positions[i, 1], Color.White);
            SmallTextRenderer.Draw(after, label, positions[i, 0], positions[i, 1], Color.White);
            SamePixels(before, after, "visible text " + i);
            if (before.DrawCalls != after.DrawCalls)
                throw new Exception("Visible text changed Canvas call count");
        }
        int[,] offscreen = {
            { 128, 0 }, { 0, 48 }, { -200, 0 }, { 0, -7 },
            { int.MaxValue, 0 }, { 0, int.MaxValue },
            { int.MinValue, 0 }, { 0, int.MinValue }
        };
        for (int i = 0; i < offscreen.GetLength(0); i++)
        {
            Canvas canvas = new Canvas(128, 48);
            SmallTextRenderer.Draw(canvas, label, offscreen[i, 0], offscreen[i, 1], Color.White);
            if (canvas.DrawCalls != 0)
                throw new Exception("Offscreen text reached Canvas " + i);
        }
        Console.WriteLine("Text visibility: 7 visible framebuffer/call equivalence checks, 8 fully offscreen culls.");
        Benchmark("offscreen text", label, 128, 0, 20000);
        Benchmark("visible text", label, 0, 0, 2000);
    }
}
