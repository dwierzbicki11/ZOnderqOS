"""Regression guard for overflow-safe scrollbar pointer hit testing."""
from pathlib import Path
import unittest

SOURCE = (Path(__file__).resolve().parents[1] / "source/GUI/ScrollBar.cs").read_text(encoding="utf-8")


class ScrollbarPointerBoundsTests(unittest.TestCase):
    def test_track_bounds_do_not_add_large_coordinates(self):
        self.assertIn("(long)mouseX - X < Width", SOURCE)
        self.assertIn("(long)mouseY - Y < Height", SOURCE)
        self.assertNotIn("mouseX < X + Width", SOURCE)
        self.assertNotIn("mouseY < Y + Height", SOURCE)

    def test_thumb_hit_testing_uses_wide_subtraction(self):
        self.assertIn("(long)mouseY - thumbY < thumbHeight", SOURCE)
        self.assertNotIn("mouseY < thumbY + thumbHeight", SOURCE)

    def test_negative_offsets_are_rejected(self):
        self.assertIn("mouseX >= X && (long)mouseX - X < Width", SOURCE)
        self.assertIn("mouseY >= Y && (long)mouseY - Y < Height", SOURCE)
        self.assertIn("mouseY >= thumbY && (long)mouseY - thumbY < thumbHeight", SOURCE)


if __name__ == "__main__":
    unittest.main()
