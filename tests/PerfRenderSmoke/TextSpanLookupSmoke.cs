using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;

internal static class TextSpanLookupSmoke
{
    private static readonly MethodInfo Glyph = typeof(SmallTextRenderer).GetMethod(
        "Glyph", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly FieldInfo SpanTable = typeof(SmallTextRenderer).GetField(
        "CachedSpanRuns", BindingFlags.Static | BindingFlags.NonPublic);
    private static long sink;

    private static byte[] BuildLegacyRows(string text)
    {
        if (Glyph == null)
            throw new Exception("Production Glyph method unavailable");
        byte[] rows = new byte[text.Length * 7];
        for (int i = 0; i < text.Length; i++)
            for (int row = 0; row < 7; row++)
                rows[i * 7 + row] = (byte)(int)Glyph.Invoke(null, new object[] { text[i], row });
        return rows;
    }

    // Old production per-row span scanner. Prepared glyph rows remove
    // reflection and glyph decoding from BOTH timed paths.
    private static void LegacySpans(Canvas canvas, byte[] rows, int characters, int x, int y)
    {
        for (int ch = 0; ch < characters; ch++)
        {
            int glyphX = x + ch * 6;
            for (int row = 0; row < 7; row++)
            {
                int bits = rows[ch * 7 + row];
                if (bits == 0)
                    continue;
                for (int col = 0; col < 5;)
                {
                    if ((bits & (1 << (4 - col))) == 0)
                    {
                        col++;
                        continue;
                    }
                    int start = col++;
                    while (col < 5 && (bits & (1 << (4 - col))) != 0)
                        col++;
                    canvas.DrawFilledRectangle(Color.White, glyphX + start, y + row, col - start, 1);
                }
            }
        }
    }

    private static void VerifyPackedRows()
    {
        if (SpanTable == null)
            throw new Exception("Production packed-span table unavailable");
        int[] table = (int[])SpanTable.GetValue(null);
        if (table.Length != 32)
            throw new Exception("Packed span table must contain exactly 32 patterns");
        for (int bits = 0; bits < 32; bits++)
        {
            int packed = table[bits];
            int count = packed & 3;
            packed >>= 2;
            int reconstructed = 0;
            int lastEnd = 0;
            for (int run = 0; run < count; run++)
            {
                int start = packed & 7;
                int length = (packed >> 3) & 7;
                if (length == 0 || start < lastEnd || start + length > 5)
                    throw new Exception("Invalid packed glyph span for bits=" + bits);
                for (int col = start; col < start + length; col++)
                    reconstructed |= 1 << (4 - col);
                lastEnd = start + length + 1;
                packed >>= 6;
            }
            if (packed != 0 || reconstructed != bits)
                throw new Exception("Packed glyph span mismatch for bits=" + bits);
        }
        Console.WriteLine("PASS: all 32 five-bit glyph patterns reconstruct exactly");
    }

    private static double Measure(Canvas canvas, byte[] rows, string text, bool packed, int repetitions)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repetitions; i++)
        {
            if (packed)
                SmallTextRenderer.Draw(canvas, text, 8, 8, Color.White);
            else
                LegacySpans(canvas, rows, text.Length, 8, 8);
        }
        sink += canvas.DrawCalls;
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    internal static void Verify()
    {
        VerifyPackedRows();
        string ascii = "";
        for (int ch = 32; ch < 127; ch++)
            ascii += (char)ch;
        string[] samples = { ascii, "CPU 123% PID READY 8MiB ZOnderqOS /root/foo.txt", "0 1 2 3 4 5 6 7 8 9" };
        foreach (string sample in samples)
        {
            byte[] rows = BuildLegacyRows(sample);
            Canvas baseline = new Canvas(800, 40);
            Canvas optimized = new Canvas(800, 40);
            LegacySpans(baseline, rows, sample.Length, 8, 8);
            SmallTextRenderer.Draw(optimized, sample, 8, 8, Color.White);
            if (baseline.DrawCalls != optimized.DrawCalls)
                throw new Exception("Glyph span Canvas call count changed");
            for (int i = 0; i < baseline.Pixels.Length; i++)
                if (baseline.Pixels[i] != optimized.Pixels[i])
                    throw new Exception("Glyph span pixel mismatch at " + i);
        }
        Console.WriteLine("PASS: 3 exact framebuffer and Canvas-call comparisons");

        const string text = "CPU 123% PID READY 8MiB ZOnderqOS /root/foo.txt";
        byte[] prepared = BuildLegacyRows(text);
        Canvas oldCanvas = new Canvas(800, 40);
        Canvas newCanvas = new Canvas(800, 40);
        Measure(oldCanvas, prepared, text, false, 100);
        Measure(newCanvas, prepared, text, true, 100);
        const int iterations = 1500;
        double[] oldMs = new double[7];
        double[] newMs = new double[7];
        for (int sample = 0; sample < 7; sample++)
        {
            if ((sample & 1) == 0)
            {
                oldMs[sample] = Measure(oldCanvas, prepared, text, false, iterations);
                newMs[sample] = Measure(newCanvas, prepared, text, true, iterations);
            }
            else
            {
                newMs[sample] = Measure(newCanvas, prepared, text, true, iterations);
                oldMs[sample] = Measure(oldCanvas, prepared, text, false, iterations);
            }
        }
        Array.Sort(oldMs);
        Array.Sort(newMs);
        Console.WriteLine("HOST A/B 1500 ASCII text draws, median 7 alternating samples: " +
            "legacy span scan=" + oldMs[3].ToString("F3") + " ms, packed lookup=" +
            newMs[3].ToString("F3") + " ms, ratio=" +
            (oldMs[3] / newMs[3]).ToString("F2") +
            "x (fake Canvas, not Cosmos FPS), sink=" + sink);
    }
}
