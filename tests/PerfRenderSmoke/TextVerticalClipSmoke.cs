using System;
using System.Drawing;
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
    }
}
