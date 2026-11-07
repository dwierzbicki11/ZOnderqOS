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
        // A uniform capture can be represented entirely by uniformColor.
        // Its overlap pixels need materializing only if a later capture
        // becomes patterned or moves into the generic overlap path.
        private bool savedPixelsComplete = true;
#if ZONDERQ_PERF_HOST_TEST
        // A/B only: the identical production capture path with per-pixel restore.
        internal static bool DisableUniformRestoreForBenchmark;
        internal static bool DisableOverlapReuseForBenchmark;
        internal static bool DisableVerticalBulkCopyForBenchmark;
        internal static bool DisableDeferredUniformCopyForBenchmark;
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
            // Pure vertical motion on a fully visible, flat underlay has a
            // contiguous overlapping span. Copy it once instead of issuing
            // up to 27 separate Array.Copy calls (one per scanline).
            // Do not use this path if the previous rectangle was clipped:
            // offscreen savedPixels slots are deliberately left uninitialized.
            bool bulkVertical = overlap && dx == 0 && dy != 0 &&
                firstCol == 0 && lastCol == DamageWidth &&
                firstRow == 0 && lastRow == DamageHeight &&
                (long)previousX - LeftHalo >= 0 &&
                (long)previousY - TopHalo >= 0 &&
                (long)previousX - LeftHalo + DamageWidth <= canvas.Width &&
                (long)previousY - TopHalo + DamageHeight <= canvas.Height;
#if ZONDERQ_PERF_HOST_TEST
            bulkVertical = bulkVertical && !DisableVerticalBulkCopyForBenchmark;
#endif
            if (bulkVertical)
            {
                int shiftY = (int)dy;
                int reusedRows = DamageHeight - Math.Abs(shiftY);
                int newStartRow = shiftY < 0 ? -shiftY : 0;
                int oldStartRow = shiftY > 0 ? shiftY : 0;
                // When the old underlay is flat, the reused pixels all have
                // uniformColor. Avoid copying 24*(28-|dy|) Color structs on
                // every small vertical movement. Materialize them only when
                // the newly exposed rows break uniformity. This also avoids
                // copying potentially stale savedPixels from a prior flat move.
                bool deferCopy = true;
#if ZONDERQ_PERF_HOST_TEST
                deferCopy = !DisableDeferredUniformCopyForBenchmark;
#endif
                int overlapStart = newStartRow * DamageWidth;
                int overlapEnd = overlapStart + reusedRows * DamageWidth;
                if (!deferCopy)
                {
                    if (savedPixelsComplete)
                        Array.Copy(savedPixels, oldStartRow * DamageWidth,
                            target, overlapStart, reusedRows * DamageWidth);
                    else
                        for (int i = overlapStart; i < overlapEnd; i++)
                            target[i] = uniformColor;
                }

                // Previously captured overlap is uniform. Only freshly
                // exposed rows can invalidate the single-color fast restore.
                int freshStart = shiftY > 0 ? reusedRows : 0;
                int freshEnd = shiftY > 0 ? DamageHeight : -shiftY;
                int argb = uniformColor.ToArgb();
                bool freshMatch = true;
                for (int row = freshStart; row < freshEnd; row++)
                {
                    int py = (int)(baseY + row);
                    int offset = row * DamageWidth;
                    for (int col = 0; col < DamageWidth; col++)
                    {
                        Color pixel = canvas.GetPointColor((int)(baseX + col), py);
                        target[offset + col] = pixel;
                        if (freshMatch && pixel.ToArgb() != argb)
                            freshMatch = false;
                    }
                }
                if (deferCopy && !freshMatch)
                {
                    // The next restore will use individual pixels. Populate
                    // only the reused rows; freshly captured rows stay intact.
                    for (int i = overlapStart; i < overlapEnd; i++)
                        target[i] = uniformColor;
                }
                uniformCapture = freshMatch;
                savedPixelsComplete = !deferCopy || !freshMatch;
            }
            else if (overlap)
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
                        if (savedPixelsComplete)
                            Array.Copy(savedPixels, oldRow * DamageWidth + copyFirst + shiftX,
                                target, offset + copyFirst, copyLast - copyFirst);
                        else
                        {
                            // A preceding uniform vertical move deliberately
                            // skipped storing its overlap. The color is known
                            // exactly, so rebuild these pixels without readback.
                            for (int col = copyFirst; col < copyLast; col++)
                                target[offset + col] = uniformColor;
                        }
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
                savedPixelsComplete = true;
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
                savedPixelsComplete = true;
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
