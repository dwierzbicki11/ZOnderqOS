"""Regression contracts for extreme file-grid page and scroll arithmetic."""
from pathlib import Path
import unittest

SOURCE = (Path(__file__).resolve().parents[1] / "source/GUI/Application/FileManagerApp.cs").read_text(encoding="utf-8")


class FileGridOverflowTests(unittest.TestCase):
    def test_page_jump_widens_before_multiplication(self):
        page = SOURCE.split("private void SelectPage(int direction)", 1)[1].split("private void Select(int delta)", 1)[0]
        self.assertIn("(long)direction * Columns() * rows", page)
        self.assertIn("jump > int.MaxValue", page)
        self.assertIn("jump < int.MinValue", page)

    def test_scroll_math_widens_before_multiplication(self):
        body = SOURCE.split("private void EnsureSelectionVisible()", 1)[1].split("private void GoUp()", 1)[0]
        self.assertIn("long visible = (long)cols * rows;", body)
        self.assertIn("long maxScroll = Math.Max(0L, ((long)lastRow - rows + 1) * cols);", body)

    def test_extreme_reference_math(self):
        limit = 2**31 - 1
        clamp = lambda value: max(-limit - 1, min(limit, value))
        self.assertEqual(clamp(limit * limit), limit)
        self.assertEqual(clamp(-limit * limit), -limit - 1)
        self.assertEqual(clamp(3 * 4), 12)
        # Last row 1_073_741_823, two columns, one visible row.
        self.assertEqual((1_073_741_823 - 1 + 1) * 2, 2_147_483_646)


if __name__ == "__main__":
    unittest.main()
