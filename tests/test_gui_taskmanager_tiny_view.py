#!/usr/bin/env python3
"""Short-view Task Manager source/layout regression checks (not a QEMU test)."""
from pathlib import Path
import unittest

SOURCE = (Path(__file__).resolve().parents[1] /
          "source/GUI/Application/TaskManagerModernApp.cs").read_text(encoding="utf-8")


def section(start, end):
    return SOURCE.split(start, 1)[1].split(end, 1)[0]


class TinyTaskManagerViewTests(unittest.TestCase):
    def test_zero_rows_are_representable(self):
        self.assertIn(
            "private int ListHeightForView => Math.Max(0, Height - ListTopForView - FooterHeight - 6);",
            SOURCE,
        )
        self.assertIn("public int VisibleRows => ListHeightForView / RowHeight;", SOURCE)
        self.assertNotIn("public int VisibleRows => Math.Max(1,", SOURCE)

    def test_rows_never_overlap_footer(self):
        # Include widths that trigger both compact and full table layouts.
        for width in (160, 220, 300, 480, 550, 900, 1020):
            for height in range(1, 801):
                compact = width < 550 or height < 146 + 28 + 36 + 6
                list_top = 64 if compact else 146
                available = max(0, height - list_top - 28 - 6)
                rows = available // 36
                self.assertGreaterEqual(rows, 0)
                if rows:
                    self.assertLessEqual(list_top + rows * 36, height - 28)
                if height <= list_top + 28 + 6:
                    self.assertEqual(rows, 0)
        self.assertEqual((170 - 64 - 28 - 6) // 36, 2)
        self.assertEqual((635 - 146 - 28 - 6) // 36, 12)

    def test_mouse_and_paint_ignore_unpainted_rows(self):
        hit = section("public int RowAt(int x, int y)", "public bool HandleScrollMouse(")
        mouse = section("public bool HandleScrollMouse(", "private void UpdateScrollBar()")
        table = section("private void RenderTable(Canvas canvas", "private void DrawRow(")
        self.assertIn("VisibleRows == 0", hit)
        self.assertIn("VisibleRows == 0", mouse)
        self.assertIn("if (width <= ScrollReserve + 4 || VisibleRows == 0)", table)
        self.assertLess(table.index("if (width <= ScrollReserve + 4 || VisibleRows == 0)"),
                        table.index("canvas.DrawFilledRectangle("))
        self.assertIn("if (rowOffset >= VisibleRows)", hit)

    def test_keyboard_and_end_task_reject_hidden_selection(self):
        selection = section("private void SelectAbsolute(int index)", "private void EndSelectedTask()")
        destructive = section("private void EndSelectedTask()", "private void RefreshSnapshot()")
        self.assertIn("view.VisibleRows == 0", selection)
        self.assertIn("view.VisibleRows == 0", destructive)
        self.assertLess(destructive.index("view.VisibleRows == 0"),
                        destructive.index("ModernTaskRow row = visibleRows[selectedIndex];"))
        self.assertIn("if (!row.CanEnd)", destructive)


if __name__ == "__main__":
    unittest.main()
