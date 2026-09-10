using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI
{
    public sealed class DesktopShortcut
    {
        private const int WidthValue = 92;
        private const int HeightValue = 86;
        private const int IconSize = 46;

        public int X { get; }
        public int Y { get; }
        public int Width => WidthValue;
        public int Height => HeightValue;
        public string Label { get; }
        public IconType Icon { get; }
        public Action OpenAction { get; }
        public bool IsSelected { get; set; }
        public bool IsHovered { get; set; }

        public DesktopShortcut(int x, int y, string label, IconType icon, Action openAction)
        {
            X = x;
            Y = y;
            Label = label ?? string.Empty;
            Icon = icon;
            OpenAction = openAction;
        }

        public bool Contains(int mouseX, int mouseY)
        {
            return mouseX >= X && mouseX < X + WidthValue && mouseY >= Y && mouseY < Y + HeightValue;
        }

        public void Open()
        {
            OpenAction?.Invoke();
        }

        public void Render(Canvas canvas)
        {
            Color panel;
            Color border;

            if (IsSelected)
            {
                panel = Color.FromArgb(34, 65, 91);
                border = Color.FromArgb(78, 156, 218);
            }
            else if (IsHovered)
            {
                panel = Color.FromArgb(28, 42, 54);
                border = Color.FromArgb(72, 108, 136);
            }
            else
            {
                // A permanent dark plate keeps PNG icons and captions readable on
                // both bright and dark parts of the wallpaper.
                panel = Color.FromArgb(19, 26, 33);
                border = Color.FromArgb(48, 59, 70);
            }

            // Small offset shadow separates the shortcut from detailed wallpapers.
            canvas.DrawFilledRectangle(Color.FromArgb(10, 14, 18), X + 4, Y + 4, WidthValue - 4, HeightValue - 4);
            canvas.DrawFilledRectangle(panel, X + 2, Y + 2, WidthValue - 4, HeightValue - 4);
            canvas.DrawRectangle(border, X + 2, Y + 2, WidthValue - 4, HeightValue - 4);

            int iconX = X + (WidthValue - IconSize) / 2;
            int iconY = Y + 7;

            // Dedicated icon well gives transparent PNGs a stable background.
            canvas.DrawFilledRectangle(Color.FromArgb(24, 31, 39), iconX - 5, iconY - 3, IconSize + 10, IconSize + 8);
            canvas.DrawRectangle(IsSelected ? Color.FromArgb(62, 128, 180) : Color.FromArgb(43, 54, 65),
                iconX - 5, iconY - 3, IconSize + 10, IconSize + 8);
            IconManager.DrawScaled(canvas, Icon, iconX, iconY, IconSize, IconSize);

            // Caption strip stays readable regardless of wallpaper brightness.
            canvas.DrawFilledRectangle(Color.FromArgb(13, 18, 24), X + 6, Y + 59, WidthValue - 12, 17);
            DrawCenteredLabel(canvas, Label, X + 8, Y + 64, WidthValue - 16);
        }

        private static void DrawCenteredLabel(Canvas canvas, string text, int x, int y, int width)
        {
            if (string.IsNullOrEmpty(text))
                return;

            const int glyphStep = 6;
            int maxChars = Math.Max(1, width / glyphStep);
            int count = Math.Min(text.Length, maxChars);
            bool clipped = count < text.Length;
            if (clipped && count > 3)
                count -= 3;

            int totalChars = count + (clipped ? 3 : 0);
            int textWidth = totalChars * glyphStep - 1;
            int startX = x + Math.Max(0, (width - textWidth) / 2);

            DrawTinyRange(canvas, text, 0, count, startX + 1, y + 1, Color.FromArgb(8, 11, 15));
            if (clipped)
                DrawTiny(canvas, "...", startX + count * glyphStep + 1, y + 1, Color.FromArgb(8, 11, 15));

            DrawTinyRange(canvas, text, 0, count, startX, y, Color.FromArgb(238, 243, 248));
            if (clipped)
                DrawTiny(canvas, "...", startX + count * glyphStep, y, Color.FromArgb(238, 243, 248));
        }

        private static void DrawTinyRange(Canvas canvas, string text, int start, int count, int x, int y, Color color)
        {
            int end = Math.Min(text.Length, start + count);
            for (int i = start; i < end; i++)
                DrawTinyChar(canvas, text[i], x + (i - start) * 6, y, color);
        }

        private static void DrawTiny(Canvas canvas, string text, int x, int y, Color color)
        {
            for (int i = 0; i < text.Length; i++)
                DrawTinyChar(canvas, text[i], x + i * 6, y, color);
        }

        private static void DrawTinyChar(Canvas canvas, char ch, int x, int y, Color color)
        {
            for (int row = 0; row < 7; row++)
            {
                int bits = TinyGlyph(ch, row);
                for (int col = 0; col < 5; col++)
                {
                    if ((bits & (1 << (4 - col))) != 0)
                        canvas.DrawFilledRectangle(color, x + col, y + row, 1, 1);
                }
            }
        }

        private static int TinyGlyph(char ch, int row)
        {
            string pattern;
            switch (char.ToUpperInvariant(ch))
            {
                case 'A': pattern = "011101000110001111111000110001"; break;
                case 'B': pattern = "111101000110001111101000111101"; break;
                case 'C': pattern = "011101000010000100001000001110"; break;
                case 'D': pattern = "111101000110001100011000111101"; break;
                case 'E': pattern = "111111000010000111101000011111"; break;
                case 'F': pattern = "111111000010000111101000010000"; break;
                case 'G': pattern = "011101000010000101111000101111"; break;
                case 'H': pattern = "100011000110001111111000110001"; break;
                case 'I': pattern = "111110010000100001000010011111"; break;
                case 'J': pattern = "001110001000100001001001001110"; break;
                case 'K': pattern = "100011001010100110001010010001"; break;
                case 'L': pattern = "100001000010000100001000011111"; break;
                case 'M': pattern = "100011101110101101011000110001"; break;
                case 'N': pattern = "100011100110101100111000110001"; break;
                case 'O': pattern = "011101000110001100011000101110"; break;
                case 'P': pattern = "111101000110001111101000010000"; break;
                case 'Q': pattern = "011101000110001100011010010101"; break;
                case 'R': pattern = "111101000110001111101010010001"; break;
                case 'S': pattern = "011111000010000011000000111110"; break;
                case 'T': pattern = "111110010000100001000010000100"; break;
                case 'U': pattern = "100011000110001100011000101110"; break;
                case 'V': pattern = "100011000110001100011010000100"; break;
                case 'W': pattern = "100011000110001101011010101010"; break;
                case 'X': pattern = "100011000101010001000101010001"; break;
                case 'Y': pattern = "100011000101010001000010000100"; break;
                case 'Z': pattern = "111110000100010001000100011111"; break;
                case '0': pattern = "011101000110011101011000101110"; break;
                case '1': pattern = "001000110000100001000010011111"; break;
                case '2': pattern = "011101000100001000100100011111"; break;
                case '3': pattern = "111100000100001001110000111110"; break;
                case '4': pattern = "000100011001010111110001000010"; break;
                case '5': pattern = "111111000011110000010000111110"; break;
                case '6': pattern = "011101000010000111101000101110"; break;
                case '7': pattern = "111110000100010001000010000100"; break;
                case '8': pattern = "011101000110001011101000110111"; break;
                case '9': pattern = "011101000110001011110000101110"; break;
                case '.': pattern = "000000000000000000000000000001"; break;
                case '-': pattern = "000000000000000011100000000000"; break;
                case '_': pattern = "000000000000000000000000011111"; break;
                case ' ': pattern = "000000000000000000000000000000"; break;
                default: pattern = "011101000100010001000000010000"; break;
            }

            int offset = row * 5;
            if (offset + 5 > pattern.Length)
                return 0;

            int bits = 0;
            for (int i = 0; i < 5; i++)
            {
                if (pattern[offset + i] == '1')
                    bits |= 1 << (4 - i);
            }
            return bits;
        }
    }
}
