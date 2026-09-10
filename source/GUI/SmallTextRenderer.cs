using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;

namespace ZonderqOS.GUI
{
    public static class SmallTextRenderer
    {
        public static int Width(string text)
        {
            return string.IsNullOrEmpty(text) ? 0 : text.Length * 6 - 1;
        }

        public static void Draw(Canvas canvas, string text, int x, int y, Color color)
        {
            if (string.IsNullOrEmpty(text))
                return;

            for (int i = 0; i < text.Length; i++)
                DrawChar(canvas, text[i], x + i * 6, y, color);
        }

        public static void DrawClipped(Canvas canvas, string text, int x, int y, int maxWidth, Color color)
        {
            if (string.IsNullOrEmpty(text) || maxWidth < 5)
                return;

            int maxChars = Math.Max(1, (maxWidth + 1) / 6);
            if (text.Length <= maxChars)
            {
                Draw(canvas, text, x, y, color);
                return;
            }

            if (maxChars < 4)
            {
                DrawRange(canvas, text, 0, maxChars, x, y, color);
                return;
            }

            int count = maxChars - 3;
            DrawRange(canvas, text, 0, count, x, y, color);
            Draw(canvas, "...", x + count * 6, y, color);
        }

        public static void DrawCentered(Canvas canvas, string text, int x, int y, int width, Color color)
        {
            if (string.IsNullOrEmpty(text))
                return;

            int textWidth = Width(text);
            if (textWidth <= width)
            {
                Draw(canvas, text, x + Math.Max(0, (width - textWidth) / 2), y, color);
                return;
            }

            DrawClipped(canvas, text, x, y, width, color);
        }

        public static void DrawRange(Canvas canvas, string text, int start, int count, int x, int y, Color color)
        {
            if (string.IsNullOrEmpty(text) || count <= 0 || start < 0 || start >= text.Length)
                return;

            int end = Math.Min(text.Length, start + count);
            for (int i = start; i < end; i++)
                DrawChar(canvas, text[i], x + (i - start) * 6, y, color);
        }

        private static void DrawChar(Canvas canvas, char ch, int x, int y, Color color)
        {
            ch = NormalizeGlyph(ch);
            for (int row = 0; row < 7; row++)
            {
                int bits = Glyph(ch, row);
                for (int col = 0; col < 5; col++)
                {
                    if ((bits & (1 << (4 - col))) != 0)
                        canvas.DrawFilledRectangle(color, x + col, y + row, 1, 1);
                }
            }
        }

        private static char NormalizeGlyph(char ch)
        {
            switch (ch)
            {
                case 'ą': case 'Ą': return 'A';
                case 'ć': case 'Ć': return 'C';
                case 'ę': case 'Ę': return 'E';
                case 'ł': case 'Ł': return 'L';
                case 'ń': case 'Ń': return 'N';
                case 'ó': case 'Ó': return 'O';
                case 'ś': case 'Ś': return 'S';
                case 'ź': case 'Ź': case 'ż': case 'Ż': return 'Z';
                case '–': case '—': return '-';
                case '‘': case '’': return '\'';
                case '“': case '”': return '"';
                case '\u00A0': return ' ';
                default: return ch;
            }
        }

        private static int Glyph(char ch, int row)
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
                case '/': pattern = "000010001000100010001000000000"; break;
                case '\\': pattern = "10000010000010000010000010000000000"; break;
                case ':': pattern = "000000010000000001000000000000"; break;
                case ';': pattern = "00000001000000000000001000010001000"; break;
                case ',': pattern = "00000000000000000000001000010001000"; break;
                case '|': pattern = "001000010000100001000010000100"; break;
                case '=': pattern = "000001111100000111110000000000"; break;
                case '+': pattern = "00000001000010011111001000010000000"; break;
                case '%': pattern = "11001110100010001000101100011000000"; break;
                case '!': pattern = "00100001000010000100001000000000100"; break;
                case '?': pattern = "01110100010001000100001000000000100"; break;
                case '#': pattern = "01010111110101001010111110101000000"; break;
                case '*': pattern = "00000101010111011111011101010100000"; break;
                case '@': pattern = "01110100011011110101101111000001110"; break;
                case '&': pattern = "01100100101010001000101011001001101"; break;
                case '$': pattern = "00100011111010001110001011111000100"; break;
                case '<': pattern = "00010001000100010000010000010000010"; break;
                case '>': pattern = "01000001000001000001000100010001000"; break;
                case '\'': pattern = "00100001000000000000000000000000000"; break;
                case '"': pattern = "01010010100000000000000000000000000"; break;
                case '(': pattern = "000100010000100001000010000010"; break;
                case ')': pattern = "010000010000100001000010001000"; break;
                case '[': pattern = "011100100001000010000100001110"; break;
                case ']': pattern = "011100001000010000100001001110"; break;
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
