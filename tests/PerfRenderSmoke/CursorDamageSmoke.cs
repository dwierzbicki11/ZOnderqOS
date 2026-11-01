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

    private static double Time(bool optimized, int repeats)
    {
        var canvas = new Canvas(128, 96);
        var oldMover = optimized ? null : new LegacyMover(canvas);
        var newMover = optimized ? new CursorDamageRenderer(canvas) : null;
        if (optimized) newMover.DrawAfterScene(0, 0);
        else oldMover.DrawAfterScene(0, 0);
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++)
        {
            int x = i % 7 == 0 ? -3 : (i * 13) % 119;
            int y = i % 11 == 0 ? 91 : (i * 17) % 90;
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
        // Very large coordinates must be clipped without arithmetic overflow.
        var extremeCanvas = new Canvas(16, 16);
        var extreme = new CursorDamageRenderer(extremeCanvas);
        extreme.DrawAfterScene(int.MinValue, int.MinValue);
        if (!extreme.TryMove(int.MaxValue - 20, int.MaxValue - 20) ||
            !extreme.TryMove(1, 1))
            throw new Exception("extreme coordinates rejected");

        Time(false, 300);
        Time(true, 300);
        double[] before = new double[7], after = new double[7];
        for (int sample = 0; sample < 7; sample++)
        {
            if ((sample & 1) == 0)
            {
                before[sample] = Time(false, 4000);
                after[sample] = Time(true, 4000);
            }
            else
            {
                after[sample] = Time(true, 4000);
                before[sample] = Time(false, 4000);
            }
        }
        Array.Sort(before);
        Array.Sort(after);
        Console.WriteLine("Host .NET cursor damage 4000 moves: old per-pixel bounds=" +
            before[3].ToString("F2") + "ms clipped rectangles=" +
            after[3].ToString("F2") + "ms ratio=" +
            (before[3] / after[3]).ToString("F2") +
            "x (7 alternating samples; fake Canvas, not QEMU FPS)");
        Console.WriteLine("PASS: cursor damage exact framebuffer, edge clipping, reset and extreme-coordinate checks");
    }
}
