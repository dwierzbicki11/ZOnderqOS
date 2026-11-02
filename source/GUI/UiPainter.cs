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

            // Keep the original single-loop hot path for fully visible
            // panels. Large panels dragged across a screen edge need only
            // evaluate border rows that actually intersect the framebuffer.
            if (y >= 0 && (long)y + height <= canvas.Height)
            {
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
                return;
            }

            long topFirst = Math.Max(0L, -(long)y);
            long topEnd = Math.Min((long)r, (long)canvas.Height - y);
            if (topFirst < topEnd)
            {
                int inset = CornerInset(r, (int)topFirst);
                for (int row = (int)topFirst; row < topEnd; row++)
                {
                    int next = row + 1 < r ? CornerInset(r, row + 1) : 0;
                    int edge = Math.Max(1, inset - next + 1);
                    Fill(canvas, border, x + inset, y + row, edge, 1);
                    Fill(canvas, border, x + width - inset - edge, y + row, edge, 1);
                    inset = next;
                }
            }

            long bottomFirst = Math.Max(0L, (long)y + height - canvas.Height);
            long bottomEnd = Math.Min((long)r, (long)y + height);
            if (bottomFirst < bottomEnd)
            {
                int inset = CornerInset(r, (int)bottomFirst);
                for (int row = (int)bottomFirst; row < bottomEnd; row++)
                {
                    int next = row + 1 < r ? CornerInset(r, row + 1) : 0;
                    int edge = Math.Max(1, inset - next + 1);
                    Fill(canvas, border, x + inset, y + height - row - 1, edge, 1);
                    Fill(canvas, border, x + width - inset - edge, y + height - row - 1, edge, 1);
                    inset = next;
                }
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
            // Keep the common on-screen draw loop compact. The much larger
            // vertical clipping path is cold and lives in a separate method.
            if (y < 0 || (long)y + height > canvas.Height)
            {
                FillClippedCorners(canvas, x, y, width, height, r, fill);
                return;
            }

            for (int row = 0; row < r; row++)
            {
                int inset = CornerInset(r, row);
                Fill(canvas, fill, x + inset, y + row, width - 2 * inset, 1);
                Fill(canvas, fill, x + inset, y + height - row - 1, width - 2 * inset, 1);
            }
        }

        // Cold path: only mirrored corner scanlines intersecting the physical
        // framebuffer are evaluated. RoundedFill clamps r to height/2.
        private static void FillClippedCorners(Canvas canvas, int x, int y, int width,
            int height, int r, Color fill)
        {
            long topFirst = Math.Max(0L, -(long)y);
            long topEnd = Math.Min((long)r, (long)canvas.Height - y);
            for (int row = (int)topFirst; row < topEnd; row++)
            {
                int inset = CornerInset(r, row);
                Fill(canvas, fill, x + inset, y + row, width - 2 * inset, 1);
            }

            long bottomFirst = Math.Max(0L, (long)y + height - canvas.Height);
            long bottomEnd = Math.Min((long)r, (long)y + height);
            for (int row = (int)bottomFirst; row < bottomEnd; row++)
            {
                int inset = CornerInset(r, row);
                Fill(canvas, fill, x + inset, y + height - row - 1, width - 2 * inset, 1);
            }
        }

        internal static void RoundedTop(Canvas canvas, int x, int y, int width, int height,
            int radius, Color fill)
        {
            if (canvas == null || width <= 0 || height <= 0)
                return;
            if (IsInvisible(canvas, x, y, width, height))
                return;
            int r = Math.Min(Math.Min(radius, width / 2), height);
            Fill(canvas, fill, x, y + r, width, height - r);
            if (y < 0 || (long)y + r > canvas.Height)
            {
                FillClippedTopCorners(canvas, x, y, width, r, fill);
                return;
            }
            for (int row = 0; row < r; row++)
            {
                int inset = CornerInset(r, row);
                Fill(canvas, fill, x + inset, y + row, width - 2 * inset, 1);
            }
        }

        private static void FillClippedTopCorners(Canvas canvas, int x, int y, int width,
            int r, Color fill)
        {
            int firstRow = (int)Math.Max(0L, -(long)y);
            int lastRow = (int)Math.Min((long)r, (long)canvas.Height - y);
            for (int row = firstRow; row < lastRow; row++)
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
            if (y >= 0 && (long)y + height <= canvas.Height)
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
                return;
            }

            // Clip the two mirrored corner strips independently. Starting in
            // the middle of a same-inset band preserves every visible pixel,
            // while skipping the invisible band scans and their sqrt calls.
            long topFirst = Math.Max(0L, -(long)y);
            long topEnd = Math.Min((long)radius, (long)canvas.Height - y);
            for (int row = (int)topFirst; row < topEnd;)
            {
                int inset = CornerInset(radius, row);
                int end = row + 1;
                while (end < topEnd && CornerInset(radius, end) == inset)
                    end++;
                Fill(canvas, fill, x + inset, y + row, width - 2 * inset, end - row);
                row = end;
            }

            long bottomFirst = Math.Max(0L, (long)y + height - canvas.Height);
            long bottomEnd = Math.Min((long)radius, (long)y + height);
            for (int row = (int)bottomFirst; row < bottomEnd;)
            {
                int inset = CornerInset(radius, row);
                int end = row + 1;
                while (end < bottomEnd && CornerInset(radius, end) == inset)
                    end++;
                Fill(canvas, fill, x + inset, y + height - end,
                    width - 2 * inset, end - row);
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
        // Internal for exact host A/B tests of the production clipping path.
        internal static void Fill(Canvas canvas, Color color, int x, int y, int width, int height)
        {
            if (width <= 0 || height <= 0 || x >= canvas.Width || y >= canvas.Height)
                return;

            // Most GUI scanlines are wholly inside the framebuffer. Bypass
            // long-coordinate clipping and four Math.Min/Max operations while
            // retaining the wide, overflow-safe path for dragged windows.
            // Subtractions cannot overflow: x/y are nonnegative and already
            // smaller than the corresponding Canvas dimension.
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
