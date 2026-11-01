using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;

// Executes the production private DrawChar and checks all UTF-16 glyph rows
// against the original Glyph implementation. This is a host Canvas test,
// not a Cosmos boot, FPS measurement or a hardware graphics test.
internal static class TextUnicodeGlyphSmoke
{
    private static readonly Type Renderer = typeof(SmallTextRenderer);
    private static readonly Func<char, char> Normalize =
        (Func<char, char>)Renderer.GetMethod("NormalizeGlyph",
            BindingFlags.NonPublic | BindingFlags.Static)
            .CreateDelegate(typeof(Func<char, char>));
    private static readonly Func<char, int, int> Glyph =
        (Func<char, int, int>)Renderer.GetMethod("Glyph",
            BindingFlags.NonPublic | BindingFlags.Static)
            .CreateDelegate(typeof(Func<char, int, int>));
    private static readonly Action<Canvas, char, int, int, Color> Optimized =
        (Action<Canvas, char, int, int, Color>)Renderer.GetMethod("DrawChar",
            BindingFlags.NonPublic | BindingFlags.Static)
            .CreateDelegate(typeof(Action<Canvas, char, int, int, Color>));
    private static readonly byte[] Ascii = (byte[])Renderer.GetField(
        "CachedAsciiGlyphRows", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
    private static readonly byte[] Fallback = (byte[])Renderer.GetField(
        "CachedFallbackGlyphRows", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

    private static void Legacy(Canvas canvas, char value, int x, int y, Color color)
    {
        char ch = Normalize(value);
        if (ch == ' ')
            return;
        for (int row = 0; row < 7; row++)
        {
            int bits = Glyph(ch, row);
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
                canvas.DrawFilledRectangle(color, x + start, y + row, col - start, 1);
            }
        }
    }

    private static double Time(bool optimized, Canvas canvas, char[] glyphs, int repeats)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++)
        {
            char ch = glyphs[i % glyphs.Length];
            if (optimized)
                Optimized(canvas, ch, 1, 1, Color.White);
            else
                Legacy(canvas, ch, 1, 1, Color.White);
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    internal static void Verify()
    {
        if (Ascii.Length != 128 * 7 || Fallback.Length != 7)
            throw new Exception("Glyph cache has incorrect dimensions");

        int nonAscii = 0;
        for (int code = 0; code <= char.MaxValue; code++)
        {
            char ch = Normalize((char)code);
            // Independently verify the Latin fast-path bound. If a Unicode
            // code point above U+017F uppercases to ASCII on this runtime,
            // the optimized renderer must not silently change its glyph.
            if (ch > '\u017F' && char.ToUpperInvariant(ch) < 128)
                throw new Exception("Non-Latin uppercase reaches ASCII U+" +
                    code.ToString("X4"));
            int glyphIndex = ch < 128 ? ch : char.ToUpperInvariant(ch);
            if (glyphIndex >= 128)
                nonAscii++;
            for (int row = 0; row < 7; row++)
            {
                int cached = glyphIndex < 128 ? Ascii[glyphIndex * 7 + row] : Fallback[row];
                if (cached != Glyph(ch, row))
                    throw new Exception("Unicode glyph row mismatch U+" +
                        code.ToString("X4") + ", row " + row);
            }
        }

        // Includes ASCII, Polish normalization, non-ASCII fallback, Unicode
        // uppercasing and surrogate code units.
        string text = "ZonderqOS Zażółć gęślą jaźń \\漢字 \\Ω \\ı \\ſ \\K \\😀";
        text = text.Replace("\\", "");
        Canvas expected = new Canvas(1200, 30);
        Canvas actual = new Canvas(1200, 30);
        for (int i = 0; i < text.Length; i++)
        {
            Legacy(expected, text[i], 2 + i * 6, 4, Color.White);
            Optimized(actual, text[i], 2 + i * 6, 4, Color.White);
        }
        if (expected.DrawCalls != actual.DrawCalls)
            throw new Exception("Unicode glyph Canvas call count changed");
        for (int i = 0; i < expected.Pixels.Length; i++)
            if (expected.Pixels[i] != actual.Pixels[i])
                throw new Exception("Unicode glyph framebuffer mismatch at " + i);

        char[] fallbackText = new char[] { '漢', 'Ω', 'Ж', '中', 'あ', 'א', '♣', '☀' };
        Canvas benchmarkCanvas = new Canvas(16, 16);
        Time(false, benchmarkCanvas, fallbackText, 1000);
        Time(true, benchmarkCanvas, fallbackText, 1000);
        const int repeats = 20000;
        double[] before = new double[5];
        double[] after = new double[5];
        for (int sample = 0; sample < 5; sample++)
        {
            if ((sample & 1) == 0)
            {
                before[sample] = Time(false, benchmarkCanvas, fallbackText, repeats);
                after[sample] = Time(true, benchmarkCanvas, fallbackText, repeats);
            }
            else
            {
                after[sample] = Time(true, benchmarkCanvas, fallbackText, repeats);
                before[sample] = Time(false, benchmarkCanvas, fallbackText, repeats);
            }
        }
        Array.Sort(before);
        Array.Sort(after);

        long startAllocation = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            Optimized(benchmarkCanvas, fallbackText[i % fallbackText.Length], 1, 1, Color.White);
        long allocations = GC.GetAllocatedBytesForCurrentThread() - startAllocation;
        if (allocations != 0)
            throw new Exception("Warm Unicode glyph drawing allocated " + allocations + " B");

        Console.WriteLine("Unicode glyph cache: 65536 UTF-16 characters x 7 rows, " +
            nonAscii + " fallback code units, exact Canvas output, 0 B warmed allocations.");
        Console.WriteLine("Host .NET unsupported Unicode DrawChar: " + repeats +
            " calls, median 5 alternating A/B; legacy " + before[2].ToString("F2") +
            " ms, production " + after[2].ToString("F2") + " ms (not QEMU FPS).");
    }
}
