"""Static regression checks for scrollbar dragging across window geometry changes."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
SOURCE = (ROOT / "source/GUI/ScrollBar.cs").read_text(encoding="utf-8")


class ScrollbarDragGeometryTests(unittest.TestCase):
    def test_drag_captures_track_geometry(self):
        start = SOURCE.index("dragging = true;")
        end = SOURCE.index("return true;", start)
        block = SOURCE[start:end]
        for assignment in (
            "dragTrackX = X;",
            "dragTrackY = Y;",
            "dragTrackWidth = Width;",
            "dragTrackHeight = Height;",
        ):
            with self.subTest(assignment=assignment):
                self.assertIn(assignment, block)

    def test_geometry_change_cancels_drag_before_thumb_motion(self):
        # Compare the four geometry fields independently (no assumption about formatting).
        start = SOURCE.index("if (dragging && (X != dragTrackX")
        end = SOURCE.index("dragging = false;", start)
        block = SOURCE[start:end]
        for comparison in (
            "X != dragTrackX",
            "Y != dragTrackY",
            "Width != dragTrackWidth",
            "Height != dragTrackHeight",
        ):
            self.assertIn(comparison, block)
        self.assertLess(end, SOURCE.index("if (dragging && isClicked)"))

    def test_range_change_still_cancels_drag(self):
        self.assertIn("if (rangeChanged)\n                dragging = false;", SOURCE)


if __name__ == "__main__":
    unittest.main()
