"""Regression contracts for keyboard selection arithmetic in File Manager."""
import pathlib
import unittest

SOURCE = (pathlib.Path(__file__).resolve().parents[1] /
          "source/GUI/Application/FileManagerApp.cs").read_text(encoding="utf-8")


class FileManagerSelectionOverflowTests(unittest.TestCase):
    def test_keyboard_selection_uses_widened_sum_before_clamp(self):
        method = SOURCE.split("private void Select(int delta)", 1)[1].split(
            "private void EnsureSelectionVisible()", 1)[0]
        self.assertIn("(long)selectedIndex + delta", method)
        self.assertIn("Math.Min((long)entries.Count - 1,", method)
        self.assertIn("Math.Max(0L,", method)
        self.assertIn("selectedIndex = (int)", method)

    def test_reference_clamp_extreme_deltas(self):
        int_max = 2**31 - 1
        int_min = -(2**31)
        def select(index, delta, count):
            return max(0, min(count - 1, index + delta))
        self.assertEqual(select(int_max - 2, int_max, int_max), int_max - 1)
        self.assertEqual(select(2, int_min, int_max), 0)
        self.assertEqual(select(4, 3, 10), 7)
        self.assertEqual(select(4, -9, 10), 0)


if __name__ == "__main__":
    unittest.main()
