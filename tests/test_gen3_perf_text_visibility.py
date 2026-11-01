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
        self.assertIn("if (first > 0)", numeric)
        self.assertIn("value %= Pow10(digits - first);", numeric)
        self.assertIn("ulong divisor = Pow10(digits - first - 1);", numeric)
        self.assertIn("for (int i = first; i < last; i++)", numeric)
        self.assertIn("if (last <= first)", numeric)
        self.assertNotIn("for (int i = 0; i < digits; i++)", numeric)
        self.assertIn("DrawChar(canvas, (char)('0' + (int)digit)", numeric)


    def test_numeric_digit_skip_matches_decimal_baseline(self):
        values = [0, 1, 9, 10, 42, 999, 1000,
                  12345678901234567890, 2**64 - 1]
        widths = (1, 5, 6, 24, 128, 256)
        xs = list(range(-125, 131)) + [-2**31, 2**31 - 1]
        for value in values:
            s = str(value)
            digits = len(s)
            for width in widths:
                for x in xs:
                    expected = [(i, int(ch)) for i, ch in enumerate(s)
                                if x + i * 6 + 4 >= 0 and x + i * 6 < width]
                    if x >= width or x + digits * 6 <= 0:
                        actual = []
                    else:
                        first = max(0, (-x + 1) // 6) if x < -4 else 0
                        last = min(digits, (width - x + 5) // 6)
                        actual = []
                        if last > first:
                            remainder = value % 10**(digits - first) if first else value
                            divisor = 10**(digits - first - 1)
                            for i in range(first, last):
                                actual.append((i, remainder // divisor))
                                if divisor > 1:
                                    remainder %= divisor
                                    divisor //= 10
                                else:
                                    divisor = 0
                    self.assertEqual(actual, expected, (value, width, x))


    def test_decimal_thresholds_preserve_ulong_digit_count(self):
        import random
        source = SOURCE.split("private static int DigitCount(ulong value)", 1)[1].split(
            "private static ulong Pow10(", 1)[0]
        self.assertNotIn("value /= 10", source)
        self.assertIn("value < 100UL", source)
        self.assertIn("value < 10000000000000000000UL", source)

        def count(value):
            if value < 100:
                return 1 if value < 10 else 2
            if value < 100000000:
                if value < 10000:
                    return 3 if value < 1000 else 4
                if value < 1000000:
                    return 5 if value < 100000 else 6
                return 7 if value < 10000000 else 8
            if value < 10000000000000000:
                if value < 1000000000000:
                    if value < 10000000000:
                        return 9 if value < 1000000000 else 10
                    return 11 if value < 100000000000 else 12
                if value < 100000000000000:
                    return 13 if value < 10000000000000 else 14
                return 15 if value < 1000000000000000 else 16
            if value < 1000000000000000000:
                return 17 if value < 100000000000000000 else 18
            return 19 if value < 10000000000000000000 else 20

        values = [0, 2**64 - 1]
        for exponent in range(1, 20):
            power = 10**exponent
            values.extend((power - 1, power, power + 1))
        rng = random.Random(20261101)
        values.extend(rng.getrandbits(64) for _ in range(10000))
        for value in values:
            self.assertEqual(count(value), len(str(value)), value)


    def test_cached_decimal_powers_are_exact_and_bounded(self):
        import re
        power_block = SOURCE.split("private static readonly ulong[] CachedPowersOfTen", 1)[1].split(
            "private static ulong Pow10(", 1)[0]
        values = [int(n) for n in re.findall(r"(\d+)UL", power_block)]
        self.assertEqual(values, [10**i for i in range(20)])
        pow10 = SOURCE.split("private static ulong Pow10(int exponent)", 1)[1].split(
            "private static void DrawChar(", 1)[0]
        self.assertIn("return CachedPowersOfTen[exponent];", pow10)

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
