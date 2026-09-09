using System.Drawing;
using Cosmos.Kernel.System.Graphics;

namespace ZonderqOS.GUI
{
    public static class Cursor
    {
        private static Bitmap cursorBitmap;

        static Cursor()
        {
            try
            {
                // 1. Tworzymy małe, niezależne płótno robocze o wymiarach 16x16 pikseli
                Canvas offScreen = new Canvas(16, 16);
                offScreen.Clear(Color.FromArgb(0, 0, 0, 0)); // Przezroczyste tło

                // 2. Rysujemy wektorowo klasyczną strzałkę systemową (czarna obwódka + białe wnętrze)
                // Krawędź zewnętrzna (czarna)
                offScreen.DrawLine(Color.Black, 0, 0, 0, 15);
                offScreen.DrawLine(Color.Black, 0, 0, 11, 11);
                offScreen.DrawLine(Color.Black, 11, 11, 7, 12);
                offScreen.DrawLine(Color.Black, 7, 12, 10, 16);
                offScreen.DrawLine(Color.Black, 10, 16, 7, 18);
                offScreen.DrawLine(Color.Black, 7, 18, 4, 13);
                offScreen.DrawLine(Color.Black, 4, 13, 0, 15);

                // Wypełnienie wnętrza (białe)
                offScreen.DrawFilledRectangle(Color.White, 1, 1, 1, 13);
                offScreen.DrawFilledRectangle(Color.White, 2, 2, 1, 11);
                offScreen.DrawFilledRectangle(Color.White, 3, 3, 1, 9);
                offScreen.DrawFilledRectangle(Color.White, 4, 4, 1, 8);
                offScreen.DrawFilledRectangle(Color.White, 5, 5, 1, 7);
                offScreen.DrawFilledRectangle(Color.White, 6, 6, 1, 5);

                // 3. Konwertujemy wyrenderowany kształt na obiekt Bitmap za pomocą metody GetImage
                cursorBitmap = offScreen.GetImage(0, 0, 16, 16);
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
                // Rysowanie wygenerowanej bitmapy kursora
                canvas.DrawImage(cursorBitmap, x, y);
            }
            else
            {
                // Fallback (zabezpieczenie): zwykła kropka, gdyby inicjalizacja się nie powiodła
                canvas.DrawFilledCircle(Color.White, x, y, 4);
                canvas.DrawCircle(Color.Black, x, y, 4);
            }
        }
    }
}