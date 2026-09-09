using System.Drawing;
using Cosmos.Kernel.System.Graphics;

namespace ZonderqOS.GUI
{
    public static class Cursor
    {
        // The cursor bitmap is created exactly once and reused forever.
        // No Canvas/Bitmap allocations happen inside Draw(), so mouse movement
        // cannot cause the RAM growth that the old terminal renderer caused.
        private const int CursorWidth = 16;
        private const int CursorHeight = 20;
        private static Bitmap cursorBitmap;

        static Cursor()
        {
            try
            {
                Canvas offScreen = new Canvas(CursorWidth, CursorHeight);
                offScreen.Clear(Color.FromArgb(0, 0, 0, 0));

                // Compact high-contrast arrow. It is rasterized once into a Bitmap.
                offScreen.DrawLine(Color.Black, 0, 0, 0, 15);
                offScreen.DrawLine(Color.Black, 0, 0, 12, 12);
                offScreen.DrawLine(Color.Black, 12, 12, 8, 13);
                offScreen.DrawLine(Color.Black, 8, 13, 11, 19);
                offScreen.DrawLine(Color.Black, 11, 19, 8, 20);
                offScreen.DrawLine(Color.Black, 8, 20, 5, 14);
                offScreen.DrawLine(Color.Black, 5, 14, 0, 15);

                offScreen.DrawFilledRectangle(Color.White, 1, 2, 1, 12);
                offScreen.DrawFilledRectangle(Color.White, 2, 3, 1, 10);
                offScreen.DrawFilledRectangle(Color.White, 3, 4, 1, 9);
                offScreen.DrawFilledRectangle(Color.White, 4, 5, 1, 8);
                offScreen.DrawFilledRectangle(Color.White, 5, 6, 1, 7);
                offScreen.DrawFilledRectangle(Color.White, 6, 7, 1, 6);
                offScreen.DrawFilledRectangle(Color.White, 7, 8, 1, 5);
                offScreen.DrawFilledRectangle(Color.White, 8, 9, 1, 4);
                offScreen.DrawFilledRectangle(Color.White, 9, 10, 1, 4);

                cursorBitmap = offScreen.GetImage(0, 0, CursorWidth, CursorHeight);
            }
            catch
            {
                cursorBitmap = null;
            }
        }

        public static void Draw(Canvas canvas, int x, int y)
        {
            if (cursorBitmap != null)
            {
                // x/y is the hot spot at the top-left pixel. The same cached bitmap
                // is reused on every frame.
                canvas.DrawImage(cursorBitmap, x, y);
                return;
            }

            // Safe fallback without allocations.
            canvas.DrawFilledCircle(Color.White, x, y, 4);
            canvas.DrawCircle(Color.Black, x, y, 4);
        }
    }
}
