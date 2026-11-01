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
        self.assertIn("!HasPaintedProcessRows", hit)
        self.assertIn("!HasPaintedProcessRows", mouse)
        self.assertIn("if (!HasPaintedProcessRows)", table)
        self.assertLess(table.index("if (!HasPaintedProcessRows)"),
                        table.index("canvas.DrawFilledRectangle("))
        self.assertIn("if (rowOffset >= VisibleRows)", hit)

    def test_no_row_or_column_means_no_process_interaction(self):
        self.assertIn("internal bool HasPaintedProcessRows => VisibleRows > 0 &&", SOURCE)
        self.assertIn("Width - SidebarWidth - 12 > ScrollReserve + 4;", SOURCE)
        # The view has a 170px sidebar, 12px content inset and 16px
        # scrollbar reserve; a 4px safety gap must remain.
        for width in range(1, 321):
            for height in (40, 100, 120, 146, 170, 220):
                top = 64 if width < 550 or height < 216 else 146
                rows = max(0, height - top - 28 - 6) // 36
                painted = rows > 0 and width - 170 - 12 > 16 + 4
                if width <= 202 or rows == 0:
                    self.assertFalse(painted)
                else:
                    self.assertTrue(painted)

    def test_compact_navigation_is_fully_inside_client(self):
        hit = section("public int NavAt(int x, int y)", "public int ToolbarActionAt(")
        draw = section("private void DrawNav(Canvas canvas", "private void RenderHeader(")
        sidebar = section("private void RenderSidebar(Canvas canvas)", "private void DrawNav(")
        self.assertIn("NavTop + row * 36 + 32 > Height", hit)
        self.assertIn("NavTop + (page / 2) * 36 + 32 > Height", draw)
        self.assertIn("int paintedWidth = Math.Min(SidebarWidth, Width - 1);", sidebar)
        self.assertIn("if (Width < SidebarWidth || Height < 36)", sidebar)
        for height in range(1, 300):
            for page in range(4):
                row = page // 2
                painted = 60 + row * 36 + 32 <= height
                if painted:
                    self.assertLessEqual(60 + row * 36 + 32, height)
                else:
                    self.assertGreater(60 + row * 36 + 32, height)

    def test_tiny_performance_selectors_cannot_cover_footer(self):
        hit = section("public int PerformanceResourceAt(", "public int CpuGraphModeAt(")
        draw = section("private void RenderPerformance(Canvas canvas)", "private void DrawCompactResource(")
        self.assertIn("Width <= SidebarWidth + 24 || Height < 134", hit)
        self.assertIn("Width <= SidebarWidth + 24 || Height < 134", draw)
        for height in range(1, 134):
            self.assertGreater(64 + 42 + 28, height)

    def test_keyboard_and_end_task_reject_hidden_selection(self):
        selection = section("private void SelectAbsolute(int index)", "private void EndSelectedTask()")
        destructive = section("private void EndSelectedTask()", "private void RefreshSnapshot()")
        self.assertIn("!view.HasPaintedProcessRows", selection)
        self.assertIn("!view.HasPaintedProcessRows", destructive)
        self.assertLess(destructive.index("!view.HasPaintedProcessRows"),
                        destructive.index("ModernTaskRow row = visibleRows[selectedIndex];"))
        self.assertIn("if (!row.CanEnd)", destructive)


if __name__ == "__main__":
    unittest.main()
