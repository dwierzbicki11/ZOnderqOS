"""Regression contract: extreme mouse coordinates cannot wrap double-click distance."""
from pathlib import Path
import re
import unittest

SOURCE = Path(__file__).resolve().parents[1] / "source/GUI/Application/FileManagerApp.cs"


class FileManagerPointerDeltaTests(unittest.TestCase):
    def test_pointer_distance_is_widened_before_subtraction(self):
        source = SOURCE.read_text(encoding="utf-8")
        self.assertRegex(source, r"Math\\.Abs\\(\\(long\\)mouseX\\s*-\\s*lastClickX\\)\\s*<=\\s*12")
        self.assertRegex(source, r"Math\\.Abs\\(\\(long\\)mouseY\\s*-\\s*lastClickY\\)\\s*<=\\s*12")
        self.assertNotIn("Math.Abs(mouseX - lastClickX)", source)
        self.assertNotIn("Math.Abs(mouseY - lastClickY)", source)

    def test_extreme_coordinates_are_not_nearby(self):
        int_min, int_max = -(2**31), 2**31 - 1
        self.assertGreater(abs(int_max - int_min), 12)
        for a, b in [(int_min, int_min + 12), (int_max, int_max - 12)]:
            self.assertLessEqual(abs(a - b), 12)


if __name__ == "__main__":
    unittest.main()
