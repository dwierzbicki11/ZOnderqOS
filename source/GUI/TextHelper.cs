using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;

namespace ZonderqOS.GUI
{
    public static class TextHelper
    {
        /// <summary>
        /// Zwraca szerokość tekstu w pikselach dla wybranej czcionki.
        /// </summary>
        public static int GetTextWidth(string text, Font font)
        {
            if (string.IsNullOrEmpty(text) || font == null) return 0;
            return text.Length * font.Width;
        }

        /// <summary>
        /// Zwraca wysokość linii dla wybranej czcionki.
        /// </summary>
        public static int GetTextHeight(Font font)
        {
            if (font == null) return 16;
            return font.Height;
        }

        /// <summary>
        /// Rysuje tekst idealnie wyśrodkowany wewnątrz zadanego prostokąta (np. przycisku lub nagłówka).
        /// </summary>
        public static void DrawCenteredString(Canvas canvas, string text, Font font, Color textColor, int x, int y, int width, int height)
        {
            if (string.IsNullOrEmpty(text)) return;

            int textWidth = GetTextWidth(text, font);
            int textHeight = GetTextHeight(font);

            int textX = x + (width - textWidth) / 2;
            int textY = y + (height - textHeight) / 2;

            canvas.DrawString(text, font, textColor, textX, textY);
        }
    }
}