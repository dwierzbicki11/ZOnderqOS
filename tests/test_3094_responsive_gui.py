#!/usr/bin/env python3
"""Source + pure layout-math contracts; not a replacement for QEMU graphical QA."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]

def code(name):
    return (ROOT / name).read_text(encoding="utf-8")

WINDOW = code("source/GUI/Window.cs")
DESKTOP = code("source/GUI/GuiManager.cs")
CALC = code("source/GUI/Application/CalculatorApp.cs")
SETTINGS = code("source/GUI/Application/SettingsApp.cs")
ABOUT = code("source/GUI/Application/AboutApp.cs")
CALENDAR = code("source/GUI/Application/CalendarApp.cs")
TYPOGRAPHY = code("source/GUI/UiTypography.cs")
FILE_MANAGER = code("source/GUI/Application/FileManagerApp.cs")
NOTEPAD = code("source/GUI/Application/NotepadApp.cs")
APP_CENTER = code("source/GUI/Application/AppCenterApp.cs")
DISK_MANAGER = code("source/GUI/Application/DiskManagerApp.cs")
TASK_MANAGER = code("source/GUI/Application/TaskManagerModernApp.cs")
PROJECT = code("ZonderqOS.csproj")


def fit_window(x, y, width, height, screen_width, screen_height):
    desktop_height = screen_height - 44
    margin = 12 if screen_width >= 1000 and desktop_height >= 700 else 0
    available_width, available_height = screen_width - 2 * margin, desktop_height - 2 * margin
    width = max(min(320, available_width), min(width, available_width))
    height = max(min(220, available_height), min(height, available_height))
    return (max(margin, min(x, screen_width - width - margin)),
            max(margin, min(y, desktop_height - height - margin)), width, height)


def visible_rows(window_height, list_top, bottom_offset, row_height, row_gap):
    action_top = window_height - bottom_offset
    return max(1, min(6, (action_top - list_top - 8 + row_gap) // (row_height + row_gap)))


class ResponsiveGui3094(unittest.TestCase):
    def test_sdk_version_still_pinned(self):
        self.assertIn('Sdk="Cosmos.Sdk/3.0.95"', PROJECT)
        self.assertIn('Version="3.0.95"', PROJECT)
        self.assertIn('"3.0.95", Good', SETTINGS)
        self.assertNotIn('"3.0.94", Good', SETTINGS)

    def test_window_fit_does_not_fake_actual_display_resolution(self):
        self.assertIn("desktopWidth - Width - margin", WINDOW)
        self.assertIn("desktopHeight - Height - margin", WINDOW)
        self.assertIn("System.Math.Min(Width, availableWidth)", WINDOW)
        self.assertIn("System.Math.Min(Height, availableHeight)", WINDOW)
        self.assertIn("int margin = desktopWidth >= 1000 && desktopHeight >= 700 ? 12 : 0;", WINDOW)
        self.assertIn("DisplayModeSelector.OpenNativeOrBest()", DESKTOP)
        self.assertIn("MouseManager.SetScreenSize(canvas.Width, canvas.Height)", DESKTOP)
        for width, height in ((800, 600), (1366, 768), (1920, 1080)):
            x, y, w, h = fit_window(150, 105, 1080, 720, width, height)
            self.assertGreaterEqual(x, 0)
            self.assertGreaterEqual(y, 0)
            self.assertLessEqual(x + w, width)
            self.assertLessEqual(y + h, height - 44)

    def test_window_restore_and_resize_stay_within_desktop(self):
        self.assertIn("private void ConstrainToDesktop()", WINDOW)
        self.assertGreaterEqual(WINDOW.count("ConstrainToDesktop();"), 3)
        self.assertIn("int maxWidth = System.Math.Max(1, desktopWidth - resizeStartX);", WINDOW)
        self.assertIn("int maxHeight = System.Math.Max(1, desktopHeight - resizeStartY);", WINDOW)
        self.assertIn("int minWidth = System.Math.Min(MinWidth, maxWidth);", WINDOW)
        self.assertIn("int minHeight = System.Math.Min(MinHeight, maxHeight);", WINDOW)

        # Restoring a window saved at FHD must not leave it off-screen at 800x600.
        for screen_width, screen_height in ((800, 600), (1366, 768), (1920, 1080)):
            x, y, width, height = fit_window(1550, 700, 1080, 720, screen_width, screen_height)
            self.assertGreaterEqual(x, 0)
            self.assertGreaterEqual(y, 0)
            self.assertLessEqual(x + width, screen_width)
            self.assertLessEqual(y + height, screen_height - 44)

            for start_x in (0, 50, screen_width - 320):
                max_width = max(1, screen_width - start_x)
                min_width = min(320, max_width)
                for delta in (-1000, 0, 1000):
                    resized_width = max(min_width, min(500 + delta, max_width))
                    self.assertLessEqual(start_x + resized_width, screen_width)

    def test_start_menu_stays_inside_800x600(self):
        self.assertIn("System.Math.Min(560", DESKTOP)
        self.assertIn("availableDesktopHeight - 16", DESKTOP)
        self.assertIn("availableDesktopHeight - menuHeight - 8", DESKTOP)
        height = 600
        menu_height = min(560, max(1, height - 44 - 16))
        menu_y = max(0, height - 44 - menu_height - 8)
        self.assertEqual((menu_y, menu_height), (8, 540))
        # With six tools, the last row ends at 360+50+42 = 452.
        # The footer starts at 540-76=464: no overlapping controls.
        self.assertLess(452, menu_height - 76)

    def test_app_center_list_actions_and_details_fit(self):
        self.assertIn("private int ListWidth => ShowDetails ? 640", APP_CENTER)
        self.assertIn("private int ActionY => Window.Y + Window.Height - ActionBottomOffset", APP_CENTER)
        self.assertIn("int width = ListWidth;", APP_CENTER)
        self.assertIn("if (ShowDetails)", APP_CENTER)
        self.assertIn("Hit(mouseX, mouseY, listX, ry, ListWidth, RowHeight)", APP_CENTER)
        self.assertIn("Window.X + 28 + buttonWidth, ActionY, buttonWidth", APP_CENTER)
        self.assertNotIn("int actionY = Window.Y + 593", APP_CENTER)
        for screen_w, screen_h in ((800, 600), (1366, 768), (1920, 1080)):
            x, y, w, h = fit_window(150, 90, 1040, 700, screen_w, screen_h)
            rows = visible_rows(h, 207, 107, 57, 6)
            action_top = h - 107
            last_row_bottom = 207 + (rows - 1) * 63 + 57
            self.assertLessEqual(last_row_bottom + 8, action_top)
            show_details = w >= 920 and h >= 690
            list_width = 640 if show_details else w - 36
            self.assertLessEqual(list_width + 36, w)
            if show_details:
                detail_width = w - list_width - 49
                self.assertGreaterEqual(detail_width, 200)
                self.assertLessEqual(207 + 372, action_top)
            self.assertLessEqual(action_top + 42, h - 42)
            self.assertLessEqual(18 + (w - 46) // 2 * 2 + 10, w - 18)

    def test_disk_manager_list_actions_and_details_fit(self):
        self.assertIn("private int ListWidth => ShowDetails ? 650", DISK_MANAGER)
        self.assertIn("private int ActionY => Window.Y + Window.Height - ActionBottomOffset", DISK_MANAGER)
        self.assertIn("int width = ListWidth;", DISK_MANAGER)
        self.assertIn("if (ShowDetails)", DISK_MANAGER)
        self.assertIn("Hit(mouseX, mouseY, listX, y, ListWidth, RowHeight)", DISK_MANAGER)
        self.assertNotIn("int actionY = Window.Y + 558", DISK_MANAGER)
        for screen_w, screen_h in ((800, 600), (1366, 768), (1920, 1080)):
            _, _, w, h = fit_window(150, 90, 1040, 700, screen_w, screen_h)
            rows = visible_rows(h, 162, 142, 58, 7)
            action_top = h - 142
            last_row_bottom = 162 + (rows - 1) * 65 + 58
            self.assertLessEqual(last_row_bottom + 8, action_top)
            show_details = w >= 920 and h >= 690
            list_width = 650 if show_details else w - 36
            self.assertLessEqual(list_width + 36, w)
            if show_details:
                detail_width = w - list_width - 50
                self.assertGreaterEqual(detail_width, 200)
                self.assertLessEqual(162 + 382, action_top)
            self.assertLessEqual(action_top + 48, h - 43)

    def test_embedded_views_never_exceed_parent_window(self):
        self.assertIn("view.Width = Math.Max(1, Window.Width - 20);", FILE_MANAGER)
        self.assertIn("view.Height = Math.Max(1, Window.Height - 50);", FILE_MANAGER)
        self.assertIn("view.Width = Math.Max(1, Window.Width - 20);", TASK_MANAGER)
        self.assertIn("view.Height = Math.Max(1, Window.Height - 50);", TASK_MANAGER)
        self.assertIn("int contentWidth = System.Math.Max(1, Window.Width - sidebarWidth);", SETTINGS)

    def test_calculator_seventh_row_visible_on_800x600(self):
        self.assertIn("UiPainter.RoundedPanel(", CALC)
        self.assertIn("availableHeight / Rows", CALC)
        self.assertIn("System.Math.Max(28", CALC)
        fitted_height = fit_window(185, 96, 500, 690, 800, 600)[3]
        key_height = max(28, min(58, (fitted_height - 236 - 16 - 7 * (7 - 1)) // 7))
        self.assertLessEqual(236 + 6 * (key_height + 7) + key_height, fitted_height - 12)

    def test_settings_show_all_six_rows_above_status(self):
        self.assertIn("EffectiveRowHeight", SETTINGS)
        self.assertIn("Window.Height >= 650 ? RowHeight : 50", SETTINGS)
        height = fit_window(150, 100, 1080, 720, 800, 600)[3]
        effective_height = 68 if height >= 650 else 50
        sixth_end = 142 + 5 * (effective_height + 6) + effective_height
        self.assertLess(sixth_end, height - 42)

    def test_calendar_grid_and_hitboxes_fit_800x600(self):
        self.assertIn("MonthGridWidth", CALENDAR)
        self.assertIn("MonthCellHeight", CALENDAR)
        self.assertIn("if (ShowSidePanel)", CALENDAR)
        self.assertGreaterEqual(CALENDAR.count("int cellHeight = MonthCellHeight;"), 2)
        self.assertGreaterEqual(CALENDAR.count("int gridWidth = MonthGridWidth;") +
                                CALENDAR.count("int width = MonthGridWidth;"), 2)
        for desktop_width, desktop_height in ((800, 556), (1366, 724)):
            has_sidebar = desktop_width >= 860 and desktop_height >= 630
            grid_width = 614 if has_sidebar else max(220, desktop_width - 36)
            cell_height = max(28, min(58, (desktop_height - 250 - 6 * 5) // 6))
            self.assertLessEqual(grid_width + 36 if not has_sidebar else 646, desktop_width)
            self.assertLessEqual(200 + 6 * cell_height + 5 * 6, desktop_height - 42)

    def test_typography_and_editor_shared_styles(self):
        self.assertIn("PCScreenFont.DefaultFont", TYPOGRAPHY)
        self.assertIn("SmallTextRenderer.DrawClipped(", TYPOGRAPHY)
        self.assertNotIn("Substring(", TYPOGRAPHY)
        self.assertIn("UiTypography.Heading(", WINDOW)
        self.assertIn("UiTypography.Heading(", SETTINGS)
        self.assertIn("UiPainter.RoundedPanel(", ABOUT)
        self.assertIn("UiPainter.RoundedPanel(", FILE_MANAGER)
        self.assertIn("UiPainter.RoundedPanel(", NOTEPAD)


if __name__ == "__main__":
    unittest.main(verbosity=2)
