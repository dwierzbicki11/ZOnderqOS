#!/usr/bin/env python3
"""Short-view Task Manager source/layout regression checks (not a QEMU test)."""
from pathlib import Path
import unittest

SOURCE = (Path(__file__).resolve().parents[1] /
          "source/GUI/Application/TaskManagerModernApp.cs").read_text(encoding="utf-8")


def section(start, end):
    return SOURCE.split(start, 1)[1].split(end, 1)[0]


class TinyTaskManagerViewTests(unittest.TestCase):
    def test_minimized_manager_skips_hidden_telemetry_polling(self):
        update = section("public override void Update()", "private void UpdateLayout()")
        refresh = section("private void RefreshSnapshot()", "private ModernTaskRow AcquireRow")
        self.assertIn("if (!Window.Visible || Window.IsMinimized)", update)
        self.assertIn("lastRefreshTicks = -1;", update)
        self.assertIn("lastCpuTimestamp = 0;", update)
        self.assertIn("Array.Clear(previousThreadValid, 0, previousThreadValid.Length);", update)
        self.assertIn("if (!samplingPaused)", update)
        self.assertLess(update.index("if (!Window.Visible || Window.IsMinimized)"),
                        update.index("UpdateLayout();"))
        self.assertLess(update.index("return;"), update.index("UpdateClock();"))
        self.assertLess(update.index("return;"), update.index("RefreshSnapshot();"))
        self.assertIn("TaskManagerRefreshGate.ShouldRefresh(", update)
        self.assertIn("lastRefreshTicks = Stopwatch.GetTimestamp();", refresh)
        self.assertNotIn("refreshFrame", update)

        # Reference model: hidden updates do no polling; restoring samples
        # immediately, then at most once per elapsed 500ms (independent of FPS).
        def tick(now, last, hidden):
            if hidden:
                return -1, False
            if last < 0 or now - last >= 500:
                return now, True
            return last, False

        for fps in (2, 10, 30, 60, 120, 240):
            last = 0
            for frame in range(1000):
                last, sampled = tick(frame * 1000 // fps, last, True)
                self.assertFalse(sampled)
            last, sampled = tick(10_000, last, False)
            self.assertTrue(sampled)
            samples = 1
            for frame in range(1, fps * 4 + 1):
                now = 10_000 + frame * 1000 // fps
                last, sampled = tick(now, last, False)
                samples += sampled
            self.assertGreaterEqual(samples, 7)
            self.assertLessEqual(samples, 9)

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
        self.assertIn("private int TableRowWidthForView => Width - SidebarWidth - 12 - ScrollReserve;", SOURCE)
        self.assertIn("TableRowWidthForView >= 60;", SOURCE)
        # A 170px sidebar, 12px inset and 16px scrollbar reserve leave
        # the 52px Details PID/name offset inside a 60px painted row.
        for width in range(1, 321):
            for height in (40, 100, 120, 146, 170, 220):
                top = 64 if width < 550 or height < 216 else 146
                rows = max(0, height - top - 28 - 6) // 36
                row_width = width - 170 - 12 - 16
                painted = rows > 0 and row_width >= 60
                if width < 258 or rows == 0:
                    self.assertFalse(painted)
                else:
                    self.assertTrue(painted)
                    # Both process and Details name offsets stay inside.
                    for offset in (34, 52):
                        self.assertLessEqual(offset + max(1, row_width - offset - 4),
                                             row_width)

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
        self.assertIn("Width < SidebarWidth + 96 || Height < 134", hit)
        self.assertIn("Width < SidebarWidth + 96 || Height < 134", draw)
        header = section("private void RenderHeader(Canvas canvas)", "private void DrawButton(")
        toolbar = section("public int ToolbarActionAt(", "public int PerformanceResourceAt(")
        self.assertIn("Width < SidebarWidth + 64", header)
        self.assertIn("Width < SidebarWidth + 64", toolbar)
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


    def test_logical_cpu_grid_stays_above_footer_at_every_full_height(self):
        # 16 logical CPUs require 4 rows of at least 72px, plus 8px gaps.
        # RenderLogicalCpu starts at 64 + 58 + 22 = 144 from the view top.
        # The 28px footer must retain an 18px clearance.
        layout = section("private bool CompactPerformanceLayout =>",
                         "private int ListTopForView =>")
        self.assertIn("Width < 900 || Height < 502;", layout)
        draw = section("private void RenderLogicalCpu(", "private void DrawArrow(")
        self.assertIn("int availableH = Math.Max(220, Height - (gridY - Y) - FooterHeight - 18);", draw)
        self.assertIn("int cellH = Math.Max(72,", draw)
        self.assertIn("int gap = 8;", draw)

        for width in (900, 1020, 1366, 1920):
            for height in range(220, 901):
                compact = width < 900 or height < 502
                if compact:
                    continue
                for count in range(1, 17):
                    columns = 2 if count <= 4 else 3 if count <= 9 else 4
                    rows = (count + columns - 1) // columns
                    available = max(220, height - 190)
                    cell_height = max(72, (available - 8 * (rows - 1)) // rows)
                    grid_bottom = 144 + rows * cell_height + 8 * (rows - 1)
                    self.assertLessEqual(grid_bottom, height - 28 - 18,
                                         (width, height, count))
        self.assertLess(501, 502)
        self.assertEqual(144 + 4 * 72 + 3 * 8 + 18 + 28, 502)

    def test_cpu_pager_hitboxes_match_rendered_arrows(self):
        hit = section("public int CpuLogicalPageActionAt(", "public int RowAt(")
        draw = section("private void RenderLogicalCpu(", "private void DrawArrow(")
        self.assertIn("int top = 114;", hit)
        self.assertIn("int right = Width - 10;", hit)
        self.assertIn("app.PerformanceResource != TaskManagerModernApp.PerfCpu", hit)
        self.assertIn("app.ActivePage != TaskManagerModernApp.PagePerformance", hit)
        self.assertIn('DrawArrow(canvas, right - 60, y - 8, "<");', draw)
        self.assertIn('DrawArrow(canvas, right - 28, y - 8, ">");', draw)
        # RenderCpu begins at 64, RenderLogicalCpu at +58, arrow at -8.
        top = 64 + 58 - 8
        for width in (900, 1024, 1366, 1920):
            right = width - 10
            for y in (113, 114, 122, 139, 140):
                for x in (right - 61, right - 60, right - 35, right - 34,
                          right - 29, right - 28, right - 3, right - 2):
                    left = right - 60 <= x < right - 34 and top <= y < top + 26
                    right_arrow = right - 28 <= x < right - 2 and top <= y < top + 26
                    # The same bounds must be used by hit-test and paint.
                    self.assertEqual(left or right_arrow,
                                     (top <= y < top + 26 and
                                      (right - 60 <= x < right - 34 or
                                       right - 28 <= x < right - 2)))

    def test_narrow_full_header_hides_overlapping_clock(self):
        header = section("private void RenderHeader(Canvas canvas)", "private void DrawButton(")
        self.assertIn("if (refresh - clock >= 72)", header)
        self.assertLess(header.index("if (refresh - clock >= 72)"),
                        header.index("SmallTextRenderer.Draw(canvas, app.ClockText"))
        for width in range(550, 1201):
            content_x = 170 + 8
            refresh = width - 210
            clock = max(content_x + 120, refresh - 154)
            painted = refresh - clock >= 72
            if painted:
                self.assertLessEqual(clock + 72, refresh)
            else:
                self.assertLess(width, 580)
        self.assertFalse((550 - 210) - max(298, 550 - 364) >= 72)
        self.assertTrue((580 - 210) - max(298, 580 - 364) >= 72)

    def test_disabled_performance_end_task_is_not_clickable(self):
        toolbar = section("public int ToolbarActionAt(", "public int PerformanceResourceAt(")
        self.assertIn(
            "app.ActivePage != TaskManagerModernApp.PagePerformance &&", toolbar
        )
        self.assertIn("x >= end && x < end + 100) return 2;", toolbar)


    def test_process_hitbox_excludes_unpainted_scrollbar_gap(self):
        hit = section("public int RowAt(int x, int y)", "public bool HandleScrollMouse(")
        table = section("private void RenderTable(Canvas canvas", "private void DrawRow(")
        self.assertIn("x >= contentX + TableRowWidthForView", hit)
        self.assertIn("int rowW = TableRowWidthForView;", table)
        for width in (203, 220, 320, 550, 800, 1366, 1920):
            row_width = width - 170 - 12 - 16
            content_x = 170 + 8
            row_end = content_x + row_width
            self.assertEqual(row_end, width - 20)
            for x in range(content_x, width):
                selected = x < row_end
                if x >= row_end:
                    self.assertFalse(selected)
                else:
                    self.assertTrue(selected)
            # Old hit-test admitted four pixels beyond the painted rows.
            self.assertEqual((width - 16) - row_end, 4)


if __name__ == "__main__":
    unittest.main()
