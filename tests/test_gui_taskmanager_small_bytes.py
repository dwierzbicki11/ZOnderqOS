#!/usr/bin/env python3
"""Static regression guard for Task Manager's byte-size display."""
from pathlib import Path
import re
import unittest

SOURCE = (Path(__file__).resolve().parents[1] /
          "source/GUI/Application/TaskManagerModernApp.cs").read_text()


class TaskManagerSmallBytesTests(unittest.TestCase):
    def test_sub_kib_values_do_not_render_as_zero_kb(self):
        match = re.search(
            r'private static string FormatBytes\(ulong bytes\)(.*?)\n        }',
            SOURCE, re.S)
        self.assertIsNotNone(match)
        body = match.group(1)
        self.assertIn('if (bytes == 0) return "N/A";', body)
        self.assertIn('if (bytes >= 1024UL * 1024UL)', body)
        self.assertIn('if (bytes >= 1024UL)', body)
        self.assertIn('return bytes + " B";', body)
        self.assertLess(body.index('if (bytes >= 1024UL * 1024UL)'),
                        body.index('if (bytes >= 1024UL)'))
        self.assertLess(body.index('if (bytes >= 1024UL)'),
                        body.index('return bytes + " B";'))

    def test_reference_boundary_values(self):
        def format_bytes(value):
            if value == 0:
                return "N/A"
            if value >= 1024 * 1024:
                return f"{value // (1024 * 1024)} MB"
            if value >= 1024:
                return f"{value // 1024} KB"
            return f"{value} B"

        expected = {0: "N/A", 1: "1 B", 1023: "1023 B",
                    1024: "1 KB", 1048575: "1023 KB",
                    1048576: "1 MB", 2**64 - 1: "17592186044415 MB"}
        for value, output in expected.items():
            with self.subTest(value=value):
                self.assertEqual(format_bytes(value), output)


if __name__ == "__main__":
    unittest.main()
