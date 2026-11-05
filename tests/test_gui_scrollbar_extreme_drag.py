"""Regression contracts for scrollbar pointer and thumb overflow safety."""
from pathlib import Path
import re
import unittest

SOURCE = Path(__file__).resolve().parents[1] / "source/GUI/ScrollBar.cs"


class ScrollBarExtremeGeometryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = SOURCE.read_text(encoding="utf-8")

    def test_drag_uses_wide_pointer_and_clamped_track_offset(self):
        self.assertIn("long pointerY = (long)mouseY - dragOffset;", self.source)
        self.assertIn("pointerY - Y", self.source)
        self.assertRegex(self.source, r"Math\.Max\(0L, Math\.Min\(\(long\)travel,")
        self.assertIn("Value = (int)((offset * MaxValue) / travel);", self.source)
        self.assertNotIn("mouseY - dragOffset));", self.source)

    def test_thumb_y_saturates_instead_of_wrapping(self):
        self.assertIn("long thumbY = (long)Y +", self.source)
        self.assertIn("Math.Max(int.MinValue, Math.Min(int.MaxValue, thumbY))", self.source)

    def test_render_centering_uses_wide_saturated_coordinates(self):
        self.assertIn("long centeredTrackX = (long)X +", self.source)
        self.assertIn("long centeredThumbX = (long)X +", self.source)
        self.assertIn("Math.Min(int.MaxValue, centeredTrackX)", self.source)
        self.assertIn("Math.Min(int.MaxValue, centeredThumbX)", self.source)
        self.assertNotIn("int trackX = X +", self.source)
        self.assertNotIn("int thumbX = X +", self.source)

    def test_render_centering_reference_at_int_boundaries(self):
        low, high = -(2**31), 2**31 - 1
        for x, width in ((high, 20), (low, 20), (high - 1, 200)):
            track_width = min(2, width)
            thumb_width = min(width, max(4, width - 2))
            for child_width in (track_width, thumb_width):
                raw = x + (width - child_width) // 2
                centered = max(low, min(high, raw))
                self.assertGreaterEqual(centered, low)
                self.assertLessEqual(centered, high)

    def test_extreme_drag_math_reference(self):
        # A held drag can receive pointer values at either signed-int extreme.
        i32_min, i32_max = -(2**31), 2**31 - 1
        for y, travel, pointer, offset, max_value in (
            (i32_max - 3, 100, i32_min, 20, 200),
            (i32_min + 3, 100, i32_max, 20, 200),
            (100, 500, i32_min, 50, 2000),
            (100, 500, i32_max, 50, 2000),
        ):
            track_offset = max(0, min(travel, pointer - offset - y))
            value = track_offset * max_value // travel
            self.assertGreaterEqual(value, 0)
            self.assertLessEqual(value, max_value)


if __name__ == "__main__":
    unittest.main()
