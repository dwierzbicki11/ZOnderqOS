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
            Capture(x, y);
            previousX = x;
            previousY = y;
            valid = true;
            Cursor.Draw(canvas, x, y);
        }

        /// <summary>
        /// Called ONLY when the shell has proved no widget can change.
        /// Restore the old tiny rectangle, capture beneath the new position,
        /// draw the cursor, and present. Caller falls back to full paint on false.
        /// </summary>
        internal bool TryMove(int x, int y)
        {
            if (!valid || canvas == null)
                return false;

            if (x == previousX && y == previousY)
                return true;

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
            int baseX = x - LeftHalo;
            int baseY = y - TopHalo;
            int width = (int)canvas.Width;
            int height = (int)canvas.Height;

            for (int row = 0; row < DamageHeight; row++)
            {
                int py = baseY + row;
                int offset = row * DamageWidth;
                for (int col = 0; col < DamageWidth; col++)
                {
                    int px = baseX + col;
                    savedPixels[offset + col] = px >= 0 && py >= 0 && px < width && py < height
                        ? canvas.GetPointColor(px, py) : Color.Black;
                }
            }
        }

        private void Restore(int x, int y)
        {
            int baseX = x - LeftHalo;
            int baseY = y - TopHalo;
            int width = (int)canvas.Width;
            int height = (int)canvas.Height;

            for (int row = 0; row < DamageHeight; row++)
            {
                int py = baseY + row;
                if (py < 0 || py >= height)
                    continue;

                int offset = row * DamageWidth;
                for (int col = 0; col < DamageWidth; col++)
                {
                    int px = baseX + col;
                    if (px >= 0 && px < width)
                        canvas.DrawPoint(savedPixels[offset + col], px, py);
                }
            }
        }
    }
}
