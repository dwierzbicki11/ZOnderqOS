using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;

namespace ZonderqOS.GUI
{
    public static class SmallTextRenderer
    {
        // Decode 5-pixel rows to packed horizontal runs once at startup.
        // Rendering uses one lookup per row rather than a glyph-byte lookup
        // followed by a second, data-dependent span-table lookup.
        private static readonly int[] CachedSpanRuns = BuildSpanRuns();
        private static readonly int[] CachedAsciiPackedRows = BuildAsciiPackedRows();
        private static readonly int[] CachedFallbackPackedRows = BuildFallbackPackedRows();

#if ZONDERQ_PERF_HOST_TEST
        // Exact previous two-lookup path, compiled only into host A/B tests.
        private static readonly byte[] CachedAsciiGlyphRows = BuildAsciiGlyphRows();
        private static readonly byte[] CachedFallbackGlyphRows = BuildFallbackGlyphRows();
        internal static bool UsePreviousGlyphRowLookupForBenchmark;
#endif

        private static int[] BuildSpanRuns()
        {
            var table = new int[32];
            for (int bits = 0; bits < table.Length; bits++)
            {
                int packed = 0;
                int count = 0;
                for (int col = 0; col < 5;)
                {
                    if ((bits & (1 << (4 - col))) == 0)
                    {
                        col++;
                        continue;
                    }
                    int start = col++;
                    while (col < 5 && (bits & (1 << (4 - col))) != 0)
                        col++;
                    packed |= (start | ((col - start) << 3)) << (2 + count * 6);
                    count++;
                }
                table[bits] = packed | count;
            }
            return table;
        }


        private static int[] BuildAsciiPackedRows()
        {
            var rows = new int[128 * 7];
            for (int ch = 0; ch < 128; ch++)
            {
                int offset = ch * 7;
                for (int row = 0; row < 7; row++)
                    rows[offset + row] = CachedSpanRuns[Glyph((char)ch, row)];
            }
            return rows;
        }

        private static int[] BuildFallbackPackedRows()
        {
            var rows = new int[7];
            for (int row = 0; row < rows.Length; row++)
                rows[row] = CachedSpanRuns[Glyph('\uFFFF', row)];
            return rows;
        }

#if ZONDERQ_PERF_HOST_TEST
        private static byte[] BuildAsciiGlyphRows()
        {
            byte[] rows = new byte[128 * 7];
            for (int ch = 0; ch < 128; ch++)
            {
                int offset = ch * 7;
                for (int row = 0; row < 7; row++)
                    rows[offset + row] = (byte)Glyph((char)ch, row);
            }
            return rows;
        }

        private static byte[] BuildFallbackGlyphRows()
        {
            byte[] rows = new byte[7];
            for (int row = 0; row < rows.Length; row++)
                rows[row] = (byte)Glyph('\uFFFF', row);
            return rows;
        }

#endif

        public static int Width(string text)
        {
            return string.IsNullOrEmpty(text) ? 0 : text.Length * 6 - 1;
        }

        public static int WidthUInt(ulong value)
        {
            return DigitCount(value) * 6 - 1;
        }

        public static int WidthInt(long value)
        {
            if (value >= 0)
                return WidthUInt((ulong)value);

            ulong magnitude = value == long.MinValue
                ? ((ulong)long.MaxValue + 1UL)
                : (ulong)(-value);
            return 6 + WidthUInt(magnitude);
        }

        public static void Draw(Canvas canvas, string text, int x, int y, Color color)
        {
            if (string.IsNullOrEmpty(text))
                return;

            // Use the same wide-coordinate, per-glyph visibility window as
            // DrawRange. Partially scrolled labels previously decoded every
            // hidden glyph (and could overflow x + i * 6 for long strings).
            // No substring allocation or separate rasterization path.
            DrawRange(canvas, text, 0, text.Length, x, y, color);
        }

        public static void DrawUInt(Canvas canvas, ulong value, int x, int y, Color color)
        {
            int digits = DigitCount(value);
            // Numeric labels are drawn frequently by the task manager. Avoid
            // digit division and glyph scans when the whole label is clipped.
            // Wide bounds keep extreme window offsets from wrapping.
            if (canvas == null || (long)x >= canvas.Width || (long)y >= canvas.Height ||
                (long)x + (long)digits * 6 <= 0 || (long)y + 7 <= 0)
                return;

            // Avoid decoding invisible leading/trailing digits on partially
            // scrolled numeric labels. Keep the fully visible fast path free
            // of per-digit clipping checks.
            int first = 0;
            int last = digits;
            bool partiallyClipped = x < -4 || (long)x + (long)digits * 6 > canvas.Width;
            if (partiallyClipped)
            {
                if (x < -4)
                {
                    long skip = (-(long)x + 1) / 6;
                    if (skip >= digits)
                        return;
                    first = (int)skip;
                }
                long visibleEnd = ((long)canvas.Width - x + 5) / 6;
                if (visibleEnd < last)
                    last = (int)visibleEnd;
            }

            if (last <= first)
                return;

            // Jump over completely hidden leading digits without decoding
            // them. When first > 0, digits - first <= 19, so Pow10 fits
            // ulong even when rendering the full 20-digit ulong.MaxValue.
            if (first > 0)
                value %= Pow10(digits - first);
            ulong divisor = Pow10(digits - first - 1);
            // Stop at the last visible digit instead of dividing/modulo
            // through the entire invisible tail of a clipped numeric label.
            for (int i = first; i < last; i++)
            {
                ulong digit = value / divisor;
                DrawChar(canvas, (char)('0' + (int)digit), x + i * 6, y, color);
                if (divisor > 1)
                {
                    value %= divisor;
                    divisor /= 10;
                }
                else
                {
                    divisor = 0;
                }
            }
        }

        public static void DrawInt(Canvas canvas, long value, int x, int y, Color color)
        {
            if (value >= 0)
            {
                DrawUInt(canvas, (ulong)value, x, y, color);
                return;
            }

            ulong magnitude = value == long.MinValue
                ? ((ulong)long.MaxValue + 1UL)
                : (ulong)(-value);
            // Negative task-manager values share the numeric clipping path.
            // Check the entire label before drawing the minus sign and use
            // wide arithmetic for the magnitude offset (x + 6 can overflow).
            int characters = DigitCount(magnitude) + 1;
            if (canvas == null || (long)x >= canvas.Width || (long)y >= canvas.Height ||
                (long)x + (long)characters * 6 <= 0 || (long)y + 7 <= 0)
                return;

            if (x >= -4)
                DrawChar(canvas, '-', x, y, color);
            long magnitudeX = (long)x + 6;
            if (magnitudeX < canvas.Width && magnitudeX <= int.MaxValue)
                DrawUInt(canvas, magnitude, (int)magnitudeX, y, color);
        }

        public static void DrawPercent(Canvas canvas, int value, int x, int y, Color color)
        {
            if (value < 0) value = 0;
            if (value > 100) value = 100;
            int digits = DigitCount((ulong)value);
            // The task manager draws many percentages. If a row is entirely
            // outside the framebuffer, avoid entering the numeric renderer
            // and drawing the percent glyph into a clipped Canvas.
            // Wide arithmetic also prevents overflow in the suffix position.
            if (canvas == null || (long)x >= canvas.Width || (long)y >= canvas.Height ||
                (long)x + digits * 6L + 6 <= 0 || (long)y + 7 <= 0)
                return;

            DrawUInt(canvas, (ulong)value, x, y, color);
            long percentX = (long)x + digits * 6 + 1;
            if (percentX >= -4 && percentX < canvas.Width && percentX <= int.MaxValue)
                DrawChar(canvas, '%', (int)percentX, y, color);
        }

        public static void DrawUIntWithSuffix(Canvas canvas, ulong value, string suffix, int x, int y, Color color)
        {
            int digits = DigitCount(value);
            long suffixX = (long)x + digits * 6 + 5;
            long right = string.IsNullOrEmpty(suffix)
                ? (long)x + digits * 6
                : suffixX + (long)suffix.Length * 6;
            // Numeric status labels with units are often scrolled out with
            // their parent panel. Skip both renderers before glyph work.
            // Keep suffix coordinates wide until visibility is established.
            if (canvas == null || (long)x >= canvas.Width || (long)y >= canvas.Height ||
                right <= 0 || (long)y + 7 <= 0)
                return;

            DrawUInt(canvas, value, x, y, color);
            if (!string.IsNullOrEmpty(suffix) && suffixX >= int.MinValue &&
                suffixX <= int.MaxValue)
                Draw(canvas, suffix, (int)suffixX, y, color);
        }

        public static void DrawClipped(Canvas canvas, string text, int x, int y, int maxWidth, Color color)
        {
            if (string.IsNullOrEmpty(text) || maxWidth < 5)
                return;

            // Scrolled-out file names and menu labels need no clipping math,
            // glyph scans or ellipsis. Use wide edges for extreme offsets.
            if (canvas == null || x >= canvas.Width || y >= canvas.Height ||
                (long)x + maxWidth <= 0 || (long)y + 7 <= 0)
                return;

            // maxWidth can be int.MaxValue in a caller's unconstrained
            // viewport; avoid overflow in the character budget.
            int maxChars = Math.Max(1, (int)(((long)maxWidth + 1) / 6));
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

            // Saturate the range before adding: start + count can overflow.
            int end = count >= text.Length - start ? text.Length : start + count;
            int characters = end - start;
            if (canvas == null || (long)x >= canvas.Width || (long)y >= canvas.Height ||
                (long)x + (long)characters * 6 <= 0 || (long)y + 7 <= 0)
                return;

            // A horizontal scroll can leave only a handful of glyphs visible.
            // Each glyph paints columns [x + 6*i, x + 6*i + 4], so skip
            // complete offscreen glyphs before the expensive row/span loop.
            int first = 0;
            if (x < -4)
            {
                long skip = (-(long)x + 1) / 6;
                if (skip >= characters)
                    return;
                first = (int)skip;
            }
            int last = characters;
            long visibleEnd = ((long)canvas.Width - x + 5) / 6;
            if (visibleEnd < last)
                last = (int)visibleEnd;

            for (int i = first; i < last; i++)
                DrawChar(canvas, text[start + i], x + i * 6, y, color);
        }

        // Keep this tiny wrapper inlineable for the very common 0..99
        // counters. A large threshold tree in this method prevented the JIT
        // from inlining WidthUInt's hot path and regressed small labels.
        private static int DigitCount(ulong value)
        {
            if (value < 10UL)
                return 1;
            if (value < 100UL)
                return 2;
            return DigitCountLarge(value);
        }

        // Large 3..20 digit values avoid up to 19 sequential divisions.
        private static int DigitCountLarge(ulong value)
        {
            if (value < 100000000UL)
            {
                if (value < 10000UL)
                    return value < 1000UL ? 3 : 4;
                if (value < 1000000UL)
                    return value < 100000UL ? 5 : 6;
                return value < 10000000UL ? 7 : 8;
            }
            if (value < 10000000000000000UL)
            {
                if (value < 1000000000000UL)
                {
                    if (value < 10000000000UL)
                        return value < 1000000000UL ? 9 : 10;
                    return value < 100000000000UL ? 11 : 12;
                }
                if (value < 100000000000000UL)
                    return value < 10000000000000UL ? 13 : 14;
                return value < 1000000000000000UL ? 15 : 16;
            }
            if (value < 1000000000000000000UL)
                return value < 100000000000000000UL ? 17 : 18;
            return value < 10000000000000000000UL ? 19 : 20;
        }

        // Fixed 160-byte table avoids repeated 64-bit multiplication while
        // formatting visible numeric labels. Every caller uses 0..19 only.
        private static readonly ulong[] CachedPowersOfTen =
        {
            1UL, 10UL, 100UL, 1000UL, 10000UL,
            100000UL, 1000000UL, 10000000UL, 100000000UL,
            1000000000UL, 10000000000UL, 100000000000UL,
            1000000000000UL, 10000000000000UL, 100000000000000UL,
            1000000000000000UL, 10000000000000000UL,
            100000000000000000UL, 1000000000000000000UL,
            10000000000000000000UL
        };

        private static ulong Pow10(int exponent)
        {
            return CachedPowersOfTen[exponent];
        }

        private static void DrawChar(Canvas canvas, char ch, int x, int y, Color color)
        {
            ch = NormalizeGlyph(ch);
            // Spaces (including normalized NBSP) have no lit pixels. They are
            // common in labels and tables, so skip seven empty row scans.
            if (ch == ' ')
                return;
            int glyphOffset = ch < 128 ? ch * 7 : -1;
            if (glyphOffset < 0 && ch <= '\u017F')
            {
                // Only the Latin-range Unicode exceptions can uppercase to
                // ASCII. Skip Unicode casing for CJK, Cyrillic, symbols and
                // surrogate code units; the exhaustive UTF-16 test verifies
                // the boundary against the original Glyph implementation.
                char upper = char.ToUpperInvariant(ch);
                if (upper < 128)
                    glyphOffset = upper * 7;
            }
            // A partially visible title/label should not send completely
            // offscreen glyph rows to the software Canvas. The fully visible
            // path still uses exactly seven rows and allocates nothing.
            // Wide arithmetic keeps extreme signed Y coordinates safe.
            int firstRow = y < 0 ? (int)Math.Min(7L, -(long)y) : 0;
            int lastRow = (long)y + 7 > canvas.Height
                ? (int)Math.Max(0L, Math.Min(7L, (long)canvas.Height - y)) : 7;
            for (int row = firstRow; row < lastRow; row++)
            {
                int packed = glyphOffset >= 0
                    ? CachedAsciiPackedRows[glyphOffset + row]
                    : CachedFallbackPackedRows[row];
#if ZONDERQ_PERF_HOST_TEST
                if (UsePreviousGlyphRowLookupForBenchmark)
                {
                    int bits = glyphOffset >= 0
                        ? CachedAsciiGlyphRows[glyphOffset + row]
                        : CachedFallbackGlyphRows[row];
                    packed = CachedSpanRuns[bits];
                }
#endif
                // Zero means an empty row; other values encode the exact
                // previous one-to-three horizontal runs and Canvas calls.
                if (packed == 0)
                    continue;
                int runCount = packed & 3;
                packed >>= 2;
                for (int run = 0; run < runCount; run++)
                {
                    int runStart = packed & 7;
                    int runLength = (packed >> 3) & 7;
                    canvas.DrawFilledRectangle(color, x + runStart, y + row, runLength, 1);
                    packed >>= 6;
                }
            }
        }

        private static char NormalizeGlyph(char ch)
        {
            // All printable ASCII glyphs are unchanged by normalization.
            // Avoid the non-ASCII switch for the common file-name/UI path.
            if (ch < 128)
                return ch;
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
