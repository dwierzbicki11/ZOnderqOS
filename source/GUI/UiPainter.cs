using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;

namespace ZonderqOS.GUI
{
    /// <summary>
    /// Clipped rounded rectangles for the Cosmos 3.0.95 software Canvas.
    /// Full-height content is drawn in one fast filled rectangle; only the
    /// two corner strips use a small number of scanlines. No per-pixel
    /// trigonometry, frame buffers, bitmaps or heap allocations.
    /// </summary>
    internal static class UiPainter
    {
        // Reject fully offscreen panels before O(radius) corner scans.
        // Wide coordinates avoid overflow at extreme window offsets.
        private static bool IsInvisible(Canvas canvas, int x, int y, int width, int height)
        {
            // Most windows are inside the framebuffer: two unsigned bounds
            // checks avoid computing any wide edges on that hot path. Negative
            // coordinates fall into the slow path and remain partially visible
            // when their right/bottom edge still crosses the framebuffer.
            if ((uint)x >= (uint)canvas.Width &&
                (x >= canvas.Width || (long)x + width <= 0))
                return true;
            if ((uint)y >= (uint)canvas.Height &&
                (y >= canvas.Height || (long)y + height <= 0))
                return true;
            return false;
        }

        internal static void RoundedPanel(Canvas canvas, int x, int y, int width, int height,
            int radius, Color fill, Color border)
        {
            if (canvas == null || width <= 0 || height <= 0)
                return;
            if (IsInvisible(canvas, x, y, width, height))
                return;

            int r = Math.Min(Math.Min(radius, width / 2), height / 2);
            if (r <= 0)
            {
                Fill(canvas, fill, x, y, width, height);
                Horizontal(canvas, border, x, y, width);
                Horizontal(canvas, border, x, y + height - 1, width);
                Fill(canvas, border, x, y, 1, height);
                Fill(canvas, border, x + width - 1, y, 1, height);
                return;
            }

            Fill(canvas, fill, x, y + r, width, height - 2 * r);
            FillCornerBands(canvas, x, y, width, height, r, fill);

            Horizontal(canvas, border, x + r, y, width - 2 * r);
            Horizontal(canvas, border, x + r, y + height - 1, width - 2 * r);
            Fill(canvas, border, x, y + r, 1, height - 2 * r);
            Fill(canvas, border, x + width - 1, y + r, 1, height - 2 * r);

            // Adjacent border rows share the same corner inset. Carry it
            // forward instead of calculating sqrt twice for each row.
            int borderInset = CornerInset(r, 0);
            for (int row = 0; row < r; row++)
            {
                int next = row + 1 < r ? CornerInset(r, row + 1) : 0;
                int edge = Math.Max(1, borderInset - next + 1);
                Fill(canvas, border, x + borderInset, y + row, edge, 1);
                Fill(canvas, border, x + width - borderInset - edge, y + row, edge, 1);
                Fill(canvas, border, x + borderInset, y + height - row - 1, edge, 1);
                Fill(canvas, border, x + width - borderInset - edge, y + height - row - 1, edge, 1);
                borderInset = next;
            }
        }

        internal static void RoundedFill(Canvas canvas, int x, int y, int width, int height,
            int radius, Color fill)
        {
            if (canvas == null || width <= 0 || height <= 0)
                return;
            if (IsInvisible(canvas, x, y, width, height))
                return;
            int r = Math.Min(Math.Min(radius, width / 2), height / 2);
            if (r <= 0)
            {
                Fill(canvas, fill, x, y, width, height);
                return;
            }

            Fill(canvas, fill, x, y + r, width, height - 2 * r);
            // RoundedFill is common in layered shadows. On the host Canvas
            // the grouping loop costs more than its saved draw calls at small
            // radii; use cached insets with simple scanlines instead.
            for (int row = 0; row < r; row++)
            {
                int inset = CornerInset(r, row);
                Fill(canvas, fill, x + inset, y + row, width - 2 * inset, 1);
                Fill(canvas, fill, x + inset, y + height - row - 1, width - 2 * inset, 1);
            }
        }

        internal static void RoundedTop(Canvas canvas, int x, int y, int width, int height,
            int radius, Color fill)
        {
            if (canvas == null || width <= 0 || height <= 0)
                return;
            if (canvas == null || width <= 0 || height <= 0)
                return;
            if (IsInvisible(canvas, x, y, width, height))
                return;
            int r = Math.Min(Math.Min(radius, width / 2), height);
            Fill(canvas, fill, x, y + r, width, height - r);
            // Host A/B showed grouping overhead on this one-sided strip.
            // The cached inset avoids Math.Sqrt without extra band scans.
            for (int row = 0; row < r; row++)
            {
                int inset = CornerInset(r, row);
                Fill(canvas, fill, x + inset, y + row, width - 2 * inset, 1);
            }
        }

        // Adjacent scanlines with the same corner inset form one rectangle.
        // This preserves the exact opaque pixels while reducing Canvas calls.
        private static void FillCornerBands(Canvas canvas, int x, int y, int width,
            int height, int radius, Color fill)
        {
            for (int row = 0; row < radius;)
            {
                int inset = CornerInset(radius, row);
                int end = row + 1;
                while (end < radius && CornerInset(radius, end) == inset)
                    end++;

                int bandHeight = end - row;
                Fill(canvas, fill, x + inset, y + row, width - 2 * inset, bandHeight);
                Fill(canvas, fill, x + inset, y + height - end,
                    width - 2 * inset, bandHeight);
                row = end;
            }
        }

        internal static void Shadow(Canvas canvas, int x, int y, int width, int height, int radius)
        {
            // Opaque layered approximation, not alpha blending. Never draw on
            // maximized windows, and clip each span to the physical framebuffer.
            RoundedFill(canvas, x + 5, y + 6, width + 2, height + 2, radius + 3, UiPalette.ShadowOuter);
            RoundedFill(canvas, x + 4, y + 5, width + 1, height + 1, radius + 2, UiPalette.ShadowMiddle);
            RoundedFill(canvas, x + 2, y + 3, width, height, radius + 1, UiPalette.ShadowInner);
        }

        // Common UI radii are small. Cache their integer corner geometry once:
        // 1 + 2 + ... + 32 = 528 bytes, with no allocations in the paint loop.
        // Larger radii retain the original Math.Sqrt path.
        private const int CachedCornerRadius = 32;
        private static readonly byte[] CachedCornerInsets = BuildCornerInsets();

        private static byte[] BuildCornerInsets()
        {
            byte[] insets = new byte[CachedCornerRadius * (CachedCornerRadius + 1) / 2];
            int index = 0;
            for (int radius = 1; radius <= CachedCornerRadius; radius++)
            {
                for (int row = 0; row < radius; row++)
                    insets[index++] = (byte)CalculateCornerInset(radius, row);
            }
            return insets;
        }

        // Internal only to let the host smoke test exercise the production path.
        internal static int CornerInset(int radius, int row)
        {
            if (radius >= 1 && radius <= CachedCornerRadius && row >= 0 && row < radius)
                return CachedCornerInsets[radius * (radius - 1) / 2 + row];
            return CalculateCornerInset(radius, row);
        }

        private static int CalculateCornerInset(int radius, int row)
        {
            int delta = radius - row - 1;
            int horizontal = (int)Math.Sqrt(radius * radius - delta * delta);
            return radius - horizontal;
        }

        private static void Horizontal(Canvas canvas, Color color, int x, int y, int width)
        {
            Fill(canvas, color, x, y, width, 1);
        }

        // Defensive clipping prevents a shadow touching screen edges from writing
        // outside a framebuffer on firmware-only display implementations.
        private static void Fill(Canvas canvas, Color color, int x, int y, int width, int height)
        {
            if (width <= 0 || height <= 0 || x >= canvas.Width || y >= canvas.Height)
                return;
            // Most panel spans are entirely inside the framebuffer. Avoid
            // four Min/Max operations and wide clipping arithmetic for them.
            // Subtraction is safe after x/y >= 0 and x/y < canvas bounds.
            if (x >= 0 && y >= 0 &&
                width <= canvas.Width - x && height <= canvas.Height - y)
            {
                canvas.DrawFilledRectangle(color, x, y, width, height);
                return;
            }
            long right = (long)x + width;
            long bottom = (long)y + height;
            if (right <= 0 || bottom <= 0)
                return;
            int left = Math.Max(0, x);
            int top = Math.Max(0, y);
            int endX = (int)Math.Min(canvas.Width, right);
            int endY = (int)Math.Min(canvas.Height, bottom);
            if (endX > left && endY > top)
                canvas.DrawFilledRectangle(color, left, top, endX - left, endY - top);
        }
    }
}
