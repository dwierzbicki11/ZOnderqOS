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
            if (after.DrawCalls > before.DrawCalls)
                throw new Exception("Visible text increased Canvas call count");
            if (positions[i, 0] == 0 && positions[i, 1] == 0 &&
                before.DrawCalls != after.DrawCalls)
                throw new Exception("Fully visible text changed Canvas call count");
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
        // A long, horizontally scrolled label leaves only a few glyphs on
        // screen. Verify the actual production raster, not only the index math.
        string longLabel = new string('X', 160);
        int[] offsets = { -930, -929, -928, -500, -4, 127, 128 };
        int improved = 0;
        for (int i = 0; i < offsets.Length; i++)
        {
            Canvas before = new Canvas(128, 48);
            Canvas after = new Canvas(128, 48);
            Baseline(before, longLabel, offsets[i], 0, Color.White);
            SmallTextRenderer.Draw(after, longLabel, offsets[i], 0, Color.White);
            SamePixels(before, after, "long partial text " + offsets[i]);
            if (after.DrawCalls > before.DrawCalls)
                throw new Exception("Long partial text increased Canvas calls");
            if (after.DrawCalls < before.DrawCalls)
                improved++;
        }
        if (improved < 4)
            throw new Exception("Long partial text did not cull hidden glyphs");

        // Extremes must not overflow the coordinate multiplication.
        foreach (int x in new[] { int.MinValue, int.MaxValue })
        {
            Canvas extreme = new Canvas(128, 48);
            SmallTextRenderer.Draw(extreme, longLabel, x, 0, Color.White);
            if (extreme.DrawCalls != 0)
                throw new Exception("Extreme horizontal offset reached Canvas");
        }

        Console.WriteLine("Text visibility: 7 short and 7 long exact framebuffer checks, " +
            "8 offscreen and 2 extreme guards; hidden glyph Canvas calls reduced.");
        Benchmark("offscreen text", label, 128, 0, 20000);
        Benchmark("visible text", label, 0, 0, 2000);
        Benchmark("partially visible long text", longLabel, -930, 0, 10000);
    }
}
