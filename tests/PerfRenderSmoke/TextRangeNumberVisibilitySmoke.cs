using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;

// Runs the real production DrawRange/DrawUInt with a host-only Canvas.
// The baselines mirror their previous loops; no kernel FPS is inferred.
internal static class TextRangeNumberVisibilitySmoke
{
    private static readonly Action<Canvas, char, int, int, Color> DrawChar =
        (Action<Canvas, char, int, int, Color>)typeof(SmallTextRenderer)
            .GetMethod("DrawChar", BindingFlags.NonPublic | BindingFlags.Static)
            .CreateDelegate(typeof(Action<Canvas, char, int, int, Color>));

    private static void PreviousRange(Canvas canvas, string text, int start, int count,
        int x, int y, Color color)
    {
        if (string.IsNullOrEmpty(text) || count <= 0 || start < 0 || start >= text.Length)
            return;
        int end = Math.Min(text.Length, start + count);
        for (int i = start; i < end; i++)
            DrawChar(canvas, text[i], x + (i - start) * 6, y, color);
    }

    private static void PreviousUInt(Canvas canvas, ulong value, int x, int y, Color color)
    {
        int digits = 1;
        ulong remaining = value;
        while (remaining >= 10)
        {
            remaining /= 10;
            digits++;
        }
        ulong divisor = 1;
        for (int i = 1; i < digits; i++)
            divisor *= 10;
        for (int i = 0; i < digits; i++)
        {
            ulong digit = divisor == 0 ? 0 : value / divisor;
            DrawChar(canvas, (char)('0' + (int)digit), x + i * 6, y, color);
            if (divisor > 1)
            {
                value %= divisor;
                divisor /= 10;
            }
            else
                divisor = 0;
        }
    }

    private static void Compare(Canvas expected, Canvas actual, string label)
    {
        if (expected.DrawCalls != actual.DrawCalls)
            throw new Exception(label + ": Canvas call count changed");
        for (int i = 0; i < expected.Pixels.Length; i++)
            if (expected.Pixels[i] != actual.Pixels[i])
                throw new Exception(label + ": framebuffer mismatch at " + i);
    }

    private static void VerifyVisible()
    {
        const string text = "  File Manager: 1366x768 / folder  ";
        int[,] positions = { { 0, 0 }, { -20, 3 }, { 126, 10 },
            { 2, -6 }, { 10, 46 }, { 25, 15 } };
        for (int i = 0; i < positions.GetLength(0); i++)
        {
            Canvas oldFrame = new Canvas(128, 48);
            Canvas newFrame = new Canvas(128, 48);
            int x = positions[i, 0], y = positions[i, 1];
            PreviousRange(oldFrame, text, 2, 24, x, y, Color.White);
            SmallTextRenderer.DrawRange(newFrame, text, 2, 24, x, y, Color.White);
            Compare(oldFrame, newFrame, "visible range " + i);
        }
        ulong[] numbers = { 0, 1, 42, 1234567890UL, ulong.MaxValue };
        for (int n = 0; n < numbers.Length; n++)
            for (int i = 0; i < positions.GetLength(0); i++)
            {
                Canvas oldFrame = new Canvas(128, 48);
                Canvas newFrame = new Canvas(128, 48);
                int x = positions[i, 0], y = positions[i, 1];
                PreviousUInt(oldFrame, numbers[n], x, y, Color.White);
                SmallTextRenderer.DrawUInt(newFrame, numbers[n], x, y, Color.White);
                Compare(oldFrame, newFrame, "visible uint " + n + "/" + i);
            }

        // The previous start + count overflowed for int.MaxValue. The
        // corrected path must draw exactly the remaining characters.
        Canvas expected = new Canvas(128, 48);
        Canvas actual = new Canvas(128, 48);
        PreviousRange(expected, text, 2, text.Length - 2, 1, 1, Color.White);
        SmallTextRenderer.DrawRange(actual, text, 2, int.MaxValue, 1, 1, Color.White);
        Compare(expected, actual, "saturated range");
        Console.WriteLine("Text range/number: 37 exact visible framebuffer and call-count comparisons.");
    }

    private static void VerifyOffscreen()
    {
        const string text = "ZonderqOS task manager memory and filesystem";
        int[,] positions = { { 128, 0 }, { 0, 48 }, { -500, 0 },
            { 0, -7 }, { int.MaxValue, 0 }, { int.MinValue, 0 },
            { 0, int.MaxValue }, { 0, int.MinValue } };
        for (int i = 0; i < positions.GetLength(0); i++)
        {
            Canvas range = new Canvas(128, 48);
            Canvas number = new Canvas(128, 48);
            SmallTextRenderer.DrawRange(range, text, 2, 20,
                positions[i, 0], positions[i, 1], Color.White);
            SmallTextRenderer.DrawUInt(number, ulong.MaxValue,
                positions[i, 0], positions[i, 1], Color.White);
            if (range.DrawCalls != 0 || number.DrawCalls != 0)
                throw new Exception("Fully offscreen text reached Canvas: " + i);
        }
        // No pixels, no Canvas calls and no NullReferenceException.
        SmallTextRenderer.DrawRange(null, text, 2, 20, 0, 0, Color.White);
        SmallTextRenderer.DrawUInt(null, 42, 0, 0, Color.White);
        Console.WriteLine("Text range/number: 16 offscreen culls and 2 null-canvas guards.");
    }

    private static double TimeRange(bool optimized, Canvas canvas, string text, int repetitions)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repetitions; i++)
        {
            if (optimized)
                SmallTextRenderer.DrawRange(canvas, text, 0, text.Length, 128, 0, Color.White);
            else
                PreviousRange(canvas, text, 0, text.Length, 128, 0, Color.White);
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    private static double TimeNumber(bool optimized, Canvas canvas, int repetitions)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repetitions; i++)
        {
            if (optimized)
                SmallTextRenderer.DrawUInt(canvas, ulong.MaxValue, 128, 0, Color.White);
            else
                PreviousUInt(canvas, ulong.MaxValue, 128, 0, Color.White);
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    private static void Benchmark(string label, Func<bool, Canvas, int, double> time)
    {
        Canvas frame = new Canvas(128, 48);
        time(false, frame, 100);
        time(true, frame, 100);
        const int iterations = 10000;
        double[] baseline = new double[5];
        double[] optimized = new double[5];
        for (int i = 0; i < 5; i++)
        {
            if ((i & 1) == 0)
            {
                baseline[i] = time(false, frame, iterations);
                optimized[i] = time(true, frame, iterations);
            }
            else
            {
                optimized[i] = time(true, frame, iterations);
                baseline[i] = time(false, frame, iterations);
            }
        }
        Array.Sort(baseline);
        Array.Sort(optimized);
        if (frame.DrawCalls != 0)
            throw new Exception("Offscreen benchmark unexpectedly drew pixels");
        Console.WriteLine("Host .NET " + label + ": " + iterations +
            " calls, median of 5 alternating A/B; baseline " +
            baseline[2].ToString("F2") + " ms, optimized " +
            optimized[2].ToString("F2") + " ms (not QEMU FPS).");
    }

    internal static void Verify()
    {
        VerifyVisible();
        VerifyOffscreen();
        string text = new string('X', 160);
        Benchmark("offscreen DrawRange", (optimized, canvas, iterations) =>
            TimeRange(optimized, canvas, text, iterations));
        Benchmark("offscreen DrawUInt", TimeNumber);
    }
}
