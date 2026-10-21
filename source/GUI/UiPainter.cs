using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;

namespace ZonderqOS.GUI
{
    /// <summary>
    /// Clipped rounded rectangles for the Cosmos 3.0.94 software Canvas.
    /// Full-height content is drawn in one fast filled rectangle; only the
    /// two corner strips use a small number of scanlines. No per-pixel
    /// trigonometry, frame buffers, bitmaps or heap allocations.
    /// </summary>
    internal static class UiPainter
    {
        internal static void RoundedPanel(Canvas canvas, int x, int y, int width, int height,
            int radius, Color fill, Color border)
        {
            if (canvas == null || width <= 0 || height <= 0)
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
            for (int row = 0; row < r; row++)
            {
                int inset = CornerInset(r, row);
                Fill(canvas, fill, x + inset, y + row, width - 2 * inset, 1);
                Fill(canvas, fill, x + inset, y + height - row - 1, width - 2 * inset, 1);
            }

            Horizontal(canvas, border, x + r, y, width - 2 * r);
            Horizontal(canvas, border, x + r, y + height - 1, width - 2 * r);
            Fill(canvas, border, x, y + r, 1, height - 2 * r);
            Fill(canvas, border, x + width - 1, y + r, 1, height - 2 * r);

            for (int row = 0; row < r; row++)
            {
                int inset = CornerInset(r, row);
                int next = row + 1 < r ? CornerInset(r, row + 1) : 0;
                int edge = Math.Max(1, inset - next + 1);
                Fill(canvas, border, x + inset, y + row, edge, 1);
                Fill(canvas, border, x + width - inset - edge, y + row, edge, 1);
                Fill(canvas, border, x + inset, y + height - row - 1, edge, 1);
                Fill(canvas, border, x + width - inset - edge, y + height - row - 1, edge, 1);
            }
        }

        internal static void RoundedFill(Canvas canvas, int x, int y, int width, int height,
            int radius, Color fill)
        {
            if (canvas == null || width <= 0 || height <= 0)
                return;
            int r = Math.Min(Math.Min(radius, width / 2), height / 2);
            if (r <= 0)
            {
                Fill(canvas, fill, x, y, width, height);
                return;
            }

            Fill(canvas, fill, x, y + r, width, height - 2 * r);
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
            int r = Math.Min(Math.Min(radius, width / 2), height);
            Fill(canvas, fill, x, y + r, width, height - r);
            for (int row = 0; row < r; row++)
            {
                int inset = CornerInset(r, row);
                Fill(canvas, fill, x + inset, y + row, width - 2 * inset, 1);
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

        private static int CornerInset(int radius, int row)
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
