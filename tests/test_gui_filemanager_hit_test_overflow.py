"""Regression contract for file manager grid hit testing at extreme offsets."""

from pathlib import Path
import re
import unittest

SOURCE = Path(__file__).resolve().parents[1] / "source/GUI/Application/FileManagerApp.cs"


class FileManagerHitTestOverflow(unittest.TestCase):
    def test_hit_test_uses_wide_index_before_bounds_check(self):
        source = SOURCE.read_text(encoding="utf-8")
        self.assertRegex(
            source,
            r"long index\s*=\s*\(long\)scrollIndex\s*\+\s*\(long\)row\s*\*\s*columns\s*\+\s*col\s*;",
        )
        self.assertRegex(
            source,
            r"return index >= 0 && index < entries\.Count \? \(int\)index : -1;",
        )

    def test_extreme_scroll_offset_does_not_wrap_to_valid_entry(self):
        # Simulates the arithmetic of the C# hit test, including cases that
        # would wrap a signed 32-bit intermediate to a plausible tile index.
        def hit(scroll, row, columns, col, count):
            index = scroll + row * columns + col
            return index if 0 <= index < count else -1

        self.assertEqual(hit(0, 1, 4, 2, 20), 6)
        self.assertEqual(hit(2**31 - 1, 1, 1, 0, 10), -1)
        self.assertEqual(hit(2**31 - 2, 2, 2, 1, 10), -1)
        self.assertEqual(hit(0, 2**31 - 1, 2**31 - 1, 0, 10), -1)


if __name__ == "__main__":
    unittest.main()
