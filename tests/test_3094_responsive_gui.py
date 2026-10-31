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
TASKBAR = code("source/GUI/Taskbar.cs")
NOTIFICATIONS = code("source/GUI/NotificationCenter.cs")
PROJECT = code("ZonderqOS.csproj")
QEMU_SMOKE = code("tools/smoke-services-qemu.sh")
QEMU_WORKFLOW = code(".github/workflows/services-qemu.yml")
QMP_LOGIN = code("tools/qmp-gui-login.py")
COMMAND_PALETTE = code("source/GUI/CommandPalette.cs")


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
        window_mouse = WINDOW.split("public void HandleMouse(", 1)[1].split(
            "private int GetResizeMode(", 1)[0]
        self.assertLess(window_mouse.index("SyncDesktopBounds();"),
                        window_mouse.index("int closeX = "))
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
        self.assertIn("bool startMenuCaptures = startMenuInside || startMenuOutsideClick;", DESKTOP)
        self.assertIn("bool desktopMenuCaptures = desktopMenuWasVisible &&", DESKTOP)
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
        self.assertIn("if (!desktopMenuCaptures && !fileManagerMenuCaptures)\n                            taskbar.UpdateInteractions(", dispatch)
        self.assertIn("if (!startMenuCaptures && !desktopMenuCaptures && !fileManagerMenuCaptures && !windowCapturedClick)\n                            UpdateDesktopInteractions(", dispatch)
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
        self.assertIn("startMenuInside || startMenuOutsideClick", DESKTOP)
        self.assertIn("if (startMenuOutsideClick)", DESKTOP)
        self.assertLess(DESKTOP.index("bool startMenuOutsideClick ="),
                        DESKTOP.index("startMenu.UpdateInteractions("))

    def test_taskbar_overflow_pages_are_visible_and_clickable(self):
        self.assertIn("private int firstVisibleAppIndex;", TASKBAR)
        self.assertIn("private int GetPageSlots(int runningCount, out bool overflow)", TASKBAR)
        self.assertIn("overflow = runningCount > slots && slots >= 2;", TASKBAR)
        self.assertIn("firstVisibleAppIndex += pageSlots;", TASKBAR)
        self.assertIn("if (firstVisibleAppIndex >= runningCount)", TASKBAR)
        self.assertEqual(TASKBAR.count("int pageSlots = GetPageSlots(runningCount, out overflow);"), 2)
        self.assertEqual(TASKBAR.count("if (runningIndex++ < firstVisibleAppIndex)"), 2)
        self.assertEqual(TASKBAR.count("if (shown >= pageSlots)"), 2)
        self.assertEqual(TASKBAR.count("int arrowX = OverflowButtonX(pageSlots);"), 2)

        # Pure geometry check for the render/hit-test layout. The last
        # button is reserved for the page switcher only when necessary.
        self.assertIn("private const int CompactTrayWidth = 132;", TASKBAR)
        self.assertIn("return Width < 560 || !global::ZonderqOS.SystemSettings.ShowTrayStatus", TASKBAR)
        self.assertIn("Width - GetTrayWidth() - (StartButtonWidth + SidePadding + 6)", TASKBAR)
        self.assertEqual(TASKBAR.count("int trayX = Width - GetTrayWidth();"), 2)
        self.assertIn("GetTrayWidth() == TrayWidth &&", TASKBAR)

        # At 320px the full 278px tray obscured the Start button and
        # left zero application slots. The compact tray keeps the clock
        # and notification button, while reserving space for paging.
        for screen_width in (320, 360, 400, 480, 540, 559, 560, 640,
                             800, 1024, 1366, 1920):
            tray_width = 132 if screen_width < 560 else 278
            tray_x = screen_width - tray_width
            self.assertGreaterEqual(tray_x, 48)
            notification_x = tray_x + 9
            self.assertLessEqual(notification_x + 34, screen_width - 88)
            self.assertLessEqual(screen_width - 78 + 68, screen_width)
            slots = max(0, (tray_x - 60 + 5) // 45)
            self.assertGreaterEqual(slots, 2)
            for running_count in (0, 1, 2, 9, 10, 11, 23, 50):
                overflow = running_count > slots and slots >= 2
                page_slots = slots - 1 if overflow else slots
                self.assertGreaterEqual(page_slots, 0)
                if page_slots:
                    last_button_right = 60 + (page_slots - 1) * 45 + 40
                    self.assertLessEqual(last_button_right, tray_x)
                if overflow:
                    arrow_x = 60 + page_slots * 45
                    self.assertLessEqual(arrow_x + 40, tray_x)
                    offsets = list(range(0, running_count, page_slots))
                    self.assertEqual(
                        [index for offset in offsets
                         for index in range(offset, min(running_count, offset + page_slots))],
                        list(range(running_count))
                    )
                    self.assertTrue(offsets[-1] + page_slots >= running_count)
                else:
                    self.assertLessEqual(running_count, page_slots)

    def test_taskbar_reclaims_app_slots_when_status_tiles_are_disabled(self):
        # The same tray geometry must drive painting, hit testing and paging.
        # Disabling NET/VOL status must never reserve their old 278px width.
        self.assertIn("if (GetTrayWidth() == TrayWidth &&", TASKBAR)
        self.assertEqual(TASKBAR.count("int trayX = Width - GetTrayWidth();"), 2)
        for screen_width in (320, 400, 560, 640, 800, 1024, 1366, 1920):
            compact = 132
            full = 278
            for show_status in (False, True):
                tray_width = full if show_status and screen_width >= 560 else compact
                tray_x = screen_width - tray_width
                slots = max(0, (tray_x - 60 + 5) // 45)
                self.assertGreaterEqual(slots, 2)
                self.assertGreaterEqual(tray_x, 48)
                self.assertLessEqual(tray_x + 9 + 34, screen_width - 88)
                if screen_width >= 560 and not show_status:
                    full_slots = max(0, (screen_width - full - 60 + 5) // 45)
                    self.assertGreaterEqual(slots, full_slots)
                for running_count in (0, 2, 5, 20):
                    overflow = running_count > slots and slots >= 2
                    page_slots = slots - 1 if overflow else slots
                    if overflow:
                        self.assertLessEqual(60 + page_slots * 45 + 40, tray_x)

    def test_taskbar_focus_switch_reveals_active_page_without_resetting_manual_page(self):
        sync = TASKBAR.split("private void SyncActivePage(", 1)[1].split(
            "private int OverflowButtonX(", 1
        )[0]
        render = TASKBAR.split("public override void Render(Canvas canvas)", 1)[1].split(
            "private void UpdateTrayCache()", 1
        )[0]
        mouse = TASKBAR.split("public void UpdateInteractions(", 1)[1]
        self.assertIn("int activeRunningIndex = RunningIndexOf(apps, activeApplication);", sync)
        self.assertIn("activeApplication != lastActiveApplication ||", sync)
        self.assertIn("activeRunningIndex != lastActiveRunningIndex ||", sync)
        self.assertIn("pageSlots != lastPageSlots", sync)
        self.assertIn("firstVisibleAppIndex = (activeRunningIndex / pageSlots) * pageSlots;", sync)
        self.assertLess(sync.index("NormalizeAppPage(runningCount, pageSlots, overflow);"),
                        sync.index("int activeRunningIndex = RunningIndexOf("))
        self.assertIn("SyncActivePage(apps, activeApplication, runningCount, pageSlots, overflow);", render)
        self.assertIn("SyncActivePage(apps, applicationManager.ActiveApplication, runningCount, pageSlots, overflow);", mouse)
        self.assertLess(mouse.index("SyncActivePage("), mouse.index("if (runningIndex++ < firstVisibleAppIndex)"))
        self.assertIn("private int lastPageSlots = -1;", TASKBAR)

        # A resolution change must reveal the focused app even when its
        # index stays the same. Manual paging is preserved otherwise.
        for old_slots, new_slots, active_index in (
            (18, 9, 15), (9, 18, 15), (21, 9, 23), (9, 9, 49)
        ):
            focus_page = (active_index // new_slots) * new_slots
            self.assertLessEqual(focus_page, active_index)
            self.assertLess(active_index, focus_page + new_slots)
            old_page = (active_index // old_slots) * old_slots
            if old_slots != new_slots:
                self.assertEqual(focus_page, (active_index // new_slots) * new_slots)
            else:
                manual_page = old_page + old_slots
                self.assertNotEqual(manual_page, focus_page)

    def test_start_menu_outside_click_dismisses_without_click_through(self):
        dispatch = DESKTOP.split("bool startMenuWasVisible =", 1)[1].split(
            "int priorMouseX =", 1
        )[0]
        self.assertIn("bool startMenuOutsideClick = startMenuWasVisible && !startMenuInside &&", dispatch)
        self.assertIn("mouseY < taskbar.Y) ||", dispatch)
        self.assertIn("rightPressed);", dispatch)
        self.assertIn("if (startMenuOutsideClick)\n                            startMenu.Visible = false;", dispatch)
        self.assertIn("bool startMenuCaptures = startMenuInside || startMenuOutsideClick;", dispatch)
        self.assertLess(dispatch.index("startMenu.Visible = false;"),
                        dispatch.index("applicationManager.HandleMouse("))
        self.assertIn("if (!startMenuCaptures && !desktopMenuCaptures)", dispatch)

        def captures(menu_open, inside, left, old_left, on_taskbar):
            outside = menu_open and not inside and left and not old_left and not on_taskbar
            return inside or outside

        self.assertTrue(captures(True, False, True, False, False))
        self.assertFalse(captures(True, False, True, False, True))
        self.assertTrue(captures(True, True, True, False, False))
        self.assertFalse(captures(False, False, True, False, False))

    def test_right_click_dismisses_overlays_without_click_through(self):
        # A right-click is an edge, not a held-button state. The modal
        # overlay must consume the edge before any app or desktop handler.
        dispatch = DESKTOP.split("bool rightPressed =", 1)[1].split(
            "int priorMouseX =", 1
        )[0]
        self.assertIn("currentRightButtonState && !previousRightButtonState;", DESKTOP)
        self.assertRegex(dispatch, r"if \(rightPressed\)\s*\{\s*commandPalette\.Close\(\);")
        self.assertIn("paletteCaptured = true;", dispatch)
        self.assertIn("notificationCenter.Visible && rightPressed", dispatch)
        self.assertIn("notificationCenter.Close();", dispatch)
        self.assertIn("notificationCaptured = true;", dispatch)
        self.assertIn("if (!paletteCaptured && !notificationCaptured)", dispatch)
        self.assertIn("rightPressed);", dispatch)
        self.assertRegex(dispatch, r"if \(rightPressed\)\s*desktopContextMenu\.Visible = false;")
        self.assertRegex(dispatch, r"if \(!rightPressed && !fileManagerMenuCaptures\)\s*startMenu\.UpdateInteractions\(")
        self.assertLess(dispatch.index("commandPalette.Close();"),
                        dispatch.index("applicationManager.HandleMouse("))
        self.assertLess(dispatch.index("desktopContextMenu.Visible = false;"),
                        dispatch.index("applicationManager.HandleMouse("))

        def right_edge(now, old):
            return now and not old

        def start_captures(opened, inside, left_edge, right_edge_value, on_taskbar):
            outside = opened and not inside and ((left_edge and not on_taskbar) or right_edge_value)
            return opened and inside or outside

        def desktop_captures(opened, inside, left_edge, right_edge_value):
            return opened and (inside or left_edge or right_edge_value)

        for inside in (False, True):
            for on_taskbar in (False, True):
                self.assertTrue(start_captures(True, inside, False, right_edge(True, False), on_taskbar))
                self.assertTrue(desktop_captures(True, inside, False, right_edge(True, False)))
                self.assertFalse(start_captures(False, inside, False, True, on_taskbar))
                self.assertFalse(desktop_captures(False, inside, False, True))
        self.assertFalse(right_edge(True, True))

    def test_window_titlebar_close_does_not_activate_desktop_below(self):
        dispatch = DESKTOP.split("bool windowCapturedClick =", 1)[1].split(
            "int priorMouseX =", 1
        )[0]
        self.assertIn("IsPointOverWindow(mouseX, mouseY);", dispatch)
        self.assertLess(dispatch.index("IsPointOverWindow(mouseX, mouseY);"),
                        dispatch.index("applicationManager.HandleMouse("))
        self.assertIn(
            "if (!startMenuCaptures && !desktopMenuCaptures && !fileManagerMenuCaptures && !windowCapturedClick)",
            dispatch
        )
        self.assertLess(dispatch.index("applicationManager.HandleMouse("),
                        dispatch.index("UpdateDesktopInteractions("))

        # Closing/minimizing a window can expose a desktop icon at exactly
        # the pointer position. Ownership must be snapshotted before the
        # application processes the click, not recalculated afterward.
        for was_window_under_pointer in (True, False):
            captured = True and was_window_under_pointer
            desktop_receives_click = not captured
            self.assertEqual(desktop_receives_click, not was_window_under_pointer)

    def test_titlebar_double_click_is_timed_and_reset_on_focus_loss(self):
        mouse = WINDOW.split("public void HandleMouse(int mouseX", 1)[1].split(
            "private int GetResizeMode(", 1)[0]
        active = WINDOW.split("public bool IsActive", 1)[1].split(
            "public System.Action CloseAction", 1)[0]
        sync = WINDOW.split("private void SyncDesktopBounds()", 1)[1].split(
            "public void AddChild(", 1)[0]
        self.assertIn("long now = Stopwatch.GetTimestamp();", mouse)
        self.assertIn("elapsed <= Stopwatch.Frequency / 2", mouse)
        self.assertIn("lastTitleClickTimestamp = 0;", active)
        self.assertIn("lastTitleClickTimestamp = 0;", sync)
        self.assertIn("lastTitleClickTimestamp = 0;", mouse)
        self.assertIn("if (hoveredControl != 0 || hoveredResizeMode != 0)", mouse)
        self.assertIn("if (newX != X || newY != Y)\n                        lastTitleClickTimestamp = 0;", mouse)
        self.assertLess(mouse.index("if (hoveredControl != 0 || hoveredResizeMode != 0)"),
                        mouse.index("if (hoveredControl == 1)"))
        self.assertNotIn("lastTitleClickFrame", WINDOW)
        self.assertNotIn("interactionFrame", WINDOW)

        # A long render stall cannot turn two separated clicks into a
        # maximize event. Switching focus also discards pending clicks.
        frequency = 10_000_000
        def is_double(first, second):
            elapsed = second - first
            return first > 0 and elapsed >= 0 and elapsed <= frequency // 2

        self.assertTrue(is_double(100_000_000, 103_000_000))
        self.assertFalse(is_double(100_000_000, 130_000_000))
        self.assertFalse(is_double(0, 103_000_000))
        self.assertFalse(is_double(103_000_000, 102_000_000))

    def test_file_manager_grid_columns_and_rows_match_render_and_hit_test(self):
        columns = FILE_MANAGER.split("private int Columns()", 1)[1].split(
            "private void Select(", 1)[0]
        selection = FILE_MANAGER.split("private void EnsureSelectionVisible()", 1)[1].split(
            "private void GoUp()", 1)[0]
        view = FILE_MANAGER.split("internal sealed class FileManagerView", 1)[1]
        self.assertIn("internal int GridWidthForView =>", FILE_MANAGER)
        self.assertIn("internal int GridHeightForView =>", FILE_MANAGER)
        self.assertIn("GridWidthForView + TileGap", columns)
        self.assertIn("GridHeightForView + TileGap", selection)
        self.assertIn("int contentH = app.GridHeightForView;", view)
        self.assertIn("int gridW = app.GridWidthForView;", view)

        # At view width 418 the previous -14/-12 disagreement painted two
        # columns but navigation/hit-testing knew only one.
        for view_width in (300, 417, 418, 419, 535, 536, 654, 780):
            grid_width = max(112, view_width - 176 - 12)
            painted_columns = max(1, (grid_width + 6) // (112 + 6))
            hit_columns = max(1, (grid_width + 6) // (112 + 6))
            self.assertEqual(painted_columns, hit_columns)
        self.assertEqual((max(112, 418 - 188) + 6) // 118, 2)

        # At heights 276..279 the old -12 scroll math had one row while
        # the painter could show two. Both now use the same grid height.
        for view_height in (170, 275, 276, 277, 279, 280, 465, 700):
            grid_height = max(40, view_height - 24 - 50 - 8)
            painted_rows = max(1, (grid_height + 6) // (94 + 6))
            scroll_rows = max(1, (grid_height + 6) // (94 + 6))
            self.assertEqual(painted_rows, scroll_rows)
        self.assertEqual((max(40, 276 - 82) + 6) // 100, 2)

    def test_file_manager_sidebar_hit_testing_excludes_clipped_volume_rows(self):
        sidebar = FILE_MANAGER.split("private SidebarEntry GetSidebarEntryAt(int y)", 1)[1].split(
            "private void MountVolume(", 1)[0]
        render = FILE_MANAGER.split("private void RenderSidebar(Canvas canvas", 1)[1].split(
            "private bool IsEntryActive(", 1)[0]
        self.assertIn("int sidebarBottom = ContentTop + GridHeightForView;", sidebar)
        self.assertIn("internal int GridHeightForView =>", FILE_MANAGER)
        self.assertIn("if (itemY + height > sidebarBottom)", sidebar)
        self.assertLess(sidebar.index("if (itemY + height > sidebarBottom)"),
                        sidebar.index("if (y >= itemY && y < itemY + height)"))
        self.assertIn("if (itemY + rowHeight > y + height)", render)

        # RenderSidebar paints only complete rows. Hit-testing must not
        # activate a hidden mount/unmount button on a clipped row.
        for view_height in (170, 220, 300, 465, 700):
            content_bottom = max(50 + 40, view_height - 24 - 8)
            item_y = 50 + 8
            row_heights = (22, 36, 36, 22, 36, 36, 36, 36)
            visible = []
            for row_height in row_heights:
                if item_y + row_height > content_bottom:
                    break
                visible.append((item_y, item_y + row_height))
                item_y += row_height
            for y in range(50, content_bottom + 1):
                hit = next((i for i, (top, bottom) in enumerate(visible)
                            if top <= y < bottom), None)
                self.assertEqual(hit is not None,
                                 any(top <= y < bottom for top, bottom in visible))
            if view_height == 170:
                self.assertEqual(len(visible), 2)
                self.assertFalse(any(top <= 125 < bottom for top, bottom in visible))

    def test_file_manager_double_click_does_not_leak_across_navigation(self):
        mouse = FILE_MANAGER.split("public override void HandleMouse(int mouseX", 1)[1].split(
            "private int HitEntryIndex(", 1)[0]
        refresh = FILE_MANAGER.split("private void RefreshInternal(string keep)", 1)[1].split(
            "private void RebuildSidebar()", 1)[0]
        self.assertIn("private void ResetClickSequence()", mouse)
        self.assertIn("ResetClickSequence();\\n                OpenEntry(index);".replace("\\n", "\n"), mouse)
        self.assertLess(mouse.index("ResetClickSequence();\n                OpenEntry(index);"),
                        mouse.index("EnsureSelectionVisible();"))
        self.assertIn("lastClickIndex = index;\n                lastClickTimestamp = now;", mouse)
        self.assertIn("long now = Stopwatch.GetTimestamp();", mouse)
        self.assertIn("elapsed <= Stopwatch.Frequency / 2", mouse)
        self.assertNotIn("frameCounter - clickFrame", FILE_MANAGER)
        self.assertIn("ResetClickSequence();\n                if (dialogMode != 0)", mouse)
        self.assertIn("ResetClickSequence();", refresh)
        self.assertIn("ResetClickSequence();\n            if (x < 42)", FILE_MANAGER)
        self.assertIn("ResetClickSequence();\n            SidebarEntry entry =", FILE_MANAGER)

        # Two clicks on the same index open once, then the sequence is
        # consumed. A subsequent click in a newly opened folder is single.
        last_index, last_frame, selected = -1, -1000, -1
        opens = 0
        for frame, index in ((100, 0), (105, 0), (110, 0), (115, 0)):
            if selected == index and last_index == index and frame - last_frame <= 25:
                opens += 1
                last_index, last_frame = -1, -1000
                selected = -1  # RefreshInternal after directory navigation
            else:
                selected, last_index, last_frame = index, index, frame
        self.assertEqual(opens, 2)

    def test_file_manager_focus_loss_clears_pending_double_click(self):
        update = FILE_MANAGER.split("public override void Update()", 1)[1].split(
            "public override void HandleMouse(", 1)[0]
        self.assertIn("if (!Window.IsActive)\n            {\n                ResetClickSequence();", update)
        self.assertIn("contextMenuVisible = false;", update)
        self.assertLess(update.index("contextMenuVisible = false;"),
                        update.index("int previousViewWidth = view.Width;"))
        self.assertLess(update.index("ResetClickSequence();"),
                        update.index("int previousViewWidth = view.Width;"))

    def test_file_manager_double_click_uses_elapsed_time_not_frame_count(self):
        # An expensive filesystem operation can stall the GUI loop. Even if
        # no frames are rendered, two clicks seconds apart must not open a file.
        frequency = 10_000_000
        def is_double(first, second):
            elapsed = second - first
            return first > 0 and elapsed >= 0 and elapsed <= frequency // 2

        self.assertTrue(is_double(100_000_000, 104_000_000))
        self.assertTrue(is_double(100_000_000, 105_000_000))
        self.assertFalse(is_double(100_000_000, 105_000_001))
        self.assertFalse(is_double(100_000_000, 140_000_000))
        self.assertFalse(is_double(100_000_000, 99_000_000))
        self.assertFalse(is_double(0, 1_000_000))

    def test_file_manager_short_grid_does_not_paint_selection_into_footer(self):
        grid = FILE_MANAGER.split("private void Grid(Canvas canvas", 1)[1].split(
            "private static IconType ResolveEntryIcon", 1)[0]
        self.assertIn("int visibleTileHeight = Math.Min(FileManagerApp.TileHeight, y + height - tileY);", grid)
        self.assertIn("if (visibleTileHeight <= 0)", grid)
        self.assertEqual(grid.count("FileManagerApp.TileWidth, visibleTileHeight"), 2)
        self.assertIn("tileY, 3, visibleTileHeight", grid)
        # Minimum Window(320x220): view 300x170, grid height 88,
        # selection used to extend 6px into the footer.
        for window_height in (220, 226, 240, 320, 520):
            view_height = window_height - 50
            grid_height = max(40, view_height - 24 - 50 - 8)
            rows = max(1, (grid_height + 6) // 100)
            for row in range(rows):
                tile_y = row * 100
                visible = min(94, grid_height - tile_y)
                self.assertGreater(visible, 0)
                self.assertLessEqual(tile_y + visible, grid_height)
        self.assertEqual(min(94, 88), 88)

    def test_file_manager_double_click_requires_nearby_pointer(self):
        mouse = FILE_MANAGER.split("public override void HandleMouse(int mouseX", 1)[1].split(
            "private void ResetClickSequence()", 1)[0]
        self.assertIn("System.Math.Abs(mouseX - lastClickX) <= 12", mouse)
        self.assertIn("System.Math.Abs(mouseY - lastClickY) <= 12", mouse)
        self.assertIn("lastClickX = mouseX;", mouse)
        self.assertIn("lastClickY = mouseY;", mouse)

        def double_click(first, second, elapsed_ms):
            return (0 <= elapsed_ms <= 500 and
                    abs(second[0] - first[0]) <= 12 and
                    abs(second[1] - first[1]) <= 12)

        self.assertTrue(double_click((130, 190), (142, 178), 450))
        self.assertFalse(double_click((130, 190), (143, 190), 100))
        self.assertFalse(double_click((130, 190), (130, 177), 100))
        self.assertFalse(double_click((130, 190), (130, 190), 501))

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

    def test_notification_center_keyboard_scroll_matches_painted_rows(self):
        self.assertIn("private int VisibleRowCount", NOTIFICATIONS)
        self.assertIn("int availableHeight = System.Math.Max(0, Height - 130 - 72);", NOTIFICATIONS)
        self.assertIn("System.Math.Min(VisibleRows, rows)", NOTIFICATIONS)
        self.assertEqual(NOTIFICATIONS.count("for (int row = 0; row < VisibleRowCount; row++)"), 2)
        self.assertIn("selectedIndex >= offset + VisibleRowCount", NOTIFICATIONS)
        self.assertIn("visibleCount - VisibleRowCount", NOTIFICATIONS)

        # The 800x600 panel paints only three rows; using the old fixed
        # eight-row scroll window made keyboard selection disappear.
        for screen_height, expected_rows in ((600, 3), (768, 5), (1080, 8)):
            panel_height = screen_height - 44 - 20
            available_height = max(0, panel_height - 130 - 72)
            rows = max(1, min(8, (available_height + 7) // (82 + 7)))
            self.assertEqual(rows, expected_rows)
            self.assertLessEqual(130 + (rows - 1) * 89 + 82, panel_height - 72)
            visible_count = 20
            offset = 0
            for selected in range(visible_count):
                if selected < offset:
                    offset = selected
                elif selected >= offset + rows:
                    offset = selected - rows + 1
                offset = min(max(0, offset), max(0, visible_count - rows))
                self.assertLessEqual(offset, selected)
                self.assertLess(selected, offset + rows)
            # Keyboard wrap to the first notification must reset scrolling.
            selected = 0
            if selected < offset:
                offset = selected
            self.assertEqual(offset, 0)

    def test_typography_and_editor_shared_styles(self):
        self.assertIn("PCScreenFont.DefaultFont", TYPOGRAPHY)
        self.assertIn("SmallTextRenderer.DrawClipped(", TYPOGRAPHY)
        self.assertNotIn("Substring(", TYPOGRAPHY)
        self.assertIn("UiTypography.Heading(", WINDOW)
        self.assertIn("UiTypography.Heading(", SETTINGS)
        self.assertIn("UiPainter.RoundedPanel(", ABOUT)
        self.assertIn("UiPainter.RoundedPanel(", FILE_MANAGER)
        self.assertIn("UiPainter.RoundedPanel(", NOTEPAD)


    def test_file_manager_search_backspace_edits_filter_before_navigation(self):
        keyboard = FILE_MANAGER.split("public override void HandleKeyboard(KeyEvent key)", 1)[1].split(
            "private void CopySelected(", 1)[0]
        refresh = FILE_MANAGER.split("private void RefreshInternal(string keep)", 1)[1].split(
            "private void RebuildSidebar()", 1)[0]
        backspace = keyboard.split("if (key.Key == ConsoleKeyEx.Backspace)", 1)[1].split(
            "if (key.Key == ConsoleKeyEx.LeftArrow)", 1)[0]
        self.assertIn("if (searchText.Length > 0)", backspace)
        self.assertIn("searchText = searchText.Substring(0, searchText.Length - 1);", backspace)
        self.assertIn("ApplySearchFilter();", backspace)
        self.assertIn("else\n                {\n                    GoUp();", backspace)
        self.assertLess(backspace.index("ApplySearchFilter();"), backspace.index("GoUp();"))
        self.assertIn('"Search: " + searchText + " (" + entries.Count + " item(s))"', refresh)

        # Backspace changes a nonempty filter, but with an empty filter it
        # keeps the preexisting parent-directory shortcut.
        filter_text = "notes"
        navigations = 0
        for expected in ("note", "not", "no", "n", ""):
            if filter_text:
                filter_text = filter_text[:-1]
            else:
                navigations += 1
            self.assertEqual(filter_text, expected)
            self.assertEqual(navigations, 0)
        if filter_text:
            filter_text = filter_text[:-1]
        else:
            navigations += 1
        self.assertEqual(navigations, 1)


    def test_file_manager_page_navigation_uses_visible_grid_geometry(self):
        keyboard = FILE_MANAGER.split("public override void HandleKeyboard(KeyEvent key)", 1)[1].split(
            "private void CopySelected(", 1)[0]
        page = FILE_MANAGER.split("private void SelectPage(int direction)", 1)[1].split(
            "private void Select(int delta)", 1)[0]
        selection = FILE_MANAGER.split("private void Select(int delta)", 1)[1].split(
            "private void EnsureSelectionVisible()", 1)[0]
        self.assertIn("ConsoleKeyEx.PageUp", keyboard)
        self.assertIn("SelectPage(-1);", keyboard)
        self.assertIn("ConsoleKeyEx.PageDown", keyboard)
        self.assertIn("SelectPage(1);", keyboard)
        self.assertIn("GridHeightForView + TileGap", page)
        self.assertIn("Select(direction * Columns() * rows);", page)
        self.assertIn("ResetClickSequence();", selection)
        self.assertIn("contextMenuVisible = false;", selection)
        self.assertIn("ResetClickSequence();\n                if (selectedIndex >= 0)", keyboard)

        # PageUp/PageDown always move by the same number of painted tiles
        # that the selection visibility calculation uses.
        for width, height in ((300, 170), (418, 276), (780, 465), (1100, 700)):
            grid_width = max(112, width - 176 - 12)
            grid_height = max(40, height - 24 - 50 - 8)
            cols = max(1, (grid_width + 6) // (112 + 6))
            rows = max(1, (grid_height + 6) // (94 + 6))
            visible = cols * rows
            selected = 17
            self.assertEqual(min(39, selected + cols * rows),
                             min(39, selected + visible))
            self.assertEqual(max(0, selected - cols * rows),
                             max(0, selected - visible))


    def test_file_manager_context_menu_dismissal_consumes_mouse_press(self):
        mouse = FILE_MANAGER.split("public override void HandleMouse(int mouseX", 1)[1].split(
            "private int HitEntryIndex(", 1)[0]
        self.assertIn("if (left && !oldLeft && dialogMode == 0 && contextMenuVisible)", mouse)
        self.assertIn("if (right && !oldRight && dialogMode == 0 && contextMenuVisible)", mouse)
        self.assertLess(mouse.index("if (ContextClick(x, y))"),
                        mouse.index("Window.HandleMouse(mouseX, mouseY, left, oldLeft);"))
        self.assertLess(mouse.index("ResetClickSequence();\n                return;"),
                        mouse.index("Window.HandleMouse(mouseX, mouseY, left, oldLeft);"))
        self.assertLess(mouse.index("Window.HandleMouse(mouseX, mouseY, left, oldLeft);"),
                        mouse.index("if (x < 0 || y < 0"))
        self.assertEqual(mouse.count("ContextClick(x, y)"), 1)

        # A dismissal click over the sidebar, toolbar, or titlebar must
        # never be replayed as a mount/unmount or window-close action.
        for left, right, menu_hit in ((True, False, False),
                                      (False, True, False),
                                      (True, False, True)):
            menu_open = True
            consumed = menu_open and (left or right)
            dispatched_to_window = not consumed
            dispatched_to_sidebar = not consumed
            self.assertFalse(dispatched_to_window)
            self.assertFalse(dispatched_to_sidebar)


    def test_file_manager_modal_menu_owns_taskbar_and_window_clicks(self):
        dispatch = DESKTOP.split("bool windowCapturedClick =", 1)[1].split(
            "if (lockRequested)", 1)[0]
        self.assertIn("FileManagerApp activeFileManager = applicationManager.ActiveApplication as FileManagerApp;", dispatch)
        self.assertIn("activeFileManager != null && activeFileManager.ContextMenuVisible", dispatch)
        self.assertIn("activeFileManager.ContextMenuVisible", dispatch)
        self.assertIn("activeFileManager.HandleMouse(mouseX, mouseY, currentLeftButtonState,", dispatch)
        self.assertIn("else if (!startMenuCaptures && !desktopMenuCaptures)", dispatch)
        self.assertIn("if (!rightPressed && !fileManagerMenuCaptures)", dispatch)
        self.assertIn("if (!desktopMenuCaptures && !fileManagerMenuCaptures)", DESKTOP)
        self.assertIn("!fileManagerMenuCaptures && !windowCapturedClick", DESKTOP)
        self.assertLess(dispatch.index("bool fileManagerMenuCaptures ="),
                        dispatch.index("applicationManager.HandleMouse("))

        # A click on another window or the taskbar while a File Manager
        # context menu is open must dismiss the menu without focus/volume
        # changes; the following click may then perform the normal action.
        menu_open = True
        focus_changes = 0
        taskbar_actions = 0
        for press in range(2):
            captured = menu_open
            if captured:
                menu_open = False
            else:
                focus_changes += 1
                taskbar_actions += 1
            self.assertEqual((focus_changes, taskbar_actions),
                             (0, 0) if press == 0 else (1, 1))


    def test_task_manager_compact_rows_fit_small_window_widths(self):
        table = TASK_MANAGER.split("private void RenderTable(Canvas canvas, int x, int width)", 1)[1].split(
            "private void DrawRow(", 1
        )[0]
        row = TASK_MANAGER.split("private void DrawRow(Canvas canvas, ModernTaskRow row", 1)[1].split(
            "private void RenderPerformance(", 1
        )[0]
        self.assertIn("if (width < 520)", table)
        self.assertIn("if (width < 520)", row)
        self.assertIn('app.ActivePage == 2 ? "PID / NAME" : "NAME"', table)
        self.assertIn("int textWidth = Math.Max(1, width - nameOffset - 4);", row)
        self.assertIn("SmallTextRenderer.DrawClipped(canvas, row.Name,", row)
        self.assertIn("SmallTextRenderer.DrawClipped(canvas, row.State,", row)
        self.assertLess(row.index("if (width < 520)"), row.index("int statusX = x + Math.Max(370, width - 240);"))
        footer = TASK_MANAGER.split("private void RenderFooter(Canvas canvas)", 1)[1]
        self.assertIn("if (width < 520)", footer)
        self.assertIn("Math.Max(1, width - 16), Text);", footer)
        self.assertLess(footer.index("if (width < 520)"),
                        footer.index('"F5 REFRESH   DEL END TASK"'))

        for window_width in (320, 400, 480, 640, 800, 1040):
            view_width = max(1, window_width - 20)
            table_width = view_width - 170 - 12
            row_width = table_width - 16
            if table_width < 520:
                for name_offset in (34, 52):
                    text_width = max(1, row_width - name_offset - 4)
                    self.assertLessEqual(name_offset + text_width, row_width)
            else:
                self.assertGreaterEqual(table_width, 520)


    def test_qemu_framebuffer_capture_is_optional_and_not_a_gui_pass(self):
        self.assertIn('CAPTURE_SCREEN="${SERVICE_QEMU_CAPTURE_SCREEN:-0}"', QEMU_SMOKE)
        self.assertIn('"screendump"', QEMU_SMOKE)
        self.assertIn('"filename": sys.argv[2]', QEMU_SMOKE)
        self.assertIn('QMP screendump failed:', QEMU_SMOKE)
        self.assertIn('QMP screendump missing', QEMU_SMOKE)
        self.assertIn("with open(sys.argv[1], 'rb') as screenshot:", QEMU_SMOKE)
        self.assertIn("if len(pixels) != width * height * 3:", QEMU_SMOKE)
        self.assertIn("NOT that GUI interactions work", QEMU_SMOKE)
        self.assertIn("SERVICE_QEMU_CAPTURE_SCREEN: '1'", QEMU_WORKFLOW)
        self.assertIn("services-qemu-output/*.ppm", QEMU_WORKFLOW)
        self.assertIn("SERVICE_QEMU_AUTOMATE_GUI_LOGIN: '1'", QEMU_WORKFLOW)
        self.assertIn('AUTOMATE_GUI_LOGIN="${SERVICE_QEMU_AUTOMATE_GUI_LOGIN:-0}"', QEMU_SMOKE)
        self.assertIn('qmp-gui-login.py" "$socket" "$phase"', QEMU_SMOKE)
        self.assertIn('TEST_PASSWORD = "gui2026test"', QMP_LOGIN)
        self.assertIn('"execute": "send-key"', QMP_LOGIN)
        self.assertIn('"hold-time": 80', QMP_LOGIN)
        self.assertIn('if sys.argv[2] == "first":', QMP_LOGIN)
        self.assertIn("enter_password(channel)", QMP_LOGIN)
        self.assertIn("framebuffer must be inspected separately", QMP_LOGIN)
        self.assertIn("-display none -monitor none", QEMU_SMOKE)
        self.assertIn('[[ ! -e "$DISK" ]]', QEMU_SMOKE)


    def test_file_manager_escape_clears_search_before_closing_window(self):
        keyboard = FILE_MANAGER.split("public override void HandleKeyboard(KeyEvent key)", 1)[1].split(
            "private void CopySelected(bool cut)", 1
        )[0]
        escape = keyboard.split("if (key.Key == ConsoleKeyEx.Escape)", 2)[-1].split(
            "if (key.Key == ConsoleKeyEx.Backspace)", 1
        )[0]
        self.assertIn("if (contextMenuVisible)", escape)
        self.assertIn("if (searchText.Length > 0)", escape)
        self.assertIn('searchText = "";', escape)
        self.assertIn("ApplySearchFilter();", escape)
        self.assertIn("Close();", escape)
        self.assertLess(escape.index("if (contextMenuVisible)"),
                        escape.index("if (searchText.Length > 0)"))
        self.assertLess(escape.index("if (searchText.Length > 0)"),
                        escape.index("Close();"))

        # First Escape dismisses a context menu; the next clears the
        # filter; only the last Escape closes the window.
        menu, filter_text, closed = True, "report", False
        for expected in ((False, "report", False), (False, "", False), (False, "", True)):
            if menu:
                menu = False
            elif filter_text:
                filter_text = ""
            else:
                closed = True
            self.assertEqual((menu, filter_text, closed), expected)


    def test_file_manager_home_end_select_and_reveal_boundary_tiles(self):
        keyboard = FILE_MANAGER.split("public override void HandleKeyboard(KeyEvent key)", 1)[1].split(
            "private void CopySelected(bool cut)", 1
        )[0]
        boundary = FILE_MANAGER.split("private void SelectBoundary(bool last)", 1)[1].split(
            "private void SelectPage(", 1
        )[0]
        self.assertIn("if (key.Key == ConsoleKeyEx.Home)", keyboard)
        self.assertIn("SelectBoundary(false);", keyboard)
        self.assertIn("if (key.Key == ConsoleKeyEx.End)", keyboard)
        self.assertIn("SelectBoundary(true);", keyboard)
        self.assertIn("if (entries.Count == 0)", boundary)
        self.assertIn("ResetClickSequence();", boundary)
        self.assertIn("contextMenuVisible = false;", boundary)
        self.assertIn("selectedIndex = last ? entries.Count - 1 : 0;", boundary)
        self.assertIn("EnsureSelectionVisible();", boundary)

        for count in (0, 1, 10, 1234):
            for last in (False, True):
                selected = -1 if count == 0 else (count - 1 if last else 0)
                self.assertTrue(selected == -1 or 0 <= selected < count)


    def test_file_manager_modifier_shortcuts_do_not_type_into_names_or_filter(self):
        keyboard = FILE_MANAGER.split("public override void HandleKeyboard(KeyEvent key)", 1)[1].split(
            "private void CopySelected(bool cut)", 1
        )[0]
        dialog = keyboard.split("if (dialogMode != 0)", 1)[1].split(
            "bool control =", 1
        )[0]
        search = keyboard.split("if (key.KeyChar != '\\0' && !char.IsControl(key.KeyChar) &&", 2)[-1]
        guard = "(key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0"
        self.assertIn(guard, dialog)
        self.assertIn(guard, search)
        self.assertLess(dialog.index(guard), dialog.index("dialogName += key.KeyChar;"))
        self.assertLess(search.index(guard), search.index("searchText += key.KeyChar;"))

        # Control combinations are actions, not text. Normal typing,
        # including Shift-modified uppercase characters, must still work.
        for control, alt, shift, expected in (
            (False, False, False, True),
            (False, False, True, True),
            (True, False, False, False),
            (False, True, False, False),
            (True, True, False, False),
        ):
            self.assertEqual(not (control or alt), expected)


    def test_file_manager_live_filter_uses_cached_directory_snapshot(self):
        refresh = FILE_MANAGER.split("private void RefreshInternal(string keep)", 1)[1].split(
            "private void RebuildSidebar()", 1)[0]
        filter_only = refresh.split("private void ApplySearchFilter()", 1)[1]
        self.assertIn("cachedEntries.Clear();", refresh)
        self.assertIn("cachedEntries.AddRange(entries);", refresh)
        self.assertIn("entries.AddRange(cachedEntries);", filter_only)
        self.assertIn("Filter();", filter_only)
        self.assertNotIn("Directory.GetFiles(", filter_only)
        self.assertNotIn("Directory.GetDirectories(", filter_only)
        self.assertNotIn("Sort();", filter_only)
        keyboard = FILE_MANAGER.split("public override void HandleKeyboard(KeyEvent key)", 1)[1].split(
            "private void CopySelected(bool cut)", 1)[0]
        self.assertGreaterEqual(keyboard.count("ApplySearchFilter();"), 3)
        self.assertIn("RefreshAll();", keyboard)  # Explicit F5 still scans disk.
        toolbar = FILE_MANAGER.split("private void Toolbar(int x)", 1)[1].split(
            "private void SidebarClick(", 1)[0]
        self.assertIn("ApplySearchFilter();", toolbar)

        # Independent model: narrowing and then clearing a search must
        # restore the complete, originally sorted snapshot.
        snapshot = ["alpha.txt", "alpine.log", "beta.txt", "readme"]
        for term, expected in (("a", ["alpha.txt", "alpine.log", "beta.txt", "readme"]),
                               ("alp", ["alpha.txt", "alpine.log"]),
                               ("", snapshot)):
            self.assertEqual([name for name in snapshot if term in name], expected)

    def test_file_manager_filter_compacts_in_linear_time(self):
        filter_code = FILE_MANAGER.split("private void Filter()", 1)[1].split(
            "public string CurrentPath", 1)[0]
        self.assertIn("for (int read = 0; read < entries.Count; read++)", filter_code)
        self.assertIn("entries[write++] = entry;", filter_code)
        self.assertIn("entries.RemoveRange(write, entries.Count - write);", filter_code)
        self.assertIn("StringComparison.OrdinalIgnoreCase", filter_code)
        self.assertNotIn("entries.RemoveAt(i)", filter_code)
        self.assertNotIn("ToLowerInvariant()", filter_code)

        # A stable in-place compaction must preserve directory order and
        # match Unicode/case-insensitive queries without mutating the cache.
        original = ["README", "notes.txt", "Readme.md", "data.bin", "READ-ME"]
        for query, expected in (("read", ["README", "Readme.md", "READ-ME"]),
                                ("TXT", ["notes.txt"]),
                                ("absent", []), ("", original)):
            working = list(original)
            write = 0
            for name in working:
                if query.lower() in name.lower():
                    working[write] = name
                    write += 1
            del working[write:]
            self.assertEqual(working, expected)
            self.assertEqual(len(original), 5)

    def test_file_manager_sort_scales_and_preserves_equal_name_order(self):
        sort = FILE_MANAGER.split("private void Sort()", 1)[1].split(
            "private int Compare(", 1)[0]
        compare = FILE_MANAGER.split("private int Compare(", 1)[1].split(
            "private void Filter()", 1)[0]
        self.assertIn("FileEntry[] scratch = new FileEntry[count];", sort)
        self.assertIn("for (int run = 1; run < count;", sort)
        self.assertIn("Compare(entries[left], entries[right]) <= 0", sort)
        self.assertIn("scratch[write] = entries[left++];", sort)
        self.assertIn("scratch[write] = entries[right++];", sort)
        self.assertNotIn("while (j >= 0 && Compare(entries[j], value) > 0)", sort)
        self.assertIn("if (a.IsDirectory != b.IsDirectory)", compare)
        self.assertIn("StringComparison.OrdinalIgnoreCase", compare)

        # Independent bottom-up merge model: directories precede files;
        # case-insensitive ties must retain the input order.
        def sort_model(items):
            data = list(items)
            n = len(data)
            scratch = [None] * n
            run = 1
            while run < n:
                for start in range(0, n, 2 * run):
                    middle = min(n, start + run)
                    end = min(n, middle + run)
                    left, right = start, middle
                    for write in range(start, end):
                        if left < middle and (right >= end or
                                (not data[left][0], data[left][1].lower()) <=
                                (not data[right][0], data[right][1].lower())):
                            scratch[write] = data[left]
                            left += 1
                        else:
                            scratch[write] = data[right]
                            right += 1
                data[:] = scratch
                run = n if run > n // 2 else run * 2
            return data

        for count in (0, 1, 2, 3, 5, 17, 100, 1025):
            items = [(i % 3 == 0, ("Dir" if i % 3 == 0 else "file") +
                      str(i % 17), i) for i in range(count)]
            expected = sorted(items, key=lambda item: (not item[0], item[1].lower()))
            self.assertEqual(sort_model(items), expected)


    def test_desktop_shortcuts_fit_actual_usable_height(self):
        layout = DESKTOP.split("private void InitializeDesktopShortcuts()", 1)[1].split(
            "private void LaunchRegisteredApp(", 1)[0]
        self.assertIn("int usableHeight = System.Math.Max(1, (int)canvas.Height - TaskbarHeight);", layout)
        self.assertIn("const int shortcutHeight = 86;", layout)
        self.assertIn("const int shortcutRowStride = 94;", layout)
        self.assertIn("int rowsPerColumn = System.Math.Max(1,", layout)
        self.assertIn("int column = target / rowsPerColumn;", layout)
        self.assertIn("int row = target % rowsPerColumn;", layout)
        self.assertNotIn("const int rowsPerColumn = 9;", layout)

        for width, height in ((800, 600), (1024, 768), (1366, 768), (1920, 1080)):
            usable_height = height - 44
            rows = max(1, int((usable_height - 22 - 86) / 94) + 1)
            self.assertGreaterEqual(rows, 1)
            # Check the first 16 shortcuts, including multiple columns.
            for index in range(16):
                column, row = divmod(index, rows)
                x, y = 20 + column * 100, 22 + row * 94
                self.assertLessEqual(y + 86, usable_height)
                self.assertLessEqual(x + 92, width)
            self.assertEqual(rows, {600: 5, 768: 7, 1080: 10}[height])


    def test_desktop_shortcut_double_click_uses_elapsed_time_and_proximity(self):
        desktop = DESKTOP.split("private void UpdateDesktopInteractions(", 1)[1].split(
            "private bool IsPointOverWindow(", 1)[0]
        self.assertIn("long now = Stopwatch.GetTimestamp();", desktop)
        self.assertIn("elapsed <= Stopwatch.Frequency / 2", desktop)
        self.assertIn("System.Math.Abs(mouseX - lastShortcutClickX) <= 12", desktop)
        self.assertIn("System.Math.Abs(mouseY - lastShortcutClickY) <= 12", desktop)
        self.assertIn("lastShortcutClickTimestamp = now;", desktop)
        self.assertNotIn("lastShortcutClickFrame", DESKTOP)
        self.assertIn("if (windowCapturedClick)\n                            lastShortcutClickTimestamp = 0;", DESKTOP)
        self.assertIn("lastShortcutClickTimestamp = 0;\n                desktopShortcuts[hitIndex].Open();", desktop)
        for elapsed_ms, dx, dy, expected in (
            (100, 0, 0, True), (499, 12, 12, True),
            (501, 0, 0, False), (100, 13, 0, False),
            (100, 0, -13, False), (2500, 0, 0, False),
        ):
            self.assertEqual(elapsed_ms <= 500 and abs(dx) <= 12 and abs(dy) <= 12,
                             expected)

    def test_titlebar_double_click_requires_nearby_coordinates(self):
        title = WINDOW.split("long now = Stopwatch.GetTimestamp();", 1)[1].split(
            "private int GetResizeMode(", 1)[0]
        self.assertIn("System.Math.Abs(mouseX - lastTitleClickX) <= 12", title)
        self.assertIn("System.Math.Abs(mouseY - lastTitleClickY) <= 12", title)
        self.assertIn("lastTitleClickX = mouseX;", title)
        self.assertIn("lastTitleClickY = mouseY;", title)
        self.assertLess(title.index("lastTitleClickX = mouseX;"),
                        title.index("if (doubleClick)"))
        for dx, dy in ((0, 0), (12, -12), (-12, 12)):
            self.assertTrue(abs(dx) <= 12 and abs(dy) <= 12)
        for dx, dy in ((13, 0), (0, 13), (-20, -20)):
            self.assertFalse(abs(dx) <= 12 and abs(dy) <= 12)


    def test_qmp_file_manager_keyboard_probe_is_opt_in_and_evidence_scoped(self):
        probe = code("tools/qmp-gui-filemanager.py")
        self.assertIn('PROBE_FILE_MANAGER="${SERVICE_QEMU_PROBE_FILE_MANAGER:-0}"', QEMU_SMOKE)
        self.assertIn('[[ "$PROBE_FILE_MANAGER" == 0 || "$AUTOMATE_GUI_LOGIN" == 1 ]]', QEMU_SMOKE)
        self.assertIn('"$elapsed" == 65', QEMU_SMOKE)
        self.assertIn('qmp-gui-filemanager.py" "$socket" "$OUT/$phase"', QEMU_SMOKE)
        self.assertIn("SERVICE_QEMU_PROBE_FILE_MANAGER: '1'", QEMU_WORKFLOW)
        self.assertIn("python3 -m py_compile tools/qmp-gui-login.py tools/qmp-gui-filemanager.py", QEMU_WORKFLOW)
        self.assertIn('send_keys(channel, "alt", "spc")', probe)
        self.assertIn('for key in "file":', probe)
        self.assertIn('send_keys(channel, "ret")', probe)
        self.assertIn('"screendump"', probe)
        self.assertIn('if fraction < 0.12:', probe)
        self.assertIn("visual app identity requires inspection", probe)
        self.assertIn("first_filemanager_launches=", QEMU_SMOKE)
        self.assertIn("second_filemanager_launches=", QEMU_SMOKE)
        self.assertIn("'[PALETTE] Launched application: File Manager'", QEMU_SMOKE)
        self.assertIn("second_filemanager_launches > first_filemanager_launches", QEMU_SMOKE)
        self.assertIn("Never connect this probe to a guest with host disks", probe)


    def test_window_keyboard_shortcuts_cancel_pending_desktop_double_click(self):
        shortcuts = DESKTOP.split("private bool HandleWindowShortcut(KeyEvent key)", 1)[1].split(
            "private void LogoutSession()", 1)[0]
        self.assertIn("if (key.Key == ConsoleKeyEx.Tab)", shortcuts)
        self.assertIn("if (key.Key != ConsoleKeyEx.F4)", shortcuts)
        self.assertEqual(shortcuts.count("lastShortcutClickTimestamp = 0;"), 2)
        self.assertLess(shortcuts.index("lastShortcutClickTimestamp = 0;"),
                        shortcuts.index("applicationManager?.CycleWindow(reverse);"))
        self.assertLess(shortcuts.rindex("lastShortcutClickTimestamp = 0;"),
                        shortcuts.index("applicationManager?.RequestCloseActiveWindow();"))



class CommandPaletteResponsiveTests(unittest.TestCase):
    def test_palette_stays_inside_actual_desktop(self):
        self.assertIn("Width = System.Math.Min(Width, System.Math.Max(1, screenWidth - 16));",
                      COMMAND_PALETTE)
        self.assertIn("Height = System.Math.Min(Height, System.Math.Max(1, screenHeight - 16));",
                      COMMAND_PALETTE)
        self.assertIn("X = System.Math.Max(0, (screenWidth - Width) / 2);",
                      COMMAND_PALETTE)
        self.assertIn("screenHeight - Height));", COMMAND_PALETTE)
        for width, height in ((320, 196), (640, 436), (800, 556),
                              (1024, 724), (1366, 724), (1920, 1036)):
            palette_width = min(680, max(1, width - 16))
            palette_height = min(448, max(1, height - 16))
            x = max(0, (width - palette_width) // 2)
            y = max(0, min(max(18, (height - palette_height) // 4),
                           height - palette_height))
            self.assertGreaterEqual(x, 0)
            self.assertGreaterEqual(y, 0)
            self.assertLessEqual(x + palette_width, width)
            self.assertLessEqual(y + palette_height, height)

    def test_palette_rows_share_render_and_mouse_geometry(self):
        self.assertIn("listY + visibleRows * resultRowHeight", COMMAND_PALETTE)
        self.assertIn("int row = (mouseY - listY) / resultRowHeight;", COMMAND_PALETTE)
        self.assertIn("for (int row = 0; row < visibleRows; row++)", COMMAND_PALETTE)
        self.assertIn("int y = listY + row * resultRowHeight;", COMMAND_PALETTE)
        self.assertIn("selectedIndex - visibleRows + 1", COMMAND_PALETTE)
        self.assertIn("resultCount - visibleRows", COMMAND_PALETTE)
        for height in (180, 420, 448, 720):
            list_space = max(1, height - (52 + 44 + 20) - 34)
            rows = max(1, min(6, list_space // 48))
            row_height = min(48, max(1, list_space // rows))
            self.assertGreaterEqual(rows, 1)
            self.assertLessEqual(rows, 6)
            self.assertLessEqual(116 + rows * row_height, height - 34)

    def test_relaunch_same_app_does_not_duplicate_window_or_taskbar_slot(self):
        launch = APPLICATION_MANAGER.split("public void Launch(", 1)[1].split(
            "public void Activate(", 1)[0]
        self.assertIn("if (!applications.Contains(application))", launch)
        self.assertIn("applications.Add(application);", launch)
        self.assertLess(launch.index("if (!applications.Contains(application))"),
                        launch.index("applications.Add(application);"))
        self.assertLess(launch.index("applications.Add(application);"),
                        launch.index("Activate(application);"))

        # Repeatedly launching the same instance changes MRU position, not
        # the number of windows rendered or counted by taskbar pagination.
        apps = []
        for app in ("files", "files", "terminal", "files", "terminal"):
            if app not in apps:
                apps.append(app)
            apps.remove(app)
            apps.append(app)
        self.assertEqual(len(apps), 2)
        self.assertEqual(apps, ["files", "terminal"])

    def test_palette_keyboard_page_navigation_clamps_to_result_count(self):
        for key in ("Home", "End", "PageUp", "PageDown"):
            self.assertIn("ConsoleKeyEx." + key, COMMAND_PALETTE)
        self.assertIn("selectedIndex - visibleRows", COMMAND_PALETTE)
        self.assertIn("selectedIndex + visibleRows", COMMAND_PALETTE)
        self.assertIn("System.Math.Max(0, resultCount - 1)", COMMAND_PALETTE)
        for count, rows in ((0, 1), (1, 1), (5, 5), (12, 5), (40, 6)):
            for start in range(max(1, count)):
                page_up = max(0, start - rows)
                page_down = min(max(0, count - 1), start + rows)
                self.assertGreaterEqual(page_up, 0)
                self.assertLessEqual(page_up, max(0, count - 1))
                self.assertGreaterEqual(page_down, 0)
                self.assertLessEqual(page_down, max(0, count - 1))

    def test_focus_loss_cancels_drag_resize_and_hover_state(self):
        setter = WINDOW.split("public bool IsActive", 1)[1].split(
            "public System.Action CloseAction", 1)[0]
        self.assertIn("if (!value)", setter)
        for reset in ("dragging = false;", "resizing = false;",
                      "resizeMode = 0;", "hoveredResizeMode = 0;",
                      "hoveredControl = 0;", "lastTitleClickTimestamp = 0;"):
            self.assertIn(reset, setter)
        self.assertLess(setter.index("if (!value)"),
                        setter.index("dragging = false;"))
        self.assertLess(setter.index("resizing = false;"),
                        setter.index("UiAnimator.RequestFrames();"))

    def test_palette_compacts_details_in_narrow_mode(self):
        self.assertIn("if (resultRowHeight >= 42 && Width >= 540)", COMMAND_PALETTE)
        self.assertIn("if (Width >= 620)", COMMAND_PALETTE)
        self.assertIn('"UP/DOWN  ENTER  ESC"', COMMAND_PALETTE)
        self.assertIn("System.Math.Max(1, resultRowHeight - 14)", COMMAND_PALETTE)


    def test_file_manager_filter_retains_selection_and_enter_opens_first_match(self):
        section = FILE_MANAGER.split("private void ApplySearchFilter()", 1)[1].split(
            "private void RebuildSidebar()", 1)[0]
        self.assertIn("FileEntry previousSelection =", section)
        self.assertIn("entries.IndexOf(previousSelection)", section)
        self.assertIn("searchText.Length > 0 && entries.Count > 0", section)
        self.assertIn("selectedIndex = 0;", section)
        self.assertIn("EnsureSelectionVisible();", section)
        self.assertNotIn("Directory.GetFiles", section)
        self.assertNotIn("Directory.GetDirectories", section)
        keyboard = FILE_MANAGER.split("public override void HandleKeyboard(", 1)[1].split(
            "private void CopySelected(", 1)[0]
        self.assertIn("if (selectedIndex >= 0)\\n                    OpenEntry(selectedIndex);".replace("\\n", "\n"), keyboard)

        def select_after_filter(names, previous, query):
            visible = [name for name in names if query.lower() in name.lower()]
            if previous in visible:
                return visible, visible.index(previous)
            return visible, 0 if query and visible else -1

        names = ["Alpha.txt", "Beta.txt", "Gamma.log", "README.md"]
        self.assertEqual(select_after_filter(names, "Gamma.log", "log"),
                         (["Gamma.log"], 0))
        self.assertEqual(select_after_filter(names, "Beta.txt", "a"),
                         (["Alpha.txt", "Beta.txt", "Gamma.log", "README.md"], 1))
        self.assertEqual(select_after_filter(names, "Beta.txt", "read"),
                         (["README.md"], 0))
        self.assertEqual(select_after_filter(names, None, "zzzz"), ([], -1))
        self.assertEqual(select_after_filter(names, None, ""), (names, -1))

    def test_sidebar_context_click_cannot_mount_or_unmount_volume(self):
        right = FILE_MANAGER.split("private void SidebarRightClick(", 1)[1].split(
            "private SidebarEntry GetSidebarEntryAt(", 1)[0]
        self.assertIn("status = entry.IsMounted", right)
        self.assertNotIn("UnmountVolume(", right)
        self.assertNotIn("MountVolume(", right)
        self.assertNotIn("TryUnmount(", right)
        left = FILE_MANAGER.split("private void SidebarClick(", 1)[1].split(
            "private void SidebarRightClick(", 1)[0]
        self.assertIn("UnmountVolume(entry);", left)
        self.assertIn("MountVolume(entry, false);", left)


    def test_file_manager_context_menu_keyboard_navigation_and_disabled_actions(self):
        keyboard = FILE_MANAGER.split("public override void HandleKeyboard(", 1)[1].split(
            "private void CopySelected(", 1)[0]
        actions = FILE_MANAGER.split("private bool ContextActionEnabled(", 1)[1].split(
            "private void Toolbar(", 1)[0]
        render = FILE_MANAGER.split("private void Menu(Canvas canvas)", 1)[1].split(
            "private void Dialog(Canvas canvas)", 1)[0]
        self.assertIn("contextMenuSelection = -1;", FILE_MANAGER)
        self.assertIn("ConsoleKeyEx.UpArrow || key.Key == ConsoleKeyEx.DownArrow", keyboard)
        self.assertIn("if (ContextActionEnabled(candidate))", keyboard)
        self.assertIn("ExecuteContextAction(contextMenuSelection);", keyboard)
        self.assertIn("contextMenuSelection = -1;", keyboard)
        self.assertIn("return FileClipboard.HasItem;", actions)
        self.assertIn("selectedIndex >= 0 && selectedIndex < entries.Count", actions)
        self.assertIn("if (!ContextActionEnabled(item))", actions)
        self.assertIn("app.ContextMenuSelection == index", render)

        def next_enabled(current, direction, enabled):
            count = len(enabled)
            candidate = current
            for _ in range(count):
                candidate = (0 if direction > 0 else count - 1) if candidate < 0 else (
                    candidate + direction + count) % count
                if enabled[candidate]:
                    return candidate
            return current

        # Without a selected file and clipboard, Copy/Cut/Paste cannot run.
        enabled = [False, False, False, True, True, True, True]
        self.assertEqual(next_enabled(-1, 1, enabled), 3)
        self.assertEqual(next_enabled(-1, -1, enabled), 6)
        self.assertEqual(next_enabled(6, 1, enabled), 3)
        self.assertEqual(next_enabled(3, -1, enabled), 6)
        self.assertEqual(next_enabled(3, 1, enabled), 4)
        self.assertEqual(next_enabled(0, 1, [True] * 7), 1)


    def test_file_manager_volume_mount_hitbox_matches_painted_button(self):
        click = FILE_MANAGER.split("private void SidebarClick(", 1)[1].split(
            "private void SidebarRightClick(", 1)[0]
        render = FILE_MANAGER.split("private void RenderSidebar(", 1)[1].split(
            "private bool IsEntryActive(", 1)[0]
        self.assertIn("IsVolumeActionHit(entry, x, y)", click)
        self.assertIn("const int actionLeft = 4 + SidebarWidth - 27;", click)
        self.assertIn("x >= actionLeft + 20", click)
        self.assertIn("y >= rowY + 7 && y < rowY + 29", click)
        self.assertIn("rowY + rowHeight > sidebarBottom", click)
        self.assertIn('status = "Use the M button to mount " + entry.Label;', click)
        self.assertNotIn("MountVolume(entry, true);", click)
        self.assertIn("int actionX = x + width - 27;", render)
        self.assertIn("itemY + 7, 20, 22", render)

        # View-relative hitbox, independent of screen resolution.
        action_left = 4 + 176 - 27
        for row_y in (50 + 8, 50 + 8 + 22 + 36, 50 + 8 + 22 + 72):
            def hit(x, y):
                return action_left <= x < action_left + 20 and row_y + 7 <= y < row_y + 29
            self.assertFalse(hit(action_left - 1, row_y + 15))
            self.assertTrue(hit(action_left, row_y + 7))
            self.assertTrue(hit(action_left + 19, row_y + 28))
            self.assertFalse(hit(action_left + 20, row_y + 15))
            self.assertFalse(hit(action_left + 10, row_y + 6))
            self.assertFalse(hit(action_left + 10, row_y + 29))


    def test_notification_shortcuts_ignore_ctrl_alt_modifiers(self):
        keyboard = NOTIFICATIONS.split("public bool HandleKeyboard(KeyEvent key)", 1)[1].split(
            "public bool UpdateInteractions(", 1)[0]
        self.assertIn("bool control = (key.Modifiers & ConsoleModifiers.Control)", keyboard)
        self.assertIn("bool alt = (key.Modifiers & ConsoleModifiers.Alt)", keyboard)
        self.assertIn("if (control || alt)", keyboard)
        self.assertLess(keyboard.index("if (control || alt)"),
                        keyboard.index("NotificationService.ClearAll();"))
        self.assertLess(keyboard.index("if (control || alt)"),
                        keyboard.index("NotificationService.MarkAllRead();"))
        self.assertLess(keyboard.index("if (control || alt)"),
                        keyboard.index("NotificationService.DoNotDisturb ="))
        for control, alt, ch in ((True, False, "c"), (False, True, "r"),
                                 (True, True, "d"), (False, False, "c")):
            allowed = not (control or alt) and ch in "dDrRcC"
            self.assertEqual(allowed, not (control or alt))

    def test_notification_toast_uses_normal_panel_open_path(self):
        mouse = NOTIFICATIONS.split("public bool UpdateInteractions(", 1)[1].split(
            "public override void Render(", 1)[0]
        self.assertIn("NotificationService.MarkRead(toast.Id);", mouse)
        self.assertIn("Toggle();", mouse)
        self.assertNotIn("Visible = true;", mouse)
        self.assertLess(mouse.index("NotificationService.MarkRead(toast.Id);"),
                        mouse.index("Toggle();"))


    def test_notification_panel_and_toast_fit_small_and_full_hd_modes(self):
        self.assertIn("private static int PanelWidth(int screenWidth)", NOTIFICATIONS)
        self.assertIn("private int ToastWidth", NOTIFICATIONS)
        self.assertIn("private int ToastX", NOTIFICATIONS)
        self.assertIn("private int ToastY", NOTIFICATIONS)
        self.assertIn("ToastX, ToastY, ToastWidth, 98", NOTIFICATIONS)
        self.assertIn("int width = ToastWidth;", NOTIFICATIONS)

        for width, height in ((320, 240), (360, 480), (400, 600),
                              (640, 480), (800, 600), (1024, 768),
                              (1366, 768), (1920, 1080)):
            panel_width = min(462, max(1, width - 20))
            panel_x = max(0, width - panel_width - 10)
            toast_width = min(414, max(1, width - 24))
            toast_x = max(0, width - toast_width - 18)
            toast_y = max(0, height - 44 - 116)
            self.assertGreaterEqual(panel_x, 0)
            self.assertLessEqual(panel_x + panel_width, width)
            self.assertGreaterEqual(toast_x, 0)
            self.assertLessEqual(toast_x + toast_width, width)
            self.assertGreaterEqual(toast_y, 0)
            self.assertLessEqual(toast_y + 98, height - 44)

    def test_notification_footer_render_and_hit_test_share_narrow_bounds(self):
        self.assertIn("private void FooterButtonBounds(int index, out int x, out int width)", NOTIFICATIONS)
        render = NOTIFICATIONS.split("private void RenderFooter(", 1)[1].split(
            "private void DrawFooterButton(", 1)[0]
        hover = NOTIFICATIONS.split("private void UpdateHover(", 1)[1].split(
            "private void EnsureFresh(", 1)[0]
        self.assertEqual(render.count("FooterButtonBounds("), 4)
        self.assertEqual(hover.count("FooterButtonBounds("), 1)
        self.assertIn("Hit(mouseX, mouseY, buttonX, footerY, buttonWidth, 38)", hover)
        self.assertIn('Width < 380 && i == 1 ? "NOWE"', NOTIFICATIONS)
        for width in (300, 340, 380, 420, 462):
            gap = 4
            slot = max(1, (width - 28 - 2 * gap) // 3)
            bounds = []
            for index in range(3):
                if width >= 440:
                    x = (14, 147, 258)[index]
                    button_width = (126, 104, width - 272)[index]
                else:
                    x = 14 + index * (slot + gap)
                    button_width = max(1, width - 14 - x) if index == 2 else slot
                bounds.append((x, x + button_width))
            self.assertGreaterEqual(bounds[0][0], 0)
            self.assertLessEqual(bounds[-1][1], width)
            for left, right in bounds:
                self.assertGreater(right, left)
            self.assertLessEqual(bounds[0][1], bounds[1][0])
            self.assertLessEqual(bounds[1][1], bounds[2][0])


if __name__ == "__main__":
    unittest.main(verbosity=2)
