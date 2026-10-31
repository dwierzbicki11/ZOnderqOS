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


class RoundedPanelPerformanceContracts(unittest.TestCase):
    def test_border_geometry_is_identical(self):
        for radius in range(1, 65):
            with self.subTest(radius=radius):
                self.assertEqual(original_border(radius), reused_border(radius))

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
    print(f"RoundedPanel radius={r}: CornerInset calls {before} -> {after} "
          "(pure source-level count; not measured runtime).")
    unittest.main(verbosity=2)
