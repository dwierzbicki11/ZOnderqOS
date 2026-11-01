using System;
using System.Diagnostics;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;

// The baseline is the exact pre-change production source compiled in the
// same process. Host timings do not represent QEMU or real hardware FPS.
internal static class PanelVisibilitySmoke
{
    private static readonly Color Ink = Color.FromArgb(30, 55, 78);
    private static readonly Color Edge = Color.FromArgb(150, 175, 200);

    private static void Draw(bool optimized, int op, Canvas canvas,
        int x, int y, int width, int height, int radius)
    {
        if (op == 0)
        {
            if (optimized) UiPainter.RoundedPanel(canvas, x, y, width, height, radius, Ink, Edge);
            else UiPainterLegacy.RoundedPanel(canvas, x, y, width, height, radius, Ink, Edge);
        }
        else if (op == 1)
        {
            if (optimized) UiPainter.RoundedFill(canvas, x, y, width, height, radius, Ink);
            else UiPainterLegacy.RoundedFill(canvas, x, y, width, height, radius, Ink);
        }
        else
        {
            if (optimized) UiPainter.RoundedTop(canvas, x, y, width, height, radius, Ink);
            else UiPainterLegacy.RoundedTop(canvas, x, y, width, height, radius, Ink);
        }
    }

    private static double Time(bool optimized, int op, Canvas canvas, int repeats)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++)
            Draw(optimized, op, canvas, 600 + (i & 7) * 13, 350, 180, 100, 32);
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    internal static void Verify()
    {
        int[] xs = { -140, -30, -1, 0, 8, 100, 139, 140, 400 };
        int[] ys = { -110, -25, -1, 0, 15, 75, 99, 100, 300 };
        int[] radii = { 0, 1, 4, 12, 32, 48 };
        int checks = 0;
        for (int op = 0; op < 3; op++)
            foreach (int x in xs)
                foreach (int y in ys)
                    foreach (int radius in radii)
                    {
                        Canvas oldFrame = new Canvas(140, 100);
                        Canvas newFrame = new Canvas(140, 100);
                        Draw(false, op, oldFrame, x, y, 85, 65, radius);
                        Draw(true, op, newFrame, x, y, 85, 65, radius);
                        for (int i = 0; i < oldFrame.Pixels.Length; i++)
                            if (oldFrame.Pixels[i] != newFrame.Pixels[i])
                                throw new Exception("Panel pixel mismatch " + i +
                                    " op=" + op + " x=" + x + " y=" + y + " radius=" + radius);
                        checks++;
                    }

        Canvas extreme = new Canvas(140, 100);
        UiPainter.RoundedPanel(extreme, int.MaxValue, 0, 180, 100, 32, Ink, Edge);
        UiPainter.RoundedPanel(extreme, int.MinValue, 0, 180, 100, 32, Ink, Edge);
        UiPainter.RoundedFill(extreme, 0, int.MaxValue, 180, 100, 32, Ink);
        UiPainter.RoundedTop(extreme, 0, int.MinValue, 180, 100, 32, Ink);
        UiPainter.RoundedTop(null, 0, 0, 180, 100, 32, Ink);
        if (extreme.DrawCalls != 0)
            throw new Exception("Offscreen extreme coordinates touched Canvas");

        const int repeats = 20000;
        for (int op = 0; op < 3; op++)
        {
            Canvas beforeFrame = new Canvas(140, 100);
            Canvas afterFrame = new Canvas(140, 100);
            Time(false, op, beforeFrame, 1000);
            Time(true, op, afterFrame, 1000);
            double[] before = new double[5];
            double[] after = new double[5];
            for (int sample = 0; sample < 5; sample++)
            {
                if ((sample & 1) == 0)
                {
                    before[sample] = Time(false, op, beforeFrame, repeats);
                    after[sample] = Time(true, op, afterFrame, repeats);
                }
                else
                {
                    after[sample] = Time(true, op, afterFrame, repeats);
                    before[sample] = Time(false, op, beforeFrame, repeats);
                }
            }
            Array.Sort(before);
            Array.Sort(after);
            if (beforeFrame.DrawCalls != 0 || afterFrame.DrawCalls != 0)
                throw new Exception("Offscreen benchmark touched framebuffer");
            Console.WriteLine("Host .NET offscreen " +
                (op == 0 ? "RoundedPanel" : op == 1 ? "RoundedFill" : "RoundedTop") +
                ": " + repeats + " calls, median 5 alternating A/B; old " +
                before[2].ToString("F2") + " ms, culled " + after[2].ToString("F2") +
                " ms (not QEMU FPS).");
        }
        Console.WriteLine("Panel visibility: " + checks +
            " exact framebuffer comparisons and extreme/null guards.");
    }
}
