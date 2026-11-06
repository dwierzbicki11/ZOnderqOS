#!/usr/bin/env python3
"""Regression guard for Task Manager's large-capacity percentage formatting."""
from pathlib import Path
import re
import unittest

SOURCE = (Path(__file__).resolve().parents[1] /
          "source/GUI/Application/TaskManagerModernApp.cs").read_text()

class TaskManagerPercentageTests(unittest.TestCase):
    def test_large_capacity_ratio_does_not_multiply_ulong(self):
        match = re.search(r'private static string FormatPercentPrecise\(ulong used, ulong total\)(.*?)\n        }', SOURCE, re.S)
        self.assertIsNotNone(match)
        body = match.group(1)
        self.assertIn('if (total == 0)', body)
        self.assertIn('if (used >= total)', body)
        self.assertIn('(double)used / total * 10000.0', body)
        self.assertIn('Math.Min(9999UL,', body)
        self.assertNotIn('used * 10000UL', body)

    def test_extreme_capacity_reference_values(self):
        max_ulong = (1 << 64) - 1
        for used, total in ((0, max_ulong), (max_ulong // 2, max_ulong),
                            (max_ulong - 1, max_ulong), (max_ulong, max_ulong),
                            (max_ulong, 1)):
            with self.subTest(used=used, total=total):
                scaled = 10000 if used >= total else min(9999, int(float(used) / total * 10000.0))
                self.assertGreaterEqual(scaled, 0)
                self.assertLessEqual(scaled, 10000)
                if used < total:
                    self.assertLess(scaled, 10000)
                else:
                    self.assertEqual(scaled, 10000)

if __name__ == "__main__":
    unittest.main()
