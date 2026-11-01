#!/usr/bin/env python3
"""Ranged-text visibility arithmetic: deterministic model, not QEMU FPS."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
SOURCE = (ROOT / "source/GUI/SmallTextRenderer.cs").read_text(encoding="utf-8")


def visible_indices(x, canvas_width, characters):
    if x >= canvas_width or x + characters * 6 <= 0:
        return []
    first = 0
    if x < -4:
        skip = (-x + 1) // 6
        if skip >= characters:
            return []
        first = skip
    last = min(characters, (canvas_width - x + 5) // 6)
    return list(range(first, last))


def reference_indices(x, canvas_width, characters):
    return [i for i in range(characters)
            if x + i * 6 + 4 >= 0 and x + i * 6 < canvas_width]


class TextRangeVisibilityContracts(unittest.TestCase):
    def test_exact_visible_glyph_indices(self):
        for width in (1, 2, 5, 6, 7, 8, 31, 80, 128, 1920):
            for characters in (1, 2, 3, 4, 8, 20, 80, 160):
                for x in range(-200, 201):
                    self.assertEqual(visible_indices(x, width, characters),
                                     reference_indices(x, width, characters),
                                     (width, characters, x))

    def test_extreme_coordinates_and_saturating_count(self):
        for x in (-2**31, -1000000, -5, -4, -1, 0, 2**31 - 1):
            for width in (1, 128, 1920):
                self.assertEqual(visible_indices(x, width, 20),
                                 reference_indices(x, width, 20))
        text_length, start, count = 34, 2, 2**31 - 1
        end = text_length if count >= text_length - start else start + count
        self.assertEqual(end, text_length)

    def test_draw_delegates_to_clipped_range_without_allocating(self):
        draw = SOURCE.split("public static void Draw(Canvas", 1)[1].split(
            "public static void DrawUInt(", 1)[0]
        self.assertIn("DrawRange(canvas, text, 0, text.Length, x, y, color);", draw)
        self.assertNotIn("DrawChar(", draw)
        self.assertNotIn("Substring(", draw)

    def test_numeric_glyph_culling_preserves_full_visibility_fast_path(self):
        numeric = SOURCE.split("public static void DrawUInt(Canvas", 1)[1].split(
            "public static void DrawInt(", 1)[0]
        self.assertIn("bool partiallyClipped =", numeric)
        self.assertIn("if (skip >= digits)", numeric)
        self.assertIn("if (!partiallyClipped || (i >= first && i < last))", numeric)
        self.assertIn("DrawChar(canvas, (char)('0' + (int)digit)", numeric)

    def test_signed_numbers_cull_before_drawing_minus(self):
        signed = SOURCE.split("public static void DrawInt(Canvas", 1)[1].split(
            "public static void DrawPercent(", 1)[0]
        self.assertLess(signed.index("int characters = DigitCount(magnitude) + 1;"),
                        signed.index("DrawChar(canvas, '-', x, y, color);"))
        self.assertIn("long magnitudeX = (long)x + 6;", signed)
        self.assertIn("DrawUInt(canvas, magnitude, (int)magnitudeX, y, color);", signed)

    def test_production_source_uses_wide_culling_and_saturation(self):
        self.assertIn("int end = count >= text.Length - start ? text.Length : start + count;", SOURCE)
        self.assertIn("long skip = (-(long)x + 1) / 6;", SOURCE)
        self.assertIn("long visibleEnd = ((long)canvas.Width - x + 5) / 6;", SOURCE)
        self.assertIn("for (int i = first; i < last; i++)", SOURCE)
        self.assertIn("(long)x + (long)digits * 6 <= 0", SOURCE)
        self.assertIn("(long)x + (long)characters * 6 <= 0", SOURCE)


if __name__ == "__main__":
    unittest.main(verbosity=2)
