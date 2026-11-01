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

    private static void VerifyTallClippedPanel(int y)
    {
        Canvas baseline = new Canvas(140, 100);
        Canvas optimized = new Canvas(140, 100);
        UiPainterLegacy.RoundedPanel(baseline, -450, y, 1024, 1024, 512, Ink, Edge);
        UiPainter.RoundedPanel(optimized, -450, y, 1024, 1024, 512, Ink, Edge);
        if (baseline.DrawCalls != optimized.DrawCalls)
            throw new Exception("Tall clipped panel changed Canvas calls at y=" + y +
                ": " + baseline.DrawCalls + " vs " + optimized.DrawCalls);
        for (int i = 0; i < baseline.Pixels.Length; i++)
            if (baseline.Pixels[i] != optimized.Pixels[i])
                throw new Exception("Tall clipped panel changed pixel " + i + " at y=" + y);
    }

    private static double TimeTallPanel(bool optimized, Canvas canvas, int repeats)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++)
        {
            if (optimized)
                UiPainter.RoundedPanel(canvas, -450, -1020, 1024, 1024, 512, Ink, Edge);
            else
                UiPainterLegacy.RoundedPanel(canvas, -450, -1020, 1024, 1024, 512, Ink, Edge);
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    private static void BenchmarkTallPanel()
    {
        const int repeats = 1500;
        Canvas beforeFrame = new Canvas(140, 100);
        Canvas afterFrame = new Canvas(140, 100);
        TimeTallPanel(false, beforeFrame, 100);
        TimeTallPanel(true, afterFrame, 100);
        double[] before = new double[7];
        double[] after = new double[7];
        for (int sample = 0; sample < 7; sample++)
        {
            if ((sample & 1) == 0)
            {
                before[sample] = TimeTallPanel(false, beforeFrame, repeats);
                after[sample] = TimeTallPanel(true, afterFrame, repeats);
            }
            else
            {
                after[sample] = TimeTallPanel(true, afterFrame, repeats);
                before[sample] = TimeTallPanel(false, beforeFrame, repeats);
            }
        }
        Array.Sort(before);
        Array.Sort(after);
        if (beforeFrame.DrawCalls != afterFrame.DrawCalls || beforeFrame.DrawCalls == 0)
            throw new Exception("Tall panel benchmark changed visible Canvas calls");
        Console.WriteLine("Host .NET tall vertically clipped RoundedPanel: " +
            repeats + " calls, median 7 alternating A/B, previous " +
            before[3].ToString("F3") + " ms, optimized " +
            after[3].ToString("F3") + " ms (not QEMU FPS).");
    }

    private static double Time(bool optimized, int op, Canvas canvas, int repeats,
        bool onscreen)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++)
        {
            if (onscreen)
                Draw(optimized, op, canvas, 15, 12, 60, 42, 12);
            else
                Draw(optimized, op, canvas, 600 + (i & 7) * 13, 350, 180, 100, 32);
        }
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

        foreach (int y in new[] { -1023, -1020, -1015, -1000, -800, -512,
                                  -508, -500, -1, 0, 99 })
            VerifyTallClippedPanel(y);
        BenchmarkTallPanel();

        // Fill/Top are unchanged production methods and serve as negative
        // controls for runner/JIT timing noise; do not interpret small deltas
        // in RoundedPanel without comparing these controls.
        // Benchmark both cases. The culling guard must not hide a meaningful
        // regression in the common fully visible drawing path.
        for (int mode = 0; mode < 2; mode++)
        {
            bool onscreen = mode == 1;
            int repeats = onscreen ? 5000 : 20000;
            for (int op = 0; op < 3; op++)
            {
                Canvas beforeFrame = new Canvas(140, 100);
                Canvas afterFrame = new Canvas(140, 100);
                Time(false, op, beforeFrame, 100, onscreen);
                Time(true, op, afterFrame, 100, onscreen);
                double[] before = new double[5];
                double[] after = new double[5];
                for (int sample = 0; sample < 5; sample++)
                {
                    if ((sample & 1) == 0)
                    {
                        before[sample] = Time(false, op, beforeFrame, repeats, onscreen);
                        after[sample] = Time(true, op, afterFrame, repeats, onscreen);
                    }
                    else
                    {
                        after[sample] = Time(true, op, afterFrame, repeats, onscreen);
                        before[sample] = Time(false, op, beforeFrame, repeats, onscreen);
                    }
                }
                Array.Sort(before);
                Array.Sort(after);
                if (!onscreen && (beforeFrame.DrawCalls != 0 || afterFrame.DrawCalls != 0))
                    throw new Exception("Offscreen benchmark touched framebuffer");
                if (onscreen && (beforeFrame.DrawCalls == 0 || afterFrame.DrawCalls == 0))
                    throw new Exception("Onscreen benchmark did not draw");
                Console.WriteLine("Host .NET " + (onscreen ? "onscreen " : "offscreen ") +
                    (op == 0 ? "RoundedPanel" : op == 1 ? "RoundedFill" : "RoundedTop") +
                    ": " + repeats + " calls, median 5 alternating A/B; old " +
                    before[2].ToString("F2") + " ms, current " + after[2].ToString("F2") +
                    " ms (not QEMU FPS).");
            }
        }

        Canvas allocationFrame = new Canvas(140, 100);
        UiPainter.RoundedPanel(allocationFrame, 15, 12, 60, 42, 12, Ink, Edge);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            UiPainter.RoundedPanel(allocationFrame, 15, 12, 60, 42, 12, Ink, Edge);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        if (allocated != 0)
            throw new Exception("Visible panel drawing allocated " + allocated + " bytes");
        Console.WriteLine("Panel visibility: " + checks +
            " exact framebuffer comparisons, extreme/null guards, 0 B warm visible allocations.");
    }
}
