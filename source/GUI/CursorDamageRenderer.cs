using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;

namespace ZonderqOS.GUI
{
    /// <summary>
    /// Small software damage rectangles for cursor-only movement.
    /// No per-move allocations and no second full-screen framebuffer.
    ///
    /// Cosmos 3.0.95 Canvas.Display() still presents the full back buffer.
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
        // Double-buffered underlay: small cursor moves reuse the previous
        // captured pixels in the overlapping rectangle. Both buffers are
        // allocated once; no per-move heap allocation or framebuffer writes.
        private Color[] savedPixels = new Color[DamageWidth * DamageHeight];
        private Color[] nextPixels = new Color[DamageWidth * DamageHeight];
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
#if ZONDERQ_PERF_HOST_TEST
        // A/B only: the identical production capture path with per-pixel restore.
        internal static bool DisableUniformRestoreForBenchmark;
        internal static bool DisableOverlapReuseForBenchmark;
#endif

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
                    Capture(x, y, false);
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
                Capture(x, y, true);
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

        private void Capture(int x, int y, bool reusePrevious)
        {
            VisibleRegion(x, y, out long baseX, out long baseY,
                out int firstCol, out int lastCol, out int firstRow, out int lastRow);
            // Wide subtraction avoids overflow for extreme pointer positions.
            long dx = (long)x - previousX;
            long dy = (long)y - previousY;
            // Patterned underlays cost more to scan than the saved readbacks
            // save on the host Canvas. Reuse only a previously uniform
            // underlay; patterned scenes retain the original one-pass path.
            bool overlap = reusePrevious && uniformCapture &&
                dx > -DamageWidth && dx < DamageWidth &&
                dy > -DamageHeight && dy < DamageHeight;
#if ZONDERQ_PERF_HOST_TEST
            overlap = overlap && !DisableOverlapReuseForBenchmark;
#endif
            Color[] target = nextPixels;
            if (overlap)
            {
                int shiftX = (int)dx;
                int shiftY = (int)dy;
                int copyFirst = Math.Max(firstCol, -shiftX);
                int copyLast = Math.Min(lastCol, DamageWidth - shiftX);
                bool copied = false;
                bool previousUniform = uniformCapture;
                int previousArgb = previousUniform ? uniformColor.ToArgb() : 0;
                bool freshMatch = true;

                for (int row = firstRow; row < lastRow; row++)
                {
                    int py = (int)(baseY + row);
                    int offset = row * DamageWidth;
                    int oldRow = row + shiftY;
                    if ((uint)oldRow < DamageHeight && copyFirst < copyLast)
                    {
                        // Bulk-copy the unchanged overlap; only newly exposed
                        // left/right strips require actual Canvas readback.
                        for (int col = firstCol; col < copyFirst; col++)
                        {
                            Color pixel = canvas.GetPointColor((int)(baseX + col), py);
                            target[offset + col] = pixel;
                            if (previousUniform && pixel.ToArgb() != previousArgb)
                                freshMatch = false;
                        }
                        Array.Copy(savedPixels, oldRow * DamageWidth + copyFirst + shiftX,
                            target, offset + copyFirst, copyLast - copyFirst);
                        copied = true;
                        for (int col = copyLast; col < lastCol; col++)
                        {
                            Color pixel = canvas.GetPointColor((int)(baseX + col), py);
                            target[offset + col] = pixel;
                            if (previousUniform && pixel.ToArgb() != previousArgb)
                                freshMatch = false;
                        }
                    }
                    else
                    {
                        for (int col = firstCol; col < lastCol; col++)
                        {
                            Color pixel = canvas.GetPointColor((int)(baseX + col), py);
                            target[offset + col] = pixel;
                            if (previousUniform && pixel.ToArgb() != previousArgb)
                                freshMatch = false;
                        }
                    }
                }

                if (previousUniform && copied)
                {
                    // All reused pixels share the previous color. Only the
                    // newly exposed strips can break uniformity, so no second
                    // 672-pixel scan is needed on a flat desktop.
                    uniformCapture = freshMatch;
                }
                else
                {
                    UpdateUniformCapture(target, firstCol, lastCol, firstRow, lastRow);
                }
            }
            else
            {
                // Non-overlapping jumps retain the original one-pass capture
                // and uniformity check; do not pay for a second scan.
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
                        target[offset + col] = pixel;
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
                uniformCapture = !firstPixel && uniform;
            }
            Color[] old = savedPixels;
            savedPixels = target;
            nextPixels = old;
        }

        private void UpdateUniformCapture(Color[] pixels, int firstCol, int lastCol,
            int firstRow, int lastRow)
        {
            bool first = true;
            bool uniform = true;
            int argb = 0;
            for (int row = firstRow; row < lastRow; row++)
            {
                int offset = row * DamageWidth;
                for (int col = firstCol; col < lastCol; col++)
                {
                    Color pixel = pixels[offset + col];
                    if (first)
                    {
                        uniformColor = pixel;
                        argb = pixel.ToArgb();
                        first = false;
                    }
                    else if (uniform && pixel.ToArgb() != argb)
                        uniform = false;
                }
            }
            uniformCapture = !first && uniform;
        }

        private void Restore(int x, int y)
        {
            VisibleRegion(x, y, out long baseX, out long baseY,
                out int firstCol, out int lastCol, out int firstRow, out int lastRow);
            if (uniformCapture && firstCol < lastCol && firstRow < lastRow
#if ZONDERQ_PERF_HOST_TEST
                && !DisableUniformRestoreForBenchmark
#endif
                )
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
