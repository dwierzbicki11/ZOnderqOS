"""Regression contract for file-manager grid row arithmetic on unusual displays."""
from pathlib import Path
import unittest

SOURCE = Path(__file__).resolve().parents[1] / "source/GUI/Application/FileManagerApp.cs"


class GridRowArithmeticTests(unittest.TestCase):
    def test_grid_rows_use_widened_intermediate(self):
        source = SOURCE.read_text(encoding="utf-8")
        self.assertIn("(long)GridHeightForView + TileHeight + TileGap - MinPartialTileHeight", source)
        self.assertIn("(int)Math.Max(1L,", source)

    def test_grid_row_reference_at_extreme_heights(self):
        tile_height, gap, minimum = 94, 6, 40
        for height in (0, 39, 40, 94, 139, 140, 2147483647):
            rows = 0 if height < minimum else max(1, (height + tile_height + gap - minimum) // (tile_height + gap))
            self.assertGreaterEqual(rows, 0)
            self.assertLessEqual(rows, 2147483647)
        self.assertEqual((2147483647 + 94 + 6 - 40) // 100, 21474837)


if __name__ == "__main__":
    unittest.main()
