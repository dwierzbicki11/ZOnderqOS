"""Regression contracts for capturing scrollbar drag offsets at signed-int limits."""
from pathlib import Path
import unittest

SOURCE = Path(__file__).resolve().parents[1] / "source/GUI/ScrollBar.cs"


class ScrollBarDragOffsetTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = SOURCE.read_text(encoding="utf-8")

    def test_drag_offset_is_wide_and_capture_promotes_before_subtraction(self):
        self.assertIn("private long dragOffset;", self.source)
        self.assertIn("dragOffset = (long)mouseY - thumbY;", self.source)
        self.assertNotIn("dragOffset = mouseY - thumbY;", self.source)

    def test_drag_offset_is_used_in_wide_pointer_arithmetic(self):
        self.assertIn("long pointerY = (long)mouseY - dragOffset;", self.source)
        self.assertIn("pointerY - Y", self.source)

    def test_reference_extreme_capture_and_move(self):
        low, high = -(2**31), 2**31 - 1
        for initial, thumb_y, current in (
            (high, low, low),
            (low, high, high),
            (high, low, high),
            (low, high, low),
        ):
            drag_offset = initial - thumb_y
            self.assertGreaterEqual(drag_offset, -(2**32))
            self.assertLessEqual(drag_offset, 2**32)
            pointer_y = current - drag_offset
            track_y, travel = 10, 200
            offset = max(0, min(travel, pointer_y - track_y))
            self.assertGreaterEqual(offset, 0)
            self.assertLessEqual(offset, travel)


if __name__ == "__main__":
    unittest.main()
