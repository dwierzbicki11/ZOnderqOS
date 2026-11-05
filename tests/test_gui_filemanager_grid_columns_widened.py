"""Regression checks for overflow-safe file-manager column geometry."""
from pathlib import Path
import unittest

SOURCE = Path(__file__).resolve().parents[1] / "source/GUI/Application/FileManagerApp.cs"


def columns(width: int, gap: int, tile_width: int) -> int:
    return max(1, (width + gap) // (tile_width + gap))


class FileManagerGridColumnsTests(unittest.TestCase):
    def test_production_uses_widened_intermediate(self):
        source = SOURCE.read_text(encoding="utf-8")
        start = source.index("private int Columns()")
        body = source[start:source.index("private void SelectBoundary", start)]
        self.assertIn("(long)GridWidthForView + TileGap", body)
        self.assertIn("Math.Max(1L", body)

    def test_reference_boundary_values(self):
        gap, tile_width = 8, 96
        for width in (0, 1, 95, 96, 103, 104, 800, 1366, 1920, 2**31 - 1):
            actual = columns(width, gap, tile_width)
            self.assertGreaterEqual(actual, 1)
            self.assertLessEqual(actual, 2**31 - 1)
        self.assertEqual(columns(2**31 - 1, gap, tile_width), (2**31 - 1 + gap) // 104)


if __name__ == "__main__":
    unittest.main()
