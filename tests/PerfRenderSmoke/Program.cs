using System;
using System.Drawing;
using System.Reflection;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;

internal static class Program
{
    private static readonly MethodInfo Glyph = typeof(SmallTextRenderer).GetMethod(
        "Glyph", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly MethodInfo Normalize = typeof(SmallTextRenderer).GetMethod(
        "NormalizeGlyph", BindingFlags.Static | BindingFlags.NonPublic);

    private static void LegacyDraw(Canvas canvas, string text, int x, int y, Color color)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char ch = (char)Normalize.Invoke(null, new object[] { text[i] });
            for (int row = 0; row < 7; row++)
            {
                int bits = (int)Glyph.Invoke(null, new object[] { ch, row });
                for (int col = 0; col < 5; col++)
                    if ((bits & (1 << (4 - col))) != 0)
                        canvas.DrawFilledRectangle(color, x + i * 6 + col, y + row, 1, 1);
            }
        }
    }

    private static void Verify(string label, string text, bool integerPath)
    {
        Canvas legacy = new Canvas(2800, 40);
        Canvas optimized = new Canvas(2800, 40);
        Color ink = Color.White;
        LegacyDraw(legacy, text, 2, 8, ink);
        if (integerPath)
            SmallTextRenderer.DrawInt(optimized, long.MinValue, 2, 8, ink);
        else
            SmallTextRenderer.Draw(optimized, text, 2, 8, ink);
        if (legacy.DrawCalls == 0 || optimized.DrawCalls >= legacy.DrawCalls)
            throw new Exception(label + ": draw-call reduction failed");
        for (int i = 0; i < legacy.Pixels.Length; i++)
            if (legacy.Pixels[i] != optimized.Pixels[i])
                throw new Exception(label + ": framebuffer mismatch at pixel " + i);
        Console.WriteLine(label + ": Canvas calls " + legacy.DrawCalls + " -> " +
            optimized.DrawCalls + "; framebuffer exact");
    }

    private static int Main()
    {
        if (Glyph == null || Normalize == null)
            throw new Exception("Production glyph methods unavailable");
        string ascii = "";
        for (int ch = 32; ch < 127; ch++)
            ascii += (char)ch;
        Verify("Printable ASCII 32..126", ascii, false);
        Verify("Polish glyph normalization", "Zażółć gęślą jaźń", false);
        Verify("Signed integer minimum", "-9223372036854775808", true);
        RoundedPanelSmoke.Verify();
        PanelVisibilitySmoke.Verify();
        CornerInsetSmoke.Verify();
        CornerBandSmoke.Verify();
        TextWhitespaceSmoke.Verify();
        TextNormalizeSmoke.Verify();
        TextUnicodeGlyphSmoke.Verify();
        IconBlitSmoke.Verify();
        IconCacheSmoke.Verify();
        IconTwoSizeCacheSmoke.Verify();
        IconDirectScaleSmoke.Verify();
        IconVisibilitySmoke.Verify();
        TextVisibilitySmoke.Verify();
        TextRangeNumberVisibilitySmoke.Verify();
        NumericDigitCountSmoke.Verify();
        TextPercentVisibilitySmoke.Verify();
        Console.WriteLine("PASS: production C# text/panel renderers tested with stub Canvas; not QEMU.");
        return 0;
    }
}
