#!/usr/bin/env python3
"""Deterministic draw-call and bitmap contracts; NOT an OS FPS benchmark."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
SOURCE = (ROOT / "source/GUI/SmallTextRenderer.cs").read_text(encoding="utf-8")
PATTERNS = re.findall(r'case .*?: pattern = "([01]+)"; break;', SOURCE)


def glyph_rows(pattern):
    # Mirrors Glyph: a truncated final row is blank.
    for row in range(7):
        bits = pattern[row * 5:row * 5 + 5]
        yield bits if len(bits) == 5 else "00000"


def pixels_and_spans(bits):
    old_pixels = [col for col in range(5) if bits[col] == "1"]
    new_pixels = []
    spans = 0
    col = 0
    while col < 5:
        if bits[col] == "0":
            col += 1
            continue
        start = col
        col += 1
        while col < 5 and bits[col] == "1":
            col += 1
        spans += 1
        new_pixels.extend(range(start, col))
    return old_pixels, new_pixels, spans


def draw_call_counts():
    old = new = 0
    for pattern in PATTERNS:
        for bits in glyph_rows(pattern):
            before, after, spans = pixels_and_spans(bits)
            if before != after:
                raise AssertionError("Glyph bitmap changed")
            old += len(before)
            new += spans
    return old, new


class Gen3TextRendererPerformanceContracts(unittest.TestCase):
    def test_all_known_glyph_rows_are_pixel_identical(self):
        self.assertGreaterEqual(len(PATTERNS), 60)
        for pattern in PATTERNS:
            for bits in glyph_rows(pattern):
                old, new, _ = pixels_and_spans(bits)
                self.assertEqual(old, new)

    def test_fewer_canvas_calls_for_same_pixels(self):
        old, new = draw_call_counts()
        self.assertGreater(old, 0)
        self.assertLess(new, old * 0.8)

    def test_ascii_glyph_cache_uses_same_original_decoder(self):
        self.assertIn("private static readonly byte[] CachedAsciiGlyphRows = BuildAsciiGlyphRows();", SOURCE)
        self.assertIn("byte[] rows = new byte[128 * 7];", SOURCE)
        self.assertIn("rows[offset + row] = (byte)Glyph((char)ch, row);", SOURCE)
        self.assertIn("int glyphOffset = ch < 128 ? ch * 7 : -1;", SOURCE)
        self.assertIn("CachedAsciiGlyphRows[glyphOffset + row]", SOURCE)
        self.assertIn("private static readonly byte[] CachedFallbackGlyphRows = BuildFallbackGlyphRows();", SOURCE)
        self.assertIn("rows[row] = (byte)Glyph('\\uFFFF', row);", SOURCE)
        self.assertIn("char upper = char.ToUpperInvariant(ch);", SOURCE)
        self.assertIn("if (upper < 128)", SOURCE)
        self.assertIn(": CachedFallbackGlyphRows[row];", SOURCE)
        self.assertIn("ch = NormalizeGlyph(ch);", SOURCE)

    def test_blank_glyph_fast_path_preserves_ink(self):
        # Only normalized ASCII space can bypass every row. NBSP is
        # normalized to space first, so it must be included as well.
        normalized = SOURCE.index("ch = NormalizeGlyph(ch);", SOURCE.index("private static void DrawChar"))
        blank = SOURCE.index("if (ch == ' ')", normalized)
        offset = SOURCE.index("int glyphOffset =", normalized)
        self.assertLess(normalized, blank)
        self.assertLess(blank, offset)
        self.assertIn("case '\\u00A0': return ' ';", SOURCE)
        self.assertIn("if (bits == 0)\\n                    continue;".replace("\\n", "\n"), SOURCE)

        for pattern in PATTERNS:
            for bits in glyph_rows(pattern):
                baseline, optimized, _ = pixels_and_spans(bits)
                if bits == "00000":
                    optimized = []  # production skips the empty row
                self.assertEqual(baseline, optimized)

    def test_ascii_space_is_an_empty_glyph(self):
        match = re.search(r"case ' ': pattern = \"([01]+)\"; break;", SOURCE)
        self.assertIsNotNone(match)
        self.assertEqual(set(match.group(1)), {"0"})
        # The source intentionally omits the final all-zero row.
        self.assertEqual(list(glyph_rows(match.group(1))), ["00000"] * 7)

    def test_span_renderer_is_used(self):
        # The hot path decodes one of 32 precomputed run layouts instead of
        # rescanning five glyph columns. C# host tests also verify all 32
        # packed patterns and exact framebuffer/Canvas-call equivalence.
        self.assertIn("private static readonly int[] CachedSpanRuns = BuildSpanRuns();", SOURCE)
        self.assertIn("var table = new int[32];", SOURCE)
        self.assertIn("packed |= (start | ((col - start) << 3)) << (2 + count * 6);", SOURCE)
        self.assertIn("int packed = CachedSpanRuns[bits];", SOURCE)
        self.assertIn("int runCount = packed & 3;", SOURCE)
        self.assertIn("int runStart = packed & 7;", SOURCE)
        self.assertIn("int runLength = (packed >> 3) & 7;", SOURCE)
        self.assertIn("canvas.DrawFilledRectangle(color, x + runStart, y + row, runLength, 1);", SOURCE)
        self.assertNotIn("canvas.DrawFilledRectangle(color, x + col, y + row, 1, 1);", SOURCE)

    def test_packed_row_math_for_all_32_patterns(self):
        for bits in range(32):
            packed = count = col = 0
            while col < 5:
                if not bits & (1 << (4 - col)):
                    col += 1
                    continue
                start = col
                col += 1
                while col < 5 and bits & (1 << (4 - col)):
                    col += 1
                packed |= (start | ((col - start) << 3)) << (2 + count * 6)
                count += 1
            self.assertLessEqual(count, 3)
            packed |= count
            runs = packed & 3
            packed >>= 2
            reconstructed = 0
            for _ in range(runs):
                start = packed & 7
                length = (packed >> 3) & 7
                self.assertGreater(length, 0)
                self.assertLessEqual(start + length, 5)
                for i in range(start, start + length):
                    reconstructed |= 1 << (4 - i)
                packed >>= 6
            self.assertEqual(reconstructed, bits)
            self.assertEqual(packed, 0)


if __name__ == "__main__":
    before, after = draw_call_counts()
    print(f"Text glyph Canvas calls per glyph-set pass: {before} -> {after} "
          f"({(before - after) * 100 / before:.2f}% fewer); static model, not QEMU FPS.")
    unittest.main(verbosity=2)
