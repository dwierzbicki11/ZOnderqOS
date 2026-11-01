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

    private static void PreviousInt(Canvas canvas, long value, int x, int y, Color color)
    {
        if (value >= 0)
        {
            PreviousUInt(canvas, (ulong)value, x, y, color);
            return;
        }
        DrawChar(canvas, '-', x, y, color);
        ulong magnitude = value == long.MinValue
            ? ((ulong)long.MaxValue + 1UL)
            : (ulong)(-value);
        PreviousUInt(canvas, magnitude, x + 6, y, color);
    }

    private static void Compare(Canvas expected, Canvas actual, string label,
        bool allowFewerCalls = false)
    {
        if (allowFewerCalls ? actual.DrawCalls > expected.DrawCalls :
            expected.DrawCalls != actual.DrawCalls)
            throw new Exception(label + ": Canvas call count regression");
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
            Compare(oldFrame, newFrame, "visible range " + i, true);
        }
        ulong[] numbers = { 0, 1, 42, 1234567890UL, ulong.MaxValue };
        for (int n = 0; n < numbers.Length; n++)
            for (int i = 0; i < positions.GetLength(0); i++)
            {
                Canvas oldFrame = new Canvas(128, 48);
                Canvas newFrame = new Canvas(128, 48);
                // The shortest numeric label is only six pixels wide;
                // keep it partially visible when comparing draw-call counts.
                int x = positions[i, 0] == -20 ? -3 : positions[i, 0];
                int y = positions[i, 1];
                PreviousUInt(oldFrame, numbers[n], x, y, Color.White);
                SmallTextRenderer.DrawUInt(newFrame, numbers[n], x, y, Color.White);
                Compare(oldFrame, newFrame, "visible uint " + n + "/" + i, true);
                if (x == 0 && y == 0 && oldFrame.DrawCalls != newFrame.DrawCalls)
                    throw new Exception("Fully visible number changed Canvas calls");
            }

        // The previous start + count overflowed for int.MaxValue. The
        // corrected path must draw exactly the remaining characters.
        Canvas expected = new Canvas(128, 48);
        Canvas actual = new Canvas(128, 48);
        PreviousRange(expected, text, 2, text.Length - 2, 1, 1, Color.White);
        SmallTextRenderer.DrawRange(actual, text, 2, int.MaxValue, 1, 1, Color.White);
        Compare(expected, actual, "saturated range", true);
        // Long numeric labels often extend beyond narrow task-manager columns.
        // The optimized path must draw the same visible digits with fewer
        // Canvas calls, including when only the final digit is visible.
        int[] offsets = { -110, -100, -80, -40, -4, 0, 127 };
        int reduced = 0;
        foreach (int x in offsets)
        {
            Canvas oldFrame = new Canvas(128, 48);
            Canvas newFrame = new Canvas(128, 48);
            PreviousUInt(oldFrame, ulong.MaxValue, x, 0, Color.White);
            SmallTextRenderer.DrawUInt(newFrame, ulong.MaxValue, x, 0, Color.White);
            Compare(oldFrame, newFrame, "partial uint " + x, true);
            if (newFrame.DrawCalls < oldFrame.DrawCalls)
                reduced++;
        }
        if (reduced < 3)
            throw new Exception("Numeric clipping did not reduce Canvas calls");
        // Signed numbers have a separate minus-sign entry point; preserve
        // its pixels while culling fully hidden signs and magnitudes.
        foreach (long value in new[] { long.MinValue, -123456789L, -42L })
            foreach (int x in offsets)
            {
                Canvas oldFrame = new Canvas(128, 48);
                Canvas newFrame = new Canvas(128, 48);
                PreviousInt(oldFrame, value, x, 0, Color.White);
                SmallTextRenderer.DrawInt(newFrame, value, x, 0, Color.White);
                Compare(oldFrame, newFrame, "partial signed " + value + "/" + x, true);
            }
        Console.WriteLine("Text range/number: 37 visible, 7 unsigned and 21 signed " +
            "partial framebuffer comparisons.");
    }


    // Exact pre-change DrawUInt (including its clipped-digit division loop).
    // Benchmark A/B therefore compares two implementations on the same host
    // and the same Canvas; it is not a kernel FPS or CPU-idle measurement.
    private static void BeforeSkipUInt(Canvas canvas, ulong value, int x, int y, Color color)
    {
        int digits = 1;
        ulong remaining = value;
        while (remaining >= 10)
        {
            remaining /= 10;
            digits++;
        }
        if (canvas == null || (long)x >= canvas.Width || (long)y >= canvas.Height ||
            (long)x + (long)digits * 6 <= 0 || (long)y + 7 <= 0)
            return;
        int first = 0;
        int last = digits;
        bool partiallyClipped = x < -4 || (long)x + (long)digits * 6 > canvas.Width;
        if (partiallyClipped)
        {
            if (x < -4)
            {
                long skip = (-(long)x + 1) / 6;
                if (skip >= digits)
                    return;
                first = (int)skip;
            }
            long visibleEnd = ((long)canvas.Width - x + 5) / 6;
            if (visibleEnd < last)
                last = (int)visibleEnd;
        }
        ulong divisor = 1;
        for (int i = 1; i < digits; i++)
            divisor *= 10;
        for (int i = 0; i < digits; i++)
        {
            ulong digit = divisor == 0 ? 0 : value / divisor;
            if (!partiallyClipped || (i >= first && i < last))
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

    private static double TimeSkippedDigits(bool optimized, Canvas frame, int x, int repeats)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++)
        {
            if (optimized)
                SmallTextRenderer.DrawUInt(frame, ulong.MaxValue, x, 0, Color.White);
            else
                BeforeSkipUInt(frame, ulong.MaxValue, x, 0, Color.White);
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    private static void VerifySkippedDigits()
    {
        ulong[] values = { 0, 1, 9, 10, 42, 999, 1000, 12345678901234567890UL, ulong.MaxValue };
        int[] widths = { 5, 24, 128, 256 };
        int[] offsets = { int.MinValue, -130, -120, -100, -40, -20, -5, -4, -1,
            0, 7, 24, 127, 256, int.MaxValue };
        int checks = 0;
        foreach (ulong value in values)
            foreach (int width in widths)
                foreach (int x in offsets)
                {
                    Canvas baseline = new Canvas(width, 16);
                    Canvas optimized = new Canvas(width, 16);
                    BeforeSkipUInt(baseline, value, x, 0, Color.White);
                    SmallTextRenderer.DrawUInt(optimized, value, x, 0, Color.White);
                    Compare(baseline, optimized, "skipped digits " + value + "/" + width + "/" + x);
                    checks++;
                }

        // Deterministic additional 64-bit values, including 20-digit numbers.
        ulong seed = 0x9E3779B97F4A7C15UL;
        for (int i = 0; i < 256; i++)
        {
            seed = unchecked(seed * 6364136223846793005UL + 1442695040888963407UL);
            int x = -120 + i % 145;
            Canvas baseline = new Canvas(48, 16);
            Canvas optimized = new Canvas(48, 16);
            BeforeSkipUInt(baseline, seed, x, 0, Color.White);
            SmallTextRenderer.DrawUInt(optimized, seed, x, 0, Color.White);
            Compare(baseline, optimized, "random skipped digits " + i);
            checks++;
        }

        Canvas allocationFrame = new Canvas(24, 16);
        for (int i = 0; i < 100; i++)
            SmallTextRenderer.DrawUInt(allocationFrame, ulong.MaxValue, 0, 0, Color.White);
        long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            SmallTextRenderer.DrawUInt(allocationFrame, ulong.MaxValue, 0, 0, Color.White);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAlloc;
        if (allocated != 0)
            throw new Exception("Clipped numeric renderer allocated " + allocated + " B");

        // Each A/B scenario is warmed up and alternates measurement order.
        foreach (int scenario in new[] { 0, 1, 2 })
        {
            int width = scenario == 0 ? 24 : scenario == 1 ? 128 : 256;
            int x = scenario == 1 ? -100 : 0;
            Canvas beforeFrame = new Canvas(width, 16);
            Canvas afterFrame = new Canvas(width, 16);
            TimeSkippedDigits(false, beforeFrame, x, 1000);
            TimeSkippedDigits(true, afterFrame, x, 1000);
            const int repetitions = 100000;
            double[] before = new double[5];
            double[] after = new double[5];
            for (int sample = 0; sample < 5; sample++)
            {
                if ((sample & 1) == 0)
                {
                    before[sample] = TimeSkippedDigits(false, beforeFrame, x, repetitions);
                    after[sample] = TimeSkippedDigits(true, afterFrame, x, repetitions);
                }
                else
                {
                    after[sample] = TimeSkippedDigits(true, afterFrame, x, repetitions);
                    before[sample] = TimeSkippedDigits(false, beforeFrame, x, repetitions);
                }
            }
            Compare(beforeFrame, afterFrame, "benchmark clipped digits " + scenario);
            Array.Sort(before);
            Array.Sort(after);
            Console.WriteLine("Host .NET numeric digit skip " + (scenario == 0 ? "right clipped" :
                scenario == 1 ? "left clipped" : "fully visible") + ": " + repetitions +
                " calls, median 5 alternating A/B; before " + before[2].ToString("F2") +
                " ms, after " + after[2].ToString("F2") + " ms (not QEMU FPS).");
        }
        Console.WriteLine("Numeric skip: " + checks +
            " exact framebuffers and call counts; 1000 calls = 0 B allocated.");
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
        SmallTextRenderer.DrawInt(null, -42, 0, 0, Color.White);
        foreach (int x in new[] { int.MinValue, int.MaxValue })
        {
            Canvas extreme = new Canvas(128, 48);
            SmallTextRenderer.DrawInt(extreme, long.MinValue, x, 0, Color.White);
            if (extreme.DrawCalls != 0)
                throw new Exception("Extreme signed number reached Canvas");
        }
        Console.WriteLine("Text range/number: 16 offscreen culls, 2 signed extreme " +
            "guards and 3 null-canvas guards.");
    }

    private static double TimeRange(bool optimized, Canvas canvas, string text, int repetitions, int x)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repetitions; i++)
        {
            if (optimized)
                SmallTextRenderer.DrawRange(canvas, text, 0, text.Length, x, 0, Color.White);
            else
                PreviousRange(canvas, text, 0, text.Length, x, 0, Color.White);
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    private static double TimeNumber(bool optimized, Canvas canvas, int repetitions, int x)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repetitions; i++)
        {
            if (optimized)
                SmallTextRenderer.DrawUInt(canvas, ulong.MaxValue, x, 0, Color.White);
            else
                PreviousUInt(canvas, ulong.MaxValue, x, 0, Color.White);
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    private static double TimeSigned(bool optimized, Canvas canvas, int repetitions, int x)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repetitions; i++)
        {
            if (optimized)
                SmallTextRenderer.DrawInt(canvas, long.MinValue, x, 0, Color.White);
            else
                PreviousInt(canvas, long.MinValue, x, 0, Color.White);
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    private static void Benchmark(string label, Func<bool, Canvas, int, double> time,
        bool expectNoDraw = true)
    {
        Canvas beforeFrame = new Canvas(128, 48);
        Canvas afterFrame = new Canvas(128, 48);
        time(false, beforeFrame, 100);
        time(true, afterFrame, 100);
        const int iterations = 10000;
        double[] baseline = new double[5];
        double[] optimized = new double[5];
        for (int i = 0; i < 5; i++)
        {
            if ((i & 1) == 0)
            {
                baseline[i] = time(false, beforeFrame, iterations);
                optimized[i] = time(true, afterFrame, iterations);
            }
            else
            {
                optimized[i] = time(true, afterFrame, iterations);
                baseline[i] = time(false, beforeFrame, iterations);
            }
        }
        Array.Sort(baseline);
        Array.Sort(optimized);
        if (expectNoDraw && afterFrame.DrawCalls != 0)
            throw new Exception("Optimized offscreen benchmark reached Canvas");
        for (int pixel = 0; pixel < beforeFrame.Pixels.Length; pixel++)
            if (beforeFrame.Pixels[pixel] != afterFrame.Pixels[pixel])
                throw new Exception(label + ": benchmark framebuffer mismatch");
        Console.WriteLine("Host .NET " + label + ": " + iterations +
            " calls, median of 5 alternating A/B; baseline " +
            baseline[2].ToString("F2") + " ms, optimized " +
            optimized[2].ToString("F2") + " ms (not QEMU FPS).");
    }

    internal static void Verify()
    {
        VerifyVisible();
        VerifyOffscreen();
        VerifySkippedDigits();
        string text = new string('X', 160);
        Benchmark("offscreen DrawRange", (optimized, canvas, iterations) =>
            TimeRange(optimized, canvas, text, iterations, 128));
        Benchmark("partially visible DrawRange", (optimized, canvas, iterations) =>
            TimeRange(optimized, canvas, text, iterations, -930), false);
        Benchmark("fully visible DrawRange", (optimized, canvas, iterations) =>
            TimeRange(optimized, canvas, "Task Manager", iterations, 0), false);
        Benchmark("offscreen DrawUInt", (optimized, canvas, iterations) =>
            TimeNumber(optimized, canvas, iterations, 128));
        Benchmark("partially visible DrawUInt", (optimized, canvas, iterations) =>
            TimeNumber(optimized, canvas, iterations, -100), false);
        Benchmark("fully visible DrawUInt", (optimized, canvas, iterations) =>
            TimeNumber(optimized, canvas, iterations, 0), false);
        Benchmark("partially visible DrawInt", (optimized, canvas, iterations) =>
            TimeSigned(optimized, canvas, iterations, -100), false);
        Benchmark("fully visible DrawInt", (optimized, canvas, iterations) =>
            TimeSigned(optimized, canvas, iterations, 0), false);
    }
}
