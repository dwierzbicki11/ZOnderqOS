"""Regression for overflow-safe GUI file-list merge-sort boundaries."""
from pathlib import Path
import unittest

SOURCE = Path(__file__).resolve().parents[1] / "source/GUI/Application/FileManagerApp.cs"


def boundaries(count, start, run):
    middle = start + min(run, count - start)
    end = middle + min(run, count - middle)
    return middle, end


class MergeSortBoundsTests(unittest.TestCase):
    def test_production_uses_subtraction_before_addition(self):
        source = SOURCE.read_text(encoding="utf-8")
        self.assertIn("int middle = start + Math.Min(run, count - start);", source)
        self.assertIn("int end = middle + Math.Min(run, count - middle);", source)
        self.assertNotIn("Math.Min(count, start + run)", source)
        self.assertNotIn("Math.Min(count, middle + run)", source)

    def test_extreme_int_boundaries(self):
        maximum = 2**31 - 1
        for count, start, run in [
            (maximum, maximum - 1, maximum - 1),
            (maximum, maximum - 100, maximum // 2),
            (maximum, 0, maximum - 1),
            (maximum, maximum // 2, maximum // 2),
            (3, 2, 2), (1, 0, 1),
        ]:
            middle, end = boundaries(count, start, run)
            self.assertLessEqual(start, middle)
            self.assertLessEqual(middle, end)
            self.assertLessEqual(end, count)
            self.assertEqual(middle, min(count, start + run))
            self.assertEqual(end, min(count, middle + run))


if __name__ == "__main__":
    unittest.main()
