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
        self.assertIn(": Glyph(ch, row);", SOURCE)
        self.assertIn("ch = NormalizeGlyph(ch);", SOURCE)

    def test_span_renderer_is_used(self):
        self.assertIn("int runStart = col++;", SOURCE)
        self.assertIn("while (col < 5 && (bits & (1 << (4 - col))) != 0)", SOURCE)
        self.assertIn("canvas.DrawFilledRectangle(color, x + runStart, y + row, col - runStart, 1);", SOURCE)
        self.assertNotIn("canvas.DrawFilledRectangle(color, x + col, y + row, 1, 1);", SOURCE)


if __name__ == "__main__":
    before, after = draw_call_counts()
    print(f"Text glyph Canvas calls per glyph-set pass: {before} -> {after} "
          f"({(before - after) * 100 / before:.2f}% fewer); static model, not QEMU FPS.")
    unittest.main(verbosity=2)
