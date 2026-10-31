#!/usr/bin/env python3
"""Pure math regression for rounded border reuse; not a framebuffer benchmark."""
from math import isqrt
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
SOURCE = (ROOT / "source/GUI/UiPainter.cs").read_text(encoding="utf-8")


def inset(radius, row):
    delta = radius - row - 1
    return radius - isqrt(radius * radius - delta * delta)


def original_border(radius):
    return [(inset(radius, row),
             max(1, inset(radius, row) -
                 (inset(radius, row + 1) if row + 1 < radius else 0) + 1))
            for row in range(radius)]


def reused_border(radius):
    current = inset(radius, 0)
    result = []
    for row in range(radius):
        following = inset(radius, row + 1) if row + 1 < radius else 0
        result.append((current, max(1, current - following + 1)))
        current = following
    return result



def grouped_corner_rows(radius):
    """Run-length encode equal corner insets; independent of the C# helper."""
    groups = []
    row = 0
    while row < radius:
        start = row
        value = inset(radius, row)
        row += 1
        while row < radius and inset(radius, row) == value:
            row += 1
        groups.append((start, row, value))
    return groups


def expanded_group_rows(radius):
    result = []
    for start, end, value in grouped_corner_rows(radius):
        result.extend([value] * (end - start))
    return result


class RoundedPanelPerformanceContracts(unittest.TestCase):
    def test_border_geometry_is_identical(self):
        for radius in range(1, 65):
            with self.subTest(radius=radius):
                self.assertEqual(original_border(radius), reused_border(radius))

    def test_corner_cache_geometry_and_triangular_offsets(self):
        self.assertIn("private const int CachedCornerRadius = 32;", SOURCE)
        self.assertIn("byte[] insets = new byte[CachedCornerRadius * (CachedCornerRadius + 1) / 2];", SOURCE)
        self.assertIn("CachedCornerInsets[radius * (radius - 1) / 2 + row]", SOURCE)
        self.assertIn("return CalculateCornerInset(radius, row);", SOURCE)
        cache = [inset(radius, row)
                 for radius in range(1, 33) for row in range(radius)]
        self.assertEqual(len(cache), 528)
        for radius in range(1, 33):
            offset = radius * (radius - 1) // 2
            self.assertEqual(cache[offset:offset + radius],
                             [inset(radius, row) for row in range(radius)])

    def test_corner_band_raster_and_draw_calls(self):
        self.assertEqual(SOURCE.count(
            "FillCornerBands(canvas, x, y, width, height, r, fill, true);"), 2)
        self.assertIn(
            "FillCornerBands(canvas, x, y, width, height, r, fill, false);", SOURCE)
        self.assertIn("y + height - end", SOURCE)
        for radius in range(1, 65):
            with self.subTest(radius=radius):
                self.assertEqual(expanded_group_rows(radius),
                                 [inset(radius, row) for row in range(radius)])
                self.assertLessEqual(len(grouped_corner_rows(radius)), radius)
        self.assertEqual(len(grouped_corner_rows(12)), 8)
        self.assertEqual((1 + 2 * 12, 1 + 2 * 8), (25, 17))
        self.assertEqual((1 + 12, 1 + 8), (13, 9))

    def test_radius_clamping_remains_unchanged(self):
        self.assertIn("int r = Math.Min(Math.Min(radius, width / 2), height / 2);", SOURCE)
        self.assertIn("int horizontal = (int)Math.Sqrt(radius * radius - delta * delta);", SOURCE)

    def test_carries_previous_inset_without_extra_allocation(self):
        self.assertIn("int borderInset = CornerInset(r, 0);", SOURCE)
        self.assertIn("int edge = Math.Max(1, borderInset - next + 1);", SOURCE)
        self.assertIn("borderInset = next;", SOURCE)
        self.assertNotIn("int inset = CornerInset(r, row);\n                int next", SOURCE)


if __name__ == "__main__":
    r = 12
    before, after = 3 * r - 1, 2 * r
    print(f"RoundedPanel radius={r}: CornerInset lookups {before} -> {after} "
          "(cache removes runtime sqrt for radius <=32; static count).")
    print(f"RoundedFill radius={r}: Canvas calls {1+2*r} -> "
          f"{1+2*len(grouped_corner_rows(r))} (static model, not FPS).")
    unittest.main(verbosity=2)
