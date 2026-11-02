using System;
using System.Diagnostics;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;

// Host-only cursor stub. The production Cursor.cs is deliberately not linked;
// the production CursorDamageRenderer.cs IS compiled and exercised.
namespace ZonderqOS.GUI
{
    public static class Cursor
    {
        public static void Draw(Canvas canvas, int x, int y)
        {
            for (int row = 0; row < 20; row++)
                for (int col = 0; col < 16; col++)
                    if ((row + col) % 3 == 0)
                        canvas.DrawPoint(Color.White, x + col, y + row);
        }
    }
}

internal static class CursorDamageSmoke
{
    // Exact pre-optimization implementation, retained solely as a host A/B
    // reference. No simulated kernel FPS or GPU transfer is claimed.
    private sealed class LegacyMover
    {
        private readonly Canvas canvas;
        private readonly Color[] saved = new Color[24 * 28];
        private int previousX, previousY;
        private bool valid;

        internal LegacyMover(Canvas canvas) { this.canvas = canvas; }

        internal void DrawAfterScene(int x, int y)
        {
            Capture(x, y);
            previousX = x;
            previousY = y;
            valid = true;
            Cursor.Draw(canvas, x, y);
        }

        internal bool TryMove(int x, int y)
        {
            if (!valid) return false;
            if (x == previousX && y == previousY) return true;
            Restore(previousX, previousY);
            Capture(x, y);
            previousX = x;
            previousY = y;
            Cursor.Draw(canvas, x, y);
            canvas.Display();
            return true;
        }

        private void Capture(int x, int y)
        {
            int baseX = x - 4, baseY = y - 4;
            int width = canvas.Width, height = canvas.Height;
            for (int row = 0; row < 28; row++)
            {
                int py = baseY + row, offset = row * 24;
                for (int col = 0; col < 24; col++)
                {
                    int px = baseX + col;
                    saved[offset + col] = px >= 0 && py >= 0 && px < width && py < height
                        ? canvas.GetPointColor(px, py) : Color.Black;
                }
            }
        }

        private void Restore(int x, int y)
        {
            int baseX = x - 4, baseY = y - 4;
            int width = canvas.Width, height = canvas.Height;
            for (int row = 0; row < 28; row++)
            {
                int py = baseY + row;
                if (py < 0 || py >= height) continue;
                int offset = row * 24;
                for (int col = 0; col < 24; col++)
                {
                    int px = baseX + col;
                    if (px >= 0 && px < width)
                        canvas.DrawPoint(saved[offset + col], px, py);
                }
            }
        }
    }

    private static void SamePixels(Canvas before, Canvas after, string label)
    {
        if (before.Pixels.Length != after.Pixels.Length)
            throw new Exception(label + ": size mismatch");
        for (int i = 0; i < before.Pixels.Length; i++)
            if (before.Pixels[i] != after.Pixels[i])
                throw new Exception(label + ": pixel mismatch at " + i);
        if (before.DisplayCalls != after.DisplayCalls)
            throw new Exception(label + ": Display count mismatch");
    }

    private static void VerifyPositions(int width, int height)
    {
        var oldCanvas = new Canvas(width, height);
        var newCanvas = new Canvas(width, height);
        var oldMover = new LegacyMover(oldCanvas);
        var newMover = new CursorDamageRenderer(newCanvas);
        oldMover.DrawAfterScene(10, 10);
        newMover.DrawAfterScene(10, 10);
        SamePixels(oldCanvas, newCanvas, "initial cursor");
        int[,] positions =
        {
            { 10, 10 }, { 11, 10 }, { 12, 11 }, { 0, 0 }, { -5, -4 },
            { 1, height - 1 }, { width - 1, 1 }, { width - 1, height - 1 },
            { width + 3, height + 3 }, { -30, -30 }, { width / 2, height / 2 },
            { 3, 4 }, { 4, 5 }, { width + 12, 2 }, { 5, height + 10 }
        };
        for (int i = 0; i < positions.GetLength(0); i++)
        {
            int x = positions[i, 0], y = positions[i, 1];
            if (!oldMover.TryMove(x, y) || !newMover.TryMove(x, y))
                throw new Exception("cursor move rejected at " + i);
            SamePixels(oldCanvas, newCanvas, "cursor move " + width + "x" + height + " #" + i);
        }
        newMover.Reset();
        if (newMover.TryMove(8, 8))
            throw new Exception("Reset must require full redraw");
    }

    private static void VerifyUniformAndPatterned()
    {
        foreach (bool patterned in new[] { false, true })
        {
            Canvas oldCanvas = new Canvas(128, 96);
            Canvas newCanvas = new Canvas(128, 96);
            if (patterned)
            {
                for (int y = 0; y < 96; y++)
                    for (int x = 0; x < 128; x++)
                    {
                        Color pixel = ((x + y) & 1) == 0 ? Color.Navy : Color.Maroon;
                        oldCanvas.DrawPoint(pixel, x, y);
                        newCanvas.DrawPoint(pixel, x, y);
                    }
            }
            else
            {
                oldCanvas.DrawFilledRectangle(Color.Navy, 0, 0, 128, 96);
                newCanvas.DrawFilledRectangle(Color.Navy, 0, 0, 128, 96);
            }

            var oldMover = new LegacyMover(oldCanvas);
            var newMover = new CursorDamageRenderer(newCanvas);
            oldMover.DrawAfterScene(30, 30);
            newMover.DrawAfterScene(30, 30);
            int oldCalls = oldCanvas.DrawCalls;
            int newCalls = newCanvas.DrawCalls;
            if (!oldMover.TryMove(60, 50) || !newMover.TryMove(60, 50))
                throw new Exception("cursor background move failed");
            SamePixels(oldCanvas, newCanvas, patterned ? "patterned restore" : "flat restore");
            int oldDelta = oldCanvas.DrawCalls - oldCalls;
            int newDelta = newCanvas.DrawCalls - newCalls;
            if (patterned && oldDelta != newDelta)
                throw new Exception("Patterned restore changed per-pixel Canvas calls");
            if (!patterned && oldDelta - newDelta != 24 * 28 - 1)
                throw new Exception("Flat restore should replace 672 points with one rectangle: " +
                    oldDelta + " -> " + newDelta);
            Console.WriteLine("Cursor restore " + (patterned ? "patterned" : "uniform") +
                ": Canvas calls " + oldDelta + " -> " + newDelta +
                " (includes cursor), exact framebuffer.");
        }
    }

    // Compare the identical production capture path with and without overlap
    // reuse. The old underlay is restored before each capture in both cases.
    private static void VerifyOverlapReadbacks(bool patterned)
    {
        var beforeCanvas = new Canvas(128, 96);
        var afterCanvas = new Canvas(128, 96);
        for (int y = 0; y < 96; y++)
            for (int x = 0; x < 128; x++)
            {
                Color color = patterned && ((x + y) & 1) != 0 ? Color.Maroon : Color.Navy;
                beforeCanvas.DrawPoint(color, x, y);
                afterCanvas.DrawPoint(color, x, y);
            }
        var before = new CursorDamageRenderer(beforeCanvas);
        var after = new CursorDamageRenderer(afterCanvas);
        try
        {
            before.DrawAfterScene(40, 40);
            after.DrawAfterScene(40, 40);
            int oldReads = beforeCanvas.ReadbackCalls;
            int newReads = afterCanvas.ReadbackCalls;
            CursorDamageRenderer.DisableOverlapReuseForBenchmark = true;
            if (!before.TryMove(41, 40)) throw new Exception("baseline cursor move failed");
            CursorDamageRenderer.DisableOverlapReuseForBenchmark = false;
            if (!after.TryMove(41, 40)) throw new Exception("overlap cursor move failed");
            SamePixels(beforeCanvas, afterCanvas, "overlap first move");
            int expectedOptimizedReads = patterned ? 24 * 28 : 28;
            if (beforeCanvas.ReadbackCalls - oldReads != 24 * 28 ||
                afterCanvas.ReadbackCalls - newReads != expectedOptimizedReads)
                throw new Exception("1px cursor readback policy mismatch: expected 672 -> " +
                    expectedOptimizedReads + ", got " +
                    (beforeCanvas.ReadbackCalls - oldReads) + " -> " +
                    (afterCanvas.ReadbackCalls - newReads));

            // Include diagonal moves, clipped rectangles and non-overlapping
            // jumps. The optimized path must never return stale old pixels.
            for (int i = 0; i < 150; i++)
            {
                int x = i % 17 == 0 ? -2 : i % 23 == 0 ? 125 : 35 + (i % 11);
                int y = i % 19 == 0 ? 95 : i % 29 == 0 ? -3 : 32 + (i % 9);
                CursorDamageRenderer.DisableOverlapReuseForBenchmark = true;
                if (!before.TryMove(x, y)) throw new Exception("baseline move failed " + i);
                CursorDamageRenderer.DisableOverlapReuseForBenchmark = false;
                if (!after.TryMove(x, y)) throw new Exception("overlap move failed " + i);
                SamePixels(beforeCanvas, afterCanvas, "overlap " + (patterned ? "pattern" : "flat") + " #" + i);
            }
            if (patterned && afterCanvas.ReadbackCalls != beforeCanvas.ReadbackCalls)
                throw new Exception("patterned cursor must retain the one-pass readback path");
            if (!patterned && afterCanvas.ReadbackCalls >= beforeCanvas.ReadbackCalls)
                throw new Exception("uniform cursor overlap did not reduce readback calls");
            Console.WriteLine("Cursor overlap " + (patterned ? "patterned" : "uniform") +
                " 151 moves: Canvas readbacks " + beforeCanvas.ReadbackCalls +
                " -> " + afterCanvas.ReadbackCalls + ", exact framebuffer.");
        }
        finally
        {
            CursorDamageRenderer.DisableOverlapReuseForBenchmark = false;
        }
    }

    private static double TimeOverlap(bool disabled, int repeats, bool patterned)
    {
        var canvas = new Canvas(128, 96);
        if (patterned)
        {
            for (int row = 0; row < 96; row++)
                for (int col = 0; col < 128; col++)
                    canvas.DrawPoint(((row + col) & 1) == 0 ? Color.Navy : Color.Maroon, col, row);
        }
        else
            canvas.DrawFilledRectangle(Color.Navy, 0, 0, 128, 96);
        CursorDamageRenderer.DisableOverlapReuseForBenchmark = disabled;
        var mover = new CursorDamageRenderer(canvas);
        mover.DrawAfterScene(40, 40);
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++)
        {
            int x = 40 + i % 8;
            int y = 40 + (i / 8) % 6;
            mover.TryMove(x, y);
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    private static void VerifyMixedUnderlay()
    {
        var baselineCanvas = new Canvas(160, 100);
        var optimizedCanvas = new Canvas(160, 100);
        for (int y = 0; y < 100; y++)
            for (int x = 0; x < 160; x++)
            {
                Color pixel = x < 80 && ((x + y) & 1) != 0 ? Color.Maroon : Color.Navy;
                baselineCanvas.DrawPoint(pixel, x, y);
                optimizedCanvas.DrawPoint(pixel, x, y);
            }
        var baseline = new CursorDamageRenderer(baselineCanvas);
        var optimized = new CursorDamageRenderer(optimizedCanvas);
        int[] xs = { 40, 41, 55, 69, 78, 82, 96, 105, 108, 106, 95, 79, 68, 50, 45, 88, 109, 40 };
        try
        {
            baseline.DrawAfterScene(xs[0], 40);
            optimized.DrawAfterScene(xs[0], 40);
            for (int i = 1; i < xs.Length; i++)
            {
                CursorDamageRenderer.DisableOverlapReuseForBenchmark = true;
                if (!baseline.TryMove(xs[i], 40)) throw new Exception("mixed baseline move " + i);
                CursorDamageRenderer.DisableOverlapReuseForBenchmark = false;
                if (!optimized.TryMove(xs[i], 40)) throw new Exception("mixed optimized move " + i);
                SamePixels(baselineCanvas, optimizedCanvas, "mixed underlay transition #" + i);
            }
        }
        finally
        {
            CursorDamageRenderer.DisableOverlapReuseForBenchmark = false;
        }
        Console.WriteLine("Cursor overlap mixed patterned/flat underlay transitions: exact framebuffer.");
    }

    // Compare the production per-row overlap path against a single contiguous
    // Array.Copy. Both use the same captured pixels and identical Canvas API.
    private static void VerifyVerticalBulkCopy()
    {
        var baselineCanvas = new Canvas(128, 96);
        var optimizedCanvas = new Canvas(128, 96);
        baselineCanvas.DrawFilledRectangle(Color.Navy, 0, 0, 128, 96);
        optimizedCanvas.DrawFilledRectangle(Color.Navy, 0, 0, 128, 96);
        var baseline = new CursorDamageRenderer(baselineCanvas);
        var optimized = new CursorDamageRenderer(optimizedCanvas);
        try
        {
            baseline.DrawAfterScene(40, 40);
            optimized.DrawAfterScene(40, 40);
            for (int i = 0; i < 250; i++)
            {
                int y = 40 + (i % 13);
                CursorDamageRenderer.DisableVerticalBulkCopyForBenchmark = true;
                if (!baseline.TryMove(40, y))
                    throw new Exception("vertical baseline move failed " + i);
                CursorDamageRenderer.DisableVerticalBulkCopyForBenchmark = false;
                if (!optimized.TryMove(40, y))
                    throw new Exception("vertical optimized move failed " + i);
                SamePixels(baselineCanvas, optimizedCanvas, "vertical bulk #" + i);
            }
            if (baselineCanvas.ReadbackCalls != optimizedCanvas.ReadbackCalls)
                throw new Exception("vertical bulk changed Canvas readback count");
        }
        finally
        {
            CursorDamageRenderer.DisableVerticalBulkCopyForBenchmark = false;
        }
        Console.WriteLine("PASS: 250 vertical cursor moves, exact framebuffer and readback count");
    }

    private static void VerifyVerticalUnderlayTransitions()
    {
        var baselineCanvas = new Canvas(128, 96);
        var optimizedCanvas = new Canvas(128, 96);
        for (int y = 0; y < 96; y++)
        {
            Color color = y < 55 ? Color.Navy : Color.Maroon;
            baselineCanvas.DrawFilledRectangle(color, 0, y, 128, 1);
            optimizedCanvas.DrawFilledRectangle(color, 0, y, 128, 1);
        }
        var baseline = new CursorDamageRenderer(baselineCanvas);
        var optimized = new CursorDamageRenderer(optimizedCanvas);
        try
        {
            baseline.DrawAfterScene(40, 20);
            optimized.DrawAfterScene(40, 20);
            // Walk through the color boundary, then return. The fast path
            // must disable itself on a mixed capture and re-enable after
            // the full 24x28 region becomes uniform again.
            for (int i = 0; i < 130; i++)
            {
                int y = i < 65 ? 20 + i : 84 - (i - 65);
                CursorDamageRenderer.DisableVerticalBulkCopyForBenchmark = true;
                if (!baseline.TryMove(40, y))
                    throw new Exception("vertical transition baseline failed " + i);
                CursorDamageRenderer.DisableVerticalBulkCopyForBenchmark = false;
                if (!optimized.TryMove(40, y))
                    throw new Exception("vertical transition optimized failed " + i);
                SamePixels(baselineCanvas, optimizedCanvas, "vertical color transition #" + i);
            }
            if (baselineCanvas.ReadbackCalls != optimizedCanvas.ReadbackCalls)
                throw new Exception("vertical transition changed readback count");
        }
        finally
        {
            CursorDamageRenderer.DisableVerticalBulkCopyForBenchmark = false;
        }
        Console.WriteLine("PASS: vertical mixed-to-uniform transitions, exact framebuffer and readback count");
    }

    private static double TimeVerticalBulk(bool disable, int repeats)
    {
        var canvas = new Canvas(128, 96);
        canvas.DrawFilledRectangle(Color.Navy, 0, 0, 128, 96);
        CursorDamageRenderer.DisableVerticalBulkCopyForBenchmark = disable;
        var mover = new CursorDamageRenderer(canvas);
        mover.DrawAfterScene(40, 40);
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++)
            mover.TryMove(40, 40 + i % 8);
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    private static void BenchmarkVerticalBulk()
    {
        try
        {
            TimeVerticalBulk(true, 200);
            TimeVerticalBulk(false, 200);
            double[] before = new double[7], after = new double[7];
            for (int i = 0; i < 7; i++)
            {
                if ((i & 1) == 0)
                {
                    before[i] = TimeVerticalBulk(true, 4000);
                    after[i] = TimeVerticalBulk(false, 4000);
                }
                else
                {
                    after[i] = TimeVerticalBulk(false, 4000);
                    before[i] = TimeVerticalBulk(true, 4000);
                }
            }
            Array.Sort(before);
            Array.Sort(after);
            Console.WriteLine("HOST A/B vertical-only cursor 4000 moves median 7: per-row=" +
                before[3].ToString("F3") + " ms bulk=" + after[3].ToString("F3") +
                " ms ratio=" + (before[3] / after[3]).ToString("F2") +
                "x (fake Canvas, not QEMU FPS)");
        }
        finally
        {
            CursorDamageRenderer.DisableVerticalBulkCopyForBenchmark = false;
        }
    }

    private static void BenchmarkOverlap()
    {
        try
        {
            foreach (bool patterned in new[] { false, true })
            {
                TimeOverlap(true, 200, patterned);
                TimeOverlap(false, 200, patterned);
                double[] before = new double[7], after = new double[7];
                for (int i = 0; i < 7; i++)
                {
                    if ((i & 1) == 0)
                    {
                        before[i] = TimeOverlap(true, 4000, patterned);
                        after[i] = TimeOverlap(false, 4000, patterned);
                    }
                    else
                    {
                        after[i] = TimeOverlap(false, 4000, patterned);
                        before[i] = TimeOverlap(true, 4000, patterned);
                    }
                }
                Array.Sort(before);
                Array.Sort(after);
                Console.WriteLine("Host .NET cursor overlap " + (patterned ? "patterned" : "flat") +
                    ", 4000 small moves, median 7 A/B: " +
                    before[3].ToString("F3") + " -> " + after[3].ToString("F3") +
                    " ms, ratio=" + (before[3] / after[3]).ToString("F2") +
                    "x (fake Canvas, not QEMU FPS).");
            }
        }
        finally
        {
            CursorDamageRenderer.DisableOverlapReuseForBenchmark = false;
        }
    }

    private static double TimeUniformCurrent(bool disableUniform, int repeats)
    {
        var canvas = new Canvas(128, 96);
        canvas.DrawFilledRectangle(Color.Navy, 0, 0, 128, 96);
        CursorDamageRenderer.DisableUniformRestoreForBenchmark = disableUniform;
        var mover = new CursorDamageRenderer(canvas);
        mover.DrawAfterScene(20, 20);
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++)
        {
            int x = 10 + (i * 13) % 100;
            int y = 10 + (i * 17) % 70;
            mover.TryMove(x, y);
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    private static void BenchmarkUniformCurrent()
    {
        try
        {
            TimeUniformCurrent(true, 200);
            TimeUniformCurrent(false, 200);
            double[] before = new double[7], after = new double[7];
            for (int i = 0; i < 7; i++)
            {
                if ((i & 1) == 0)
                {
                    before[i] = TimeUniformCurrent(true, 4000);
                    after[i] = TimeUniformCurrent(false, 4000);
                }
                else
                {
                    after[i] = TimeUniformCurrent(false, 4000);
                    before[i] = TimeUniformCurrent(true, 4000);
                }
            }
            Array.Sort(before);
            Array.Sort(after);
            Console.WriteLine("Host .NET cursor flat underlay, 4000 moves, median 7 A/B " +
                "identical capture, old point restore=" + before[3].ToString("F3") +
                " ms, new uniform fill=" + after[3].ToString("F3") +
                " ms (fake Canvas; not QEMU FPS).");
        }
        finally
        {
            CursorDamageRenderer.DisableUniformRestoreForBenchmark = false;
        }
    }

    private static double TimePatterned(bool optimized, int repeats)
    {
        var canvas = new Canvas(128, 96);
        for (int y = 0; y < 96; y++)
            for (int x = 0; x < 128; x++)
                canvas.DrawPoint(((x + y) & 1) == 0 ? Color.Navy : Color.Maroon, x, y);
        var oldMover = optimized ? null : new LegacyMover(canvas);
        var newMover = optimized ? new CursorDamageRenderer(canvas) : null;
        if (optimized) newMover.DrawAfterScene(20, 20);
        else oldMover.DrawAfterScene(20, 20);
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++)
        {
            int x = 10 + (i * 13) % 100;
            int y = 10 + (i * 17) % 70;
            if (optimized) newMover.TryMove(x, y);
            else oldMover.TryMove(x, y);
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    private static void BenchmarkPatterned()
    {
        TimePatterned(false, 200);
        TimePatterned(true, 200);
        double[] before = new double[7], after = new double[7];
        for (int i = 0; i < 7; i++)
        {
            if ((i & 1) == 0)
            {
                before[i] = TimePatterned(false, 3000);
                after[i] = TimePatterned(true, 3000);
            }
            else
            {
                after[i] = TimePatterned(true, 3000);
                before[i] = TimePatterned(false, 3000);
            }
        }
        Array.Sort(before);
        Array.Sort(after);
        Console.WriteLine("Host .NET patterned cursor 3000 moves, median 7 A/B: " +
            before[3].ToString("F2") + " -> " + after[3].ToString("F2") +
            " ms (fake Canvas, not QEMU FPS).");
    }

    private static double Time(bool optimized, int repeats, bool edge)
    {
        var canvas = new Canvas(128, 96);
        var oldMover = optimized ? null : new LegacyMover(canvas);
        var newMover = optimized ? new CursorDamageRenderer(canvas) : null;
        if (optimized) newMover.DrawAfterScene(0, 0);
        else oldMover.DrawAfterScene(0, 0);
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++)
        {
            int x = edge ? i % 6 : i % 7 == 0 ? -3 : (i * 13) % 119;
            int y = edge ? i % 8 : i % 11 == 0 ? 91 : (i * 17) % 90;
            if (optimized) newMover.TryMove(x, y);
            else oldMover.TryMove(x, y);
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    internal static void Verify()
    {
        VerifyPositions(128, 96);
        VerifyPositions(24, 28);
        VerifyPositions(5, 7);
        VerifyUniformAndPatterned();
        VerifyOverlapReadbacks(false);
        VerifyOverlapReadbacks(true);
        VerifyMixedUnderlay();
        VerifyVerticalBulkCopy();
        VerifyVerticalUnderlayTransitions();
        BenchmarkVerticalBulk();
        BenchmarkOverlap();
        BenchmarkUniformCurrent();
        BenchmarkPatterned();
        // Very large coordinates must be clipped without arithmetic overflow.
        var extremeCanvas = new Canvas(16, 16);
        var extreme = new CursorDamageRenderer(extremeCanvas);
        extreme.DrawAfterScene(int.MinValue, int.MinValue);
        if (!extreme.TryMove(int.MaxValue - 20, int.MaxValue - 20) ||
            !extreme.TryMove(1, 1))
            throw new Exception("extreme coordinates rejected");

        // Cursor-only motion must not allocate a new pixel buffer, bitmap,
        // delegate or temporary collection per move after initialization.
        var allocationCanvas = new Canvas(128, 96);
        var allocationMover = new CursorDamageRenderer(allocationCanvas);
        allocationMover.DrawAfterScene(5, 5);
        for (int i = 0; i < 200; i++)
            allocationMover.TryMove(i % 117, i % 87);
        long startBytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 5000; i++)
            allocationMover.TryMove(i % 117, i % 87);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - startBytes;
        if (allocated != 0)
            throw new Exception("Cursor motion allocated " + allocated + " B per 5000 moves");

        foreach (bool edge in new[] { false, true })
        {
            Time(false, 300, edge);
            Time(true, 300, edge);
            double[] before = new double[7], after = new double[7];
            for (int sample = 0; sample < 7; sample++)
            {
                if ((sample & 1) == 0)
                {
                    before[sample] = Time(false, 4000, edge);
                    after[sample] = Time(true, 4000, edge);
                }
                else
                {
                    after[sample] = Time(true, 4000, edge);
                    before[sample] = Time(false, 4000, edge);
                }
            }
            Array.Sort(before);
            Array.Sort(after);
            Console.WriteLine("Host .NET cursor damage " + (edge ? "screen edge" : "mixed positions") +
                " 4000 moves: old per-pixel bounds=" + before[3].ToString("F2") +
                "ms clipped rectangles=" + after[3].ToString("F2") +
                "ms ratio=" + (before[3] / after[3]).ToString("F2") +
                "x (7 alternating samples; fake Canvas, not QEMU FPS)");
        }
        Console.WriteLine("PASS: cursor damage exact framebuffer, edge clipping, reset, extreme coordinates and 0 B per 5000 warmed moves");
    }
}
