using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;

namespace ZonderqOS.GUI
{
    /// <summary>
    /// Small software damage rectangles for cursor-only movement.
    /// No per-move allocations and no second full-screen framebuffer.
    ///
    /// Cosmos 3.0.94 Canvas.Display() still presents the full back buffer.
    /// This only avoids rebuilding all desktop/widgets on pointer motion;
    /// it does NOT claim a partial GPU transfer or screen-page flip.
    /// </summary>
    internal sealed class CursorDamageRenderer
    {
        // Cursor is 16x20; add a 4px halo for its circular fallback.
        // Two 24x28 damage areas (old and new); 672 pixels in each.
        private const int LeftHalo = 4;
        private const int TopHalo = 4;
        private const int DamageWidth = 24;
        private const int DamageHeight = 28;
        private readonly Color[] savedPixels = new Color[DamageWidth * DamageHeight];
        private readonly Canvas canvas;
        private int previousX;
        private int previousY;
        private bool valid;
        private bool readbackSupported = true;
        // A flat desktop underlay is common. One filled rectangle restores
        // its 24x28 damage region instead of up to 672 DrawPoint calls.
        // Patterned underlays retain exact per-pixel restoration.
        private bool uniformCapture;
        private Color uniformColor;

        internal CursorDamageRenderer(Canvas canvas)
        {
            this.canvas = canvas;
        }

        internal void Reset()
        {
            // A scene redraw overwrites the old cursor and invalidates
            // the underlay; restoring it afterward would erase new UI.
            valid = false;
        }

        /// <summary>Called after a fully composed scene, before Display().</summary>
        internal void DrawAfterScene(int x, int y)
        {
            // Some future 3D/accelerated Canvas implementations may lack a
            // readable CPU back buffer. In that case keep ordinary rendering.
            if (readbackSupported)
            {
                try
                {
                    Capture(x, y);
                    previousX = x;
                    previousY = y;
                    valid = true;
                }
                catch
                {
                    readbackSupported = false;
                    valid = false;
                }
            }
            Cursor.Draw(canvas, x, y);
        }

        /// <summary>
        /// Called ONLY when the shell has proved no widget can change.
        /// Restore the old tiny rectangle, capture beneath the new position,
        /// draw the cursor, and present. Caller falls back to full paint on false.
        /// </summary>
        internal bool TryMove(int x, int y)
        {
            if (!valid || !readbackSupported || canvas == null)
                return false;

            if (x == previousX && y == previousY)
                return true;

            try
            {
                Restore(previousX, previousY);
                Capture(x, y);
                previousX = x;
                previousY = y;
                Cursor.Draw(canvas, x, y);
                canvas.Display();
                return true;
            }
            catch
            {
                // An unsupported readback must never take down the desktop.
                // The caller repaints the complete scene immediately.
                valid = false;
                readbackSupported = false;
                return false;
            }
        }

        // Clip once per damage rectangle instead of checking four bounds for
        // each of its 672 pixels. Offscreen savedPixels entries are never read
        // by Restore, so they need not be overwritten with Color.Black.
        // Wide arithmetic also avoids overflow at extreme pointer coordinates.
        private void VisibleRegion(int x, int y, out long baseX, out long baseY,
            out int firstCol, out int lastCol, out int firstRow, out int lastRow)
        {
            baseX = (long)x - LeftHalo;
            baseY = (long)y - TopHalo;
            firstCol = (int)Math.Min(DamageWidth, Math.Max(0L, -baseX));
            lastCol = (int)Math.Max(0L, Math.Min(DamageWidth, (long)canvas.Width - baseX));
            firstRow = (int)Math.Min(DamageHeight, Math.Max(0L, -baseY));
            lastRow = (int)Math.Max(0L, Math.Min(DamageHeight, (long)canvas.Height - baseY));
        }

        private void Capture(int x, int y)
        {
            VisibleRegion(x, y, out long baseX, out long baseY,
                out int firstCol, out int lastCol, out int firstRow, out int lastRow);
            bool firstPixel = true;
            bool uniform = true;
            int firstArgb = 0;
            for (int row = firstRow; row < lastRow; row++)
            {
                int py = (int)(baseY + row);
                int offset = row * DamageWidth;
                for (int col = firstCol; col < lastCol; col++)
                {
                    Color pixel = canvas.GetPointColor((int)(baseX + col), py);
                    savedPixels[offset + col] = pixel;
                    if (firstPixel)
                    {
                        uniformColor = pixel;
                        firstArgb = pixel.ToArgb();
                        firstPixel = false;
                    }
                    else if (uniform && pixel.ToArgb() != firstArgb)
                        uniform = false;
                }
            }
            // Empty offscreen captures must not reuse an older flat color.
            uniformCapture = !firstPixel && uniform;
        }

        private void Restore(int x, int y)
        {
            VisibleRegion(x, y, out long baseX, out long baseY,
                out int firstCol, out int lastCol, out int firstRow, out int lastRow);
            if (uniformCapture && firstCol < lastCol && firstRow < lastRow)
            {
                canvas.DrawFilledRectangle(uniformColor,
                    (int)(baseX + firstCol), (int)(baseY + firstRow),
                    lastCol - firstCol, lastRow - firstRow);
                return;
            }
            for (int row = firstRow; row < lastRow; row++)
            {
                int py = (int)(baseY + row);
                int offset = row * DamageWidth;
                for (int col = firstCol; col < lastCol; col++)
                    canvas.DrawPoint(savedPixels[offset + col], (int)(baseX + col), py);
            }
        }
    }
}
