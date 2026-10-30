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
        self.assertIn("if (!desktopMenuCaptures)\n                            taskbar.UpdateInteractions(", dispatch)
        self.assertIn("if (!startMenuCaptures && !desktopMenuCaptures && !windowCapturedClick)\n                            UpdateDesktopInteractions(", dispatch)
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
        for screen_width in (800, 1366, 1920):
            max_app_x = screen_width - 278
            slots = max(0, (max_app_x - 60 + 5) // 45)
            for running_count in (0, 1, 9, 10, 11, 23, 50):
                overflow = running_count > slots and slots >= 2
                page_slots = slots - 1 if overflow else slots
                self.assertGreaterEqual(page_slots, 0)
                if overflow:
                    arrow_x = 60 + page_slots * 45
                    self.assertLessEqual(arrow_x + 40, max_app_x)
                    offsets = list(range(0, running_count, page_slots))
                    self.assertEqual(
                        [index for offset in offsets
                         for index in range(offset, min(running_count, offset + page_slots))],
                        list(range(running_count))
                    )
                    self.assertEqual(offsets[-1] + page_slots >= running_count, True)
                else:
                    self.assertLessEqual(running_count, page_slots)

    def test_taskbar_focus_switch_reveals_active_page_without_resetting_manual_page(self):
        render = TASKBAR.split("public override void Render(Canvas canvas)", 1)[1].split(
            "private void UpdateTrayCache()", 1
        )[0]
        self.assertIn("int activeRunningIndex = RunningIndexOf(apps, activeApplication);", render)
        self.assertIn("activeApplication != lastActiveApplication ||", render)
        self.assertIn("activeRunningIndex != lastActiveRunningIndex", render)
        self.assertIn("firstVisibleAppIndex = (activeRunningIndex / pageSlots) * pageSlots;", render)
        self.assertLess(render.index("NormalizeAppPage(runningCount, pageSlots, overflow);"),
                        render.index("int activeRunningIndex = RunningIndexOf("))
        self.assertLess(render.index("firstVisibleAppIndex = (activeRunningIndex / pageSlots)"),
                        render.index("if (apps != null)"))

        # A focus change can jump directly to the last page, but clicking
        # the pager alone must not change the focus signature.
        for page_slots, active_index in ((9, 0), (9, 10), (9, 49), (21, 23)):
            focus_page = (active_index // page_slots) * page_slots
            self.assertLessEqual(focus_page, active_index)
            self.assertLess(active_index, focus_page + page_slots)
            manual_page = focus_page + page_slots
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
        self.assertRegex(dispatch, r"if \(!rightPressed\)\s*startMenu\.UpdateInteractions\(")
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
            "if (!startMenuCaptures && !desktopMenuCaptures && !windowCapturedClick)",
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

    def test_file_manager_sidebar_hit_testing_excludes_clipped_volume_rows(self):
        sidebar = FILE_MANAGER.split("private SidebarEntry GetSidebarEntryAt(int y)", 1)[1].split(
            "private void MountVolume(", 1)[0]
        render = FILE_MANAGER.split("private void RenderSidebar(Canvas canvas", 1)[1].split(
            "private bool IsEntryActive(", 1)[0]
        self.assertIn("int sidebarBottom = Math.Max(ContentTop + 40,", sidebar)
        self.assertIn("view.Height - FooterHeight - 8);", sidebar)
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


if __name__ == "__main__":
    unittest.main(verbosity=2)
