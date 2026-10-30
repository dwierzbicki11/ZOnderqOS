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
APPLICATION_MANAGER = code("source/GUI/Application/ApplicationManager.cs")
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

    def test_existing_windows_reflow_when_desktop_mode_changes(self):
        self.assertIn("private void SyncDesktopBounds()", WINDOW)
        self.assertIn("observedDesktopWidth == desktopWidth && observedDesktopHeight == desktopHeight", WINDOW)
        self.assertIn("observedDesktopWidth = desktopWidth;", WINDOW)
        self.assertIn("observedDesktopHeight = desktopHeight;", WINDOW)
        self.assertIn("SyncDesktopBounds();\n            interactionFrame++;", WINDOW)
        self.assertIn("SyncDesktopBounds();\n            focusProgress =", WINDOW)
        self.assertIn("if (IsMaximized)\n            {\n                MoveTo(0, 0);", WINDOW)

        # Visible windows keep their client area inside the new mode.
        for old_width, old_height in ((1920, 1080), (1366, 768)):
            old_x, old_y, old_w, old_h = fit_window(
                old_width - 500, old_height - 500, 820, 520, old_width, old_height)
            new_x, new_y, new_w, new_h = fit_window(
                old_x, old_y, old_w, old_h, 800, 600)
            self.assertLessEqual(new_x + new_w, 800)
            self.assertLessEqual(new_y + new_h, 556)

    def test_file_manager_context_menu_fits_resized_client(self):
        self.assertIn("internal int ContextMenuWidthForView =>", FILE_MANAGER)
        self.assertIn("internal int ContextMenuItemHeightForView =>", FILE_MANAGER)
        self.assertIn("internal int ContextMenuHeightForView =>", FILE_MANAGER)
        self.assertIn("view.Width - ContextMenuWidthForView - 4", FILE_MANAGER)
        self.assertIn("view.Height - ContextMenuHeightForView - FooterHeight - 4", FILE_MANAGER)
        self.assertIn("int item = (y - contextMenuY) / ContextMenuItemHeightForView;", FILE_MANAGER)
        self.assertIn("int rowHeight = app.ContextMenuItemHeightForView;", FILE_MANAGER)

        for window_width, window_height in ((320, 220), (480, 350), (800, 520)):
            view_width, view_height = window_width - 20, window_height - 50
            menu_width = min(240, max(1, view_width - 8))
            row_height = max(16, min(29, (view_height - 24 - 12) // 7))
            menu_height = row_height * 7
            self.assertLessEqual(menu_width + 8, view_width)
            self.assertLessEqual(menu_height + 24 + 8, view_height)
            for click_x, click_y in ((182, 60), (view_width - 5, view_height - 35)):
                menu_x = max(4, min(click_x, view_width - menu_width - 4))
                menu_y = max(4, min(click_y, view_height - menu_height - 24 - 4))
                self.assertLessEqual(menu_x + menu_width + 4, view_width)
                self.assertLessEqual(menu_y + menu_height + 24 + 4, view_height)

    def test_focus_restores_only_minimized_windows(self):
        activate = APPLICATION_MANAGER.split("public void Activate(", 1)[1].split(
            "public void ToggleMinimize(", 1)[0]
        toggle = APPLICATION_MANAGER.split("public void ToggleMinimize(", 1)[1].split(
            "public void Close(", 1)[0]
        self.assertIn("if (application.Window.IsMinimized)", activate)
        self.assertIn("application.Window.RestoreFromMinimized();", activate)
        self.assertEqual(activate.count("RestoreFromMinimized();"), 1)
        self.assertNotIn("RestoreFromMinimized();", toggle)
        self.assertIn("Activate(application);", toggle)

    def test_right_click_focuses_background_window_before_dispatch(self):
        mouse = APPLICATION_MANAGER.split(
            "public void HandleMouse(int mouseX, int mouseY, bool leftClicked", 1
        )[1].split("public void Update()", 1)[0]
        self.assertIn("(leftClicked && !leftWasClicked) || (rightClicked && !rightWasClicked)", mouse)
        self.assertLess(mouse.index("Activate(application);"),
                        mouse.index("target?.HandleMouse("))
        self.assertIn("application.Window.ContainsPoint(mouseX, mouseY)", mouse)

    def test_display_mode_change_cancels_stale_drag_and_resize(self):
        sync = WINDOW.split("private void SyncDesktopBounds()", 1)[1].split(
            "public void AddChild(", 1)[0]
        self.assertIn("dragging = false;", sync)
        self.assertIn("resizing = false;", sync)
        self.assertIn("resizeMode = 0;", sync)
        self.assertIn("hoveredResizeMode = 0;", sync)
        self.assertLess(sync.index("resizing = false;"), sync.index("if (IsMaximized)"))

    def test_overlay_menus_do_not_click_through_to_windows(self):
        self.assertIn("bool startMenuCaptures = startMenu.Visible &&", DESKTOP)
        self.assertIn("bool desktopMenuCaptures = desktopContextMenu != null &&", DESKTOP)
        self.assertIn("if (!startMenuCaptures && !desktopMenuCaptures)", DESKTOP)
        dispatch = DESKTOP.split("bool startMenuCaptures =", 1)[1].split(
            "startMenu.UpdateInteractions(", 1)[0]
        self.assertIn("applicationManager.HandleMouse(", dispatch)

        # Hit testing uses half-open rectangles, including right/bottom edges.
        def contains(x, y, ox, oy, w, h):
            return ox <= x < ox + w and oy <= y < oy + h

        self.assertTrue(contains(20, 20, 8, 8, 480, 540))
        self.assertFalse(contains(488, 20, 8, 8, 480, 540))
        self.assertFalse(contains(20, 548, 8, 8, 480, 540))
        self.assertTrue(contains(222, 206, 100, 100, 222, 192))

    def test_desktop_context_menu_is_modal_with_or_without_shortcuts(self):
        dispatch = DESKTOP.split("bool startMenuCaptures =", 1)[1].split(
            "int priorMouseX =", 1
        )[0]
        desktop = DESKTOP.split("private void UpdateDesktopInteractions(", 1)[1].split(
            "private bool IsPointOverWindow(", 1
        )[0]

        self.assertIn("bool desktopMenuWasVisible = desktopContextMenu != null &&", dispatch)
        self.assertIn("bool desktopMenuCaptures = desktopMenuWasVisible &&", dispatch)
        self.assertIn("(currentLeftButtonState && !previousLeftButtonState)", dispatch)
        self.assertIn("if (desktopMenuWasVisible)", dispatch)
        self.assertLess(dispatch.index("desktopContextMenu.UpdateInteractions("),
                        dispatch.index("applicationManager.HandleMouse("))
        self.assertIn("if (!desktopMenuCaptures)\n                            taskbar.UpdateInteractions(", dispatch)
        self.assertIn("if (!startMenuCaptures && !desktopMenuCaptures)\n                            UpdateDesktopInteractions(", dispatch)
        # The menu is handled once at the top-level dispatch, before the
        # ShowDesktopIcons=false early return. Do not double-dispatch actions.
        self.assertNotIn("desktopContextMenu.UpdateInteractions(", desktop)

        def capture(menu_open, inside, left_down, previous_left):
            return menu_open and (inside or (left_down and not previous_left))

        # Clicking a menu entry or outside the menu is consumed, even if
        # the action closes the overlay before other handlers are reached.
        for icons_visible in (True, False):
            self.assertTrue(capture(True, True, True, False), icons_visible)
            self.assertTrue(capture(True, False, True, False), icons_visible)
            self.assertFalse(capture(True, False, False, False), icons_visible)
            self.assertFalse(capture(False, True, True, False), icons_visible)

    def test_start_menu_action_does_not_click_desktop_shortcut_below(self):
        dispatch = DESKTOP.split("bool startMenuCaptures =", 1)[1].split(
            "int priorMouseX =", 1
        )[0]
        self.assertIn("if (!startMenuCaptures && !desktopMenuCaptures)", dispatch)
        self.assertLess(dispatch.index("startMenu.UpdateInteractions("),
                        dispatch.index("UpdateDesktopInteractions("))
        # The capture flag is computed before StartMenu.UpdateInteractions
        # can close the menu; never recompute it from the new Visible state.
        self.assertIn("startMenu.Visible &&", dispatch)
        self.assertLess(dispatch.index("startMenu.Visible &&"),
                        dispatch.index("startMenu.UpdateInteractions("))

    def test_file_manager_menu_reflows_and_dismisses_outside_view(self):
        update = FILE_MANAGER.split("public override void Update()", 1)[1].split(
            "public override void HandleMouse(", 1)[0]
        mouse = FILE_MANAGER.split("public override void HandleMouse(int mouseX", 1)[1].split(
            "private int HitEntryIndex(", 1)[0]
        self.assertIn("if (contextMenuVisible)", update)
        self.assertIn("view.Width - ContextMenuWidthForView - 4", update)
        self.assertIn("view.Height - ContextMenuHeightForView - FooterHeight - 4", update)
        self.assertIn("left && !oldLeft && dialogMode == 0 && contextMenuVisible", mouse)
        self.assertLess(mouse.index("if (ContextClick(x, y))"),
                        mouse.index("if (x < 0 || y < 0"))
        self.assertEqual(mouse.count("ContextClick(x, y)"), 1)

        # A menu opened on FHD must still fit after shrinking the view.
        for width, height in ((300, 170), (460, 300), (800, 465)):
            menu_w = min(240, max(1, width - 8))
            row_h = max(16, min(29, (height - 24 - 12) // 7))
            menu_h = row_h * 7
            x = max(4, min(680, width - menu_w - 4))
            y = max(4, min(350, height - menu_h - 24 - 4))
            self.assertLessEqual(x + menu_w + 4, width)
            self.assertLessEqual(y + menu_h + 24 + 4, height)

    def test_file_manager_selection_survives_viewport_resize(self):
        update = FILE_MANAGER.split("public override void Update()", 1)[1].split(
            "public override void HandleMouse(", 1)[0]
        selection = FILE_MANAGER.split("private void EnsureSelectionVisible()", 1)[1].split(
            "private void GoUp()", 1)[0]
        self.assertIn("view.Width != previousViewWidth || view.Height != previousViewHeight", update)
        self.assertIn("EnsureSelectionVisible();", update)
        self.assertIn("if (selectedIndex >= 0)", selection)

        # Model the selection/scroll math on a narrow and a wide client.
        for columns, rows in ((1, 1), (2, 4), (5, 4)):
            count = 40
            visible = columns * rows
            for selected in (-1, 0, 13, 39):
                scroll = 28
                if selected >= 0:
                    if selected < scroll:
                        scroll = selected
                    elif selected >= scroll + visible:
                        scroll = selected - visible + columns
                scroll -= scroll % columns
                scroll = max(0, scroll)
                maximum = max(0, count - visible)
                if scroll > maximum:
                    scroll = max(0, maximum - maximum % columns)
                self.assertGreaterEqual(scroll, 0)
                self.assertLessEqual(scroll, maximum)
                if selected >= 0:
                    self.assertLessEqual(scroll, selected)
                    self.assertLess(selected, scroll + visible)

    def test_failed_unmount_preserves_file_manager_location(self):
        unmount = FILE_MANAGER.split("private void UnmountVolume(", 1)[1].split(
            "private static bool IsInsidePath(", 1)[0]
        self.assertIn("bool viewingVolume =", unmount)
        self.assertLess(unmount.index("StorageMountManager.TryUnmount("),
                        unmount.index('currentPath = "/root";'))
        self.assertIn("if (viewingVolume)", unmount)

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
