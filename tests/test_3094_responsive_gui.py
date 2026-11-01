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
START_MENU = code("source/GUI/StartMenu.cs")


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
    def test_file_manager_initial_client_matches_constrained_window_before_first_frame(self):
        constructor = FILE_MANAGER.split(
            "public FileManagerApp(int x, int y, Action<string> openFile)", 1
        )[1].split("public bool DismissTransientUi()", 1)[0]
        update = FILE_MANAGER.split("public override void Update()", 1)[1].split(
            "public override void HandleMouse(", 1
        )[0]
        self.assertIn("Math.Max(1, Window.Width - 20)", constructor)
        self.assertIn("Math.Max(1, Window.Height - 50)", constructor)
        self.assertIn("view.Width = Math.Max(1, Window.Width - 20);", update)
        self.assertIn("view.Height = Math.Max(1, Window.Height - 50);", update)
        for screen_width, screen_height in (
            (200, 200), (240, 240), (320, 240), (640, 480),
            (800, 600), (1366, 768), (1920, 1080),
        ):
            x, y, width, height = fit_window(
                100, 100, 820, 520, screen_width, screen_height)
            view_width = max(1, width - 20)
            view_height = max(1, height - 50)
            self.assertLessEqual(view_width + 20, width)
            self.assertLessEqual(view_height + 50, height)

    def test_file_manager_small_client_preserves_full_tile_and_mount_hitbox(self):
        self.assertIn("internal int SidebarWidthForView => view.Width < 220", FILE_MANAGER)
        self.assertIn("view.Width - TileWidth - 12", FILE_MANAGER)
        self.assertIn("view.Width - SidebarWidthForView - 12", FILE_MANAGER)
        self.assertIn("if (SidebarWidthForView <= 0)", FILE_MANAGER)
        self.assertIn("if (sidebarW > 0 && contentH > 0)", FILE_MANAGER)
        self.assertIn("int sidebarW = app.SidebarWidthForView;", FILE_MANAGER)
        self.assertIn("int gridX = X + sidebarW + 8;", FILE_MANAGER)
        self.assertIn("if (Width >= 308 && Height >= 44)", FILE_MANAGER)
        self.assertIn("if (width >= 140)", FILE_MANAGER)

        # Same view-relative widths drive painting, hit testing, tile
        # columns and the volume M/U button. No host storage is accessed.
        for view_width in (180, 200, 219, 220, 239, 260, 299, 300, 400, 800):
            sidebar = 0 if view_width < 220 else min(176, view_width - 124)
            grid_left = sidebar + 8
            grid_width = max(112, view_width - sidebar - 12)
            self.assertGreaterEqual(grid_width, 112)
            self.assertLessEqual(grid_left + 112, view_width - 4)
            self.assertLessEqual(grid_left + grid_width, view_width - 4)
            if sidebar:
                action_left = 4 + sidebar - 27
                self.assertGreaterEqual(action_left, 4)
                self.assertLessEqual(action_left + 20, 4 + sidebar)
                self.assertLess(action_left + 20, grid_left)
            if view_width >= 300:
                path_width = max(120, view_width - 188)
                self.assertLessEqual(180 + path_width, view_width)

    def test_taskbar_storage_poll_is_bounded_and_failed_reads_keep_last_valid_state(self):
        # Source integration contract: the monotonic gate runs before storage
        # enumeration; a transient exception must not masquerade as VOL 0.
        self.assertIn("TaskbarTelemetryGate.ShouldPoll(", TASKBAR)
        self.assertIn("lastStoragePollTicks = nowTicks;", TASKBAR)
        storage = TASKBAR.split("long nowTicks = Stopwatch.GetTimestamp();", 1)[1].split(
            "if (volumeCount != cachedVolumeCount)", 1)[0]
        self.assertLess(storage.index("TaskbarTelemetryGate.ShouldPoll("),
                        storage.index("StorageManager.Partitions.Count"))
        self.assertIn("catch\n            {\n                // Retry on the next polling interval, not on every frame.\n                return;\n            }", storage)
        self.assertNotIn("volumeCount = 0;", storage)
        self.assertIn("if (volumeStateInitialized)", TASKBAR)

    def test_ultranarrow_taskbar_keeps_switcher_visible_without_invisible_tray_hits(self):
        # Geometry-only regression for widths below the QEMU 240px probe.
        # At these widths the old fixed 132px tray hid every app button.
        self.assertIn("if (Width < 178)\n                return 0;", TASKBAR.replace("\r", ""))
        self.assertIn("if (Width < 240)", TASKBAR)
        self.assertIn("if (Width >= 240)", TASKBAR)
        self.assertIn("if (Width < 178)\n                return;", TASKBAR.replace("\r", ""))
        self.assertIn("notificationClick?.Invoke();", TASKBAR)
        self.assertIn("applicationManager.CycleWindow(false);", TASKBAR)

        for width in range(100, 241):
            if width < 178:
                tray_width = 0
            elif width < 240:
                tray_width = min(132, max(0, width - 100))
            else:
                tray_width = 132
            tray_left = width - tray_width
            slots = max(0, (tray_left - 60 + 5) // 45)
            self.assertGreaterEqual(slots, 1, width)
            for count in (0, 1, 2, 3, 10, 50):
                overflow = count > slots
                page_slots = slots - 1 if overflow else slots
                if overflow:
                    self.assertLessEqual(60 + page_slots * 45 + 40, tray_left,
                                         (width, count))
                elif count:
                    self.assertLessEqual(60 + (count - 1) * 45 + 40,
                                         tray_left, (width, count))
            if 178 <= width < 240:
                # Clock fits entirely after the 40px switcher; notification
                # is intentionally hidden rather than painted over the clock.
                self.assertGreaterEqual(width - 78, tray_left)
                self.assertLessEqual(width - 78 + 68, width)

    def test_gui_keyboard_dispatch_has_finite_per_frame_budget(self):
        # A continuous stream of key-repeat events must not prevent the
        # mouse, lock timer and application updates from running. This is a
        # source contract, not a keyboard hardware or QEMU performance test.
        self.assertIn("private const int MaxKeyboardEventsPerFrame = 64;", DESKTOP)
        run_loop = DESKTOP.split("while (isRunning)", 1)[1].split(
            "Thread.Sleep(15);", 1)[0]
        self.assertIn("for (int processedKeys = 0;", run_loop)
        self.assertIn("processedKeys < MaxKeyboardEventsPerFrame &&", run_loop)
        self.assertIn("KeyboardManager.TryReadKey(out KeyEvent? key);", run_loop)
        self.assertIn("processedKeys++)", run_loop)
        self.assertNotIn("while (KeyboardManager.TryReadKey(", run_loop)
        self.assertLess(run_loop.index("processedKeys++)"),
                        run_loop.index("applicationManager.Update();"))
        self.assertLess(run_loop.index("applicationManager.Update();"),
                        run_loop.index("RenderFrame(mouseX, mouseY);"))

        # Model a saturated FIFO: each pass consumes at most 64 entries,
        # preserves their order, and leaves the rest for the next frame.
        queue = list(range(193))
        consumed = []
        iterations = 0
        while queue:
            batch = queue[:64]
            del queue[:64]
            consumed.extend(batch)
            self.assertLessEqual(len(batch), 64)
            iterations += 1
        self.assertEqual(iterations, 4)
        self.assertEqual(consumed, list(range(193)))

    def test_start_and_palette_do_not_run_modified_search_or_launch_actions(self):
        for name, source in (("Start", START_MENU), ("Palette", COMMAND_PALETTE)):
            keyboard = source.split("public bool HandleKeyboard(KeyEvent key)", 1)[1].split(
                "public void UpdateInteractions(" if name == "Start" else "public bool HandleMouse(", 1)[0]
            guard = "(key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) != 0"
            self.assertIn(guard, keyboard, name)
            self.assertIn("return true;", keyboard.split(guard, 1)[1][:80], name)
            self.assertLess(keyboard.index(guard), keyboard.index("ConsoleKeyEx.Backspace"), name)
            self.assertLess(keyboard.index(guard), keyboard.index("ConsoleKeyEx.Enter"), name)
            self.assertLess(keyboard.index(guard), keyboard.index("char ch = key.KeyChar"), name)

    def test_resize_edges_preserve_work_area_insets(self):
        resize = WINDOW.split("private void ApplyResize(", 1)[1].split(
            "public override void Render(", 1)[0]
        self.assertIn("desktopWidth - margin - resizeStartX", resize)
        self.assertIn("desktopHeight - margin - resizeStartY", resize)
        self.assertIn("System.Math.Min(desktopWidth - margin,", resize)
        self.assertIn("System.Math.Min(desktopHeight - margin,", resize)
        self.assertIn("System.Math.Max(margin, System.Math.Min(resizeStartX + dx", resize)
        self.assertIn("System.Math.Max(margin, System.Math.Min(resizeStartY + dy", resize)
        for screen_w, screen_h in (
            (800, 600), (1024, 768), (1366, 768), (1920, 1080),
        ):
            desktop_h = screen_h - 44
            margin = 12 if screen_w >= 1000 and desktop_h >= 700 else 0
            x, y, width, height = fit_window(
                120, 78, 820, 520, screen_w, screen_h)
            for delta in (-10000, -64, 0, 64, 10000):
                right = min(screen_w - margin, x + width)
                left = max(margin, min(x + delta,
                                       right - min(320, max(1, right - margin))))
                left_width = right - left
                self.assertGreaterEqual(left, margin)
                self.assertLessEqual(left + left_width, screen_w - margin)
                right_width = max(min(320, max(1, screen_w - margin - x)),
                                  min(width + delta,
                                      max(1, screen_w - margin - x)))
                self.assertLessEqual(x + right_width, screen_w - margin)
                bottom = min(desktop_h - margin, y + height)
                top = max(margin, min(y + delta,
                                      bottom - min(220, max(1, bottom - margin))))
                self.assertGreaterEqual(top, margin)
                self.assertLessEqual(bottom, desktop_h - margin)
                self.assertGreaterEqual(bottom - top, min(220, max(1, bottom - margin)))
                bottom_height = max(min(220, max(1, desktop_h - margin - y)),
                                    min(height + delta,
                                        max(1, desktop_h - margin - y)))
                self.assertLessEqual(y + bottom_height, desktop_h - margin)

    def test_new_mouse_press_outside_windows_never_reaches_active_app(self):
        dispatch = APPLICATION_MANAGER.split(
            "public void HandleMouse(int mouseX, int mouseY, bool leftClicked,", 1
        )[1].split("public void Update()", 1)[0]
        self.assertIn("bool newPress = (leftClicked && !leftWasClicked) ||", dispatch)
        self.assertIn("(rightClicked && !rightWasClicked);", dispatch)
        self.assertIn("if (newPress)\n                target = null;", dispatch)
        self.assertIn("if (newPress)\n            {", dispatch)
        self.assertIn("target?.HandleMouse(mouseX, mouseY", dispatch)
        self.assertLess(dispatch.index("target = null;"),
                        dispatch.index("for (int i = applications.Count - 1;"))
        # A new desktop/taskbar press has no window target, but an
        # ongoing drag still delivers motion and button release.
        for left, old_left, right, old_right in (
            (True, False, False, False), (False, False, True, False),
            (True, True, False, False), (False, True, False, False),
        ):
            new_press = (left and not old_left) or (right and not old_right)
            has_window_under_pointer = False
            target_active = not new_press or has_window_under_pointer
            self.assertEqual(target_active, not new_press)

    def test_drag_respects_same_work_area_inset_as_restore(self):
        drag = WINDOW.split("if (dragging)", 1)[1].split(
            "if (wasClicked)", 1)[0]
        self.assertIn(
            "int margin = desktopWidth >= 1000 && desktopHeight >= 700 ? 12 : 0;",
            drag)
        self.assertIn("desktopWidth - Width - margin", drag)
        self.assertIn("desktopHeight - Height - margin", drag)
        self.assertIn("System.Math.Max(margin, System.Math.Min(newX, maxX))", drag)
        self.assertIn("System.Math.Max(margin, System.Math.Min(newY, maxY))", drag)
        for screen_width, screen_height in (
            (320, 240), (800, 600), (1024, 768),
            (1366, 768), (1920, 1080),
        ):
            desktop_height = screen_height - 44
            margin = 12 if screen_width >= 1000 and desktop_height >= 700 else 0
            _, _, width, height = fit_window(
                120, 78, 820, 520, screen_width, screen_height)
            for desired_x, desired_y in (
                (-10000, -10000), (0, 0), (99999, 99999),
            ):
                x = max(margin, min(desired_x,
                                    max(margin, screen_width - width - margin)))
                y = max(margin, min(desired_y,
                                    max(margin, desktop_height - height - margin)))
                self.assertGreaterEqual(x, margin)
                self.assertGreaterEqual(y, margin)
                self.assertLessEqual(x + width, screen_width - margin)
                self.assertLessEqual(y + height, desktop_height - margin)

    def test_maximized_caption_drag_restores_without_pointer_jump(self):
        drag = WINDOW.split("if (dragging)", 1)[1].split(
            "if (wasClicked)", 1)[0]
        self.assertIn("if (IsMaximized)", drag)
        self.assertIn("System.Math.Abs(movedX) <= 6", drag)
        self.assertIn("System.Math.Abs(movedY) <= 6", drag)
        self.assertIn("ToggleMaximize();", drag)
        self.assertIn("(long)pressedOffsetX * Width", drag)
        self.assertIn("dragOffsetY = System.Math.Max(0,", drag)
        self.assertIn("dragging = true;", drag)
        self.assertIn("lastTitleClickTimestamp = 0;", drag)
        # The initial press must arm a maximized drag, but an unmoved
        # click must still be available for the existing double-click.
        caption = WINDOW.split("bool inTitle =", 1)[1].split(
            "private int GetResizeMode", 1)[0]
        self.assertIn("dragging = true;", caption)
        self.assertNotIn("if (!IsMaximized)\n            {\n                dragging = true;", caption)
        self.assertLess(drag.index("if (IsMaximized)"), drag.index("if (!IsMaximized)"))

        for screen_width, screen_height, restored_width, press_x in (
            (640, 480, 640, 500),
            (800, 600, 640, 600),
            (1366, 768, 760, 1120),
            (1920, 1080, 950, 1680),
        ):
            desktop_height = screen_height - 44
            margin = 12 if screen_width >= 1000 and desktop_height >= 700 else 0
            for move_x, move_y in ((0, 0), (6, 6), (20, 10), (-100, 40)):
                should_restore = abs(move_x) > 6 or abs(move_y) > 6
                if not should_restore:
                    continue
                offset = max(0, min(restored_width - 1,
                                    press_x * restored_width // screen_width))
                new_x = max(margin, min(press_x + move_x - offset,
                                        max(margin, screen_width - restored_width - margin)))
                self.assertGreaterEqual(new_x, margin)
                self.assertLessEqual(new_x + restored_width, screen_width - margin)
                self.assertGreaterEqual(offset, 0)
                self.assertLess(offset, restored_width)
                self.assertLessEqual(abs(offset / restored_width - press_x / screen_width),
                                     1 / restored_width + 0.001)

    def test_sdk_version_still_pinned(self):
        self.assertIn('Sdk="Cosmos.Sdk/3.0.95"', PROJECT)
        self.assertIn('Version="3.0.95"', PROJECT)
        self.assertIn('"3.0.95", Good', SETTINGS)
        self.assertNotIn('"3.0.94", Good', SETTINGS)

    def test_task_manager_compact_refresh_button_has_no_negative_label_width(self):
        source = TASK_MANAGER.split("private void DrawButton(Canvas canvas", 1)[1].split(
            "private void RenderProcessPage(Canvas canvas)", 1)[0]
        self.assertIn("if (width < 40)", source)
        self.assertIn("x + (width - 16) / 2", source)
        self.assertLess(source.index("if (width < 40)"),
                        source.index("width - 34"))
        self.assertEqual((32 - 16) // 2, 8)

    def test_task_manager_short_window_nav_and_compact_process_list(self):
        manager = TASK_MANAGER
        self.assertIn("private int ListTopForView => CompactProcessLayout ? 64 : ListTop;", manager)
        self.assertIn("int listTop = ListTopForView;", manager)
        self.assertIn("int listY = Y + ListTopForView;", manager)
        self.assertIn("scrollBar.Y = Y + ListTopForView + 2;", manager)
        self.assertIn("if (CompactProcessLayout)\n            {\n                // Summary cards", manager)
        self.assertIn("if (Height < 300)", manager)
        self.assertIn("int compactX = X + 6 + (page % 2) * 80;", manager)
        self.assertIn("int compactY = Y + NavTop + (page / 2) * 36;", manager)
        self.assertIn("return x >= Width - 42 && x < Width - 10 ? 1 : 0;", manager)
        for height in (146, 220, 386):
            for page in range(4):
                x = 6 + (page % 2) * 80 + 38
                y = 60 + (page // 2) * 36 + 16
                if height < 300:
                    self.assertLess(y, height)
                    self.assertEqual(((y - 60) // 36) * 2 + ((x - 6) // 80), page)
            if height == 146:
                list_top = 64
                list_height = max(36, height - list_top - 28 - 6)
                self.assertLessEqual(list_top + list_height, height - 28)

    def test_task_manager_compact_performance_cards_fit_and_match_hit_tests(self):
        render = TASK_MANAGER.split("private void RenderPerformance(Canvas canvas)", 1)[1].split(
            "private void DrawResource(Canvas canvas", 1)[0]
        hit = TASK_MANAGER.split("public int PerformanceResourceAt(int x, int y)", 1)[1].split(
            "public int CpuGraphModeAt(int x, int y)", 1)[0]
        self.assertIn("if (CompactPerformanceLayout)", render)
        self.assertIn("if (CompactPerformanceLayout)", hit)
        self.assertIn("if (CompactPerformanceLayout || app.ActivePage", TASK_MANAGER)
        pager = TASK_MANAGER.split("public int CpuLogicalPageActionAt(int x, int y)", 1)[1].split(
            "public int RowAt(int x, int y)", 1)[0]
        self.assertIn("CompactPerformanceLayout || app.CpuGraphMode", pager)
        self.assertIn("app.PerformanceResource != TaskManagerModernApp.PerfCpu", pager)
        self.assertIn("int buttonWidth = Math.Max(1, (available - 8) / 3);", render)
        self.assertIn("int buttonWidth = Math.Max(1, (available - 8) / 3);", hit)
        self.assertIn("DrawCompactResource(canvas", render)
        self.assertIn("if (Height >= 180)", render)
        self.assertIn("relative % (buttonWidth + 4) < buttonWidth", hit)
        for screen_width in (320, 640, 800, 1366, 1920):
            view_width = screen_width - 20
            if view_width >= 900:
                # CPU stats require 380px offset + 160px right column.
                panel_width = view_width - 360
                self.assertGreaterEqual(panel_width, 540)
                right_offset = max(380, panel_width // 2 + 80)
                right_width = max(160, panel_width - right_offset)
                self.assertLessEqual(right_offset + right_width, panel_width)
                continue
            available = max(1, view_width - 170 - 16)
            button_width = max(1, (available - 8) // 3)
            last_right = 170 + 8 + 2 * (button_width + 4) + button_width
            self.assertLessEqual(last_right, view_width - 8)
            for index in range(3):
                click_x = 170 + 8 + index * (button_width + 4) + button_width // 2
                relative = click_x - (170 + 8)
                self.assertEqual(relative // (button_width + 4), index)
                self.assertLess(relative % (button_width + 4), button_width)

    def test_display_firmware_mode_enumeration_and_switch_fail_closed(self):
        selector = code("source/GUI/DisplayModeSelector.cs")
        self.assertIn("Mode chosen = current;", selector)
        self.assertIn("chosen = SelectBest(current, canvas.AvailableModes);", selector)
        self.assertIn("catch (NotSupportedException)", selector)
        self.assertGreaterEqual(selector.count("catch (NotSupportedException)"), 2)
        self.assertGreaterEqual(selector.count("catch (InvalidOperationException)"), 2)
        self.assertIn("canvas = Canvas.GetFullScreen();", selector)
        # Source/exception contract only; actual firmware fallback requires
        # a real boot and must not be reported as hardware-verified.

    def test_display_mode_selection_enforces_both_4k_axes(self):
        selector = code("source/GUI/DisplayModeSelector.cs")
        self.assertIn("private const int MaxWidth = 3840;", selector)
        self.assertIn("private const int MaxHeight = 2160;", selector)
        self.assertIn("mode.Width > MaxWidth", selector)
        self.assertIn("mode.Height > MaxHeight", selector)
        self.assertIn("pixels > MaxPixels", selector)
        self.assertIn("Canvas.GetFullScreen(chosen)", selector)
        self.assertIn("canvas.Width", selector)
        # Mathematical contract for advertised 32-bit modes. The driver
        # still decides whether a real framebuffer mode switch succeeds.
        for width, height, allowed in (
            (800, 600, True), (1366, 768, True),
            (1920, 1080, True), (3840, 2160, True),
            (8000, 1000, False), (1000, 8000, False),
            (4000, 2000, False), (640, 480, True),
        ):
            self.assertEqual(
                width >= 640 and height >= 480 and
                width <= 3840 and height <= 2160 and
                width * height <= 3840 * 2160, allowed)

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
        self.assertIn("int maxWidth = System.Math.Max(1, desktopWidth - margin - resizeStartX);", WINDOW)
        self.assertIn("int maxHeight = System.Math.Max(1, desktopHeight - margin - resizeStartY);", WINDOW)
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

    def test_start_menu_compacts_both_grids_and_power_controls_on_small_modes(self):
        # Source-level contract: the geometry used for painting must be the
        # same Button geometry used by the mouse hit-test. Not a guest boot.
        for expression in (
            "private bool CompactLayout => Height < 530;",
            "button.Y = Y + PinnedCardsY + row * (PinnedCardsHeight + PinnedCardsGap);",
            "button.Height = PinnedCardsHeight;",
            "button.Y = Y + ToolsCardsY + row * (ToolsCardsHeight + ToolsCardsGap);",
            "button.Height = ToolsCardsHeight;",
            "Y + PinnedLabelY", "Y + ToolsLabelY",
            "button.Y + (CompactLayout ? 33 : 56)",
            "button.Y + (CompactLayout ? 10 : 17)",
            "int toolWidth = Math.Max(60,",
            "int powerGap = Width < 480 ? 6 : 8;",
            "if (button.Width < 72)",
        ):
            self.assertIn(expression, START_MENU)

        for screen_width, screen_height in ((640, 480), (800, 560),
                                            (800, 580), (800, 600),
                                            (1366, 768), (1920, 1080)):
            menu_height = min(560, max(1, screen_height - 44 - 16))
            compact = menu_height < 530
            pinned_top = 134 if compact else 146
            pinned_height = 53 if compact else 88
            pinned_gap = 5 if compact else 9
            tools_label = 250 if compact else 342
            tools_top = 264 if compact else 360
            tools_height = 30 if compact else 42
            tools_gap = 4 if compact else 8
            self.assertLess(pinned_top + 2 * pinned_height + pinned_gap, tools_label)
            self.assertLess(tools_top + 2 * tools_height + tools_gap,
                            menu_height - 76)
            self.assertEqual(compact, menu_height < 530)

        for screen_width in (320, 400, 480, 640):
            menu_width = min(480, max(1, screen_width - 16))
            pinned_width = max(86, (menu_width - 28 - 18) // 3)
            tool_width = max(60, (menu_width - 28 - 16) // 3)
            self.assertLessEqual(28 + 3 * pinned_width + 2 * 9, menu_width)
            self.assertLessEqual(28 + 3 * tool_width + 2 * 8, menu_width)

        # Narrower-than-design Start menu keeps the four distinct session
        # buttons to the right of the identity block, all inside the panel.
        for screen_width in (320, 400, 480, 640):
            menu_width = min(480, max(1, screen_width - 16))
            gap = 6 if menu_width < 480 else 8
            button_width = (max(24, (menu_width - 28 - 110 - gap * 3) // 4)
                            if menu_width < 480 else 82)
            right = menu_width - 14
            left = right - 4 * button_width - 3 * gap
            # Full-width buttons start at 114px; identity text ends at 106px.
            self.assertGreaterEqual(left, 106 + 8)
            self.assertLessEqual(right, menu_width - 14)
            self.assertGreaterEqual(button_width, 24)

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
        self.assertIn("bool newPress = (leftClicked && !leftWasClicked) ||", mouse)
        self.assertIn("(rightClicked && !rightWasClicked);", mouse)
        self.assertIn("if (newPress)", mouse)
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
        self.assertIn("overflow = runningCount > slots && slots >= 1;", TASKBAR)
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
        for screen_width in (240, 320, 360, 400, 480, 540, 559, 560, 640,
                             800, 1024, 1366, 1920):
            tray_width = 132 if screen_width < 560 else 278
            tray_x = screen_width - tray_width
            self.assertGreaterEqual(tray_x, 48)
            notification_x = tray_x + 9
            self.assertLessEqual(notification_x + 34, screen_width - 88)
            self.assertLessEqual(screen_width - 78 + 68, screen_width)
            slots = max(0, (tray_x - 60 + 5) // 45)
            self.assertGreaterEqual(slots, 1)
            for running_count in (0, 1, 2, 9, 10, 11, 23, 50):
                overflow = running_count > slots and slots >= 1
                page_slots = slots - 1 if overflow else slots
                self.assertGreaterEqual(page_slots, 0)
                if page_slots:
                    last_button_right = 60 + (page_slots - 1) * 45 + 40
                    self.assertLessEqual(last_button_right, tray_x)
                if overflow:
                    arrow_x = 60 + page_slots * 45
                    self.assertLessEqual(arrow_x + 40, tray_x)
                    if page_slots:
                        offsets = list(range(0, running_count, page_slots))
                        self.assertEqual(
                            [index for offset in offsets
                             for index in range(offset, min(running_count, offset + page_slots))],
                            list(range(running_count))
                        )
                        self.assertTrue(offsets[-1] + page_slots >= running_count)
                    else:
                        # At 240px there is room for just one taskbar slot.
                        # The arrow cycles MRU windows; no zero-step paging.
                        self.assertEqual(slots, 1)
                        self.assertGreater(running_count, 1)
                        self.assertIn("if (pageSlots == 0)", TASKBAR)
                        self.assertIn("applicationManager.CycleWindow(false);", TASKBAR)
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
                    overflow = running_count > slots and slots >= 1
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

    def test_start_menu_taskbar_click_dismisses_but_start_button_toggles(self):
        dispatch = DESKTOP.split("bool startMenuWasVisible =", 1)[1].split(
            "int priorMouseX =", 1
        )[0]
        self.assertIn("bool startMenuTaskbarDismiss = startMenuWasVisible && !startMenuInside &&", dispatch)
        self.assertIn("currentLeftButtonState && !previousLeftButtonState &&", dispatch)
        self.assertIn("mouseY >= taskbar.Y && mouseY < taskbar.Y + taskbar.Height &&", dispatch)
        self.assertIn("!taskbar.IsStartButtonHit(mouseX, mouseY);", dispatch)
        self.assertIn("if (startMenuTaskbarDismiss)\n                            startMenu.Visible = false;", dispatch)
        self.assertIn("bool startMenuCaptures = startMenuInside || startMenuOutsideClick;", dispatch)
        self.assertLess(dispatch.index("if (startMenuTaskbarDismiss)"),
                        dispatch.index("taskbar.UpdateInteractions("))
        self.assertIn("private readonly Button startButton;", TASKBAR)
        self.assertIn("startButton = new Button(4, Y + 4, StartButtonWidth - 8, height - 8", TASKBAR)
        self.assertIn("return startButton != null && startButton.Contains(mouseX, mouseY);", TASKBAR)
        # Independent hitbox model for 800p/HD/FHD: an app tile or tray
        # click closes Start and still reaches the taskbar. Start itself
        # stays open until the normal button callback toggles it.
        for screen_width in (240, 640, 800, 1366, 1920):
            taskbar_y = 600
            for x in (0, 3, 4, 24, 43, 44, 60, 100, screen_width - 1):
                for y in (taskbar_y - 1, taskbar_y, taskbar_y + 3,
                          taskbar_y + 4, taskbar_y + 39, taskbar_y + 43, taskbar_y + 44):
                    on_taskbar = 0 <= x < screen_width and taskbar_y <= y < taskbar_y + 44
                    on_start = 4 <= x < 44 and taskbar_y + 4 <= y < taskbar_y + 40
                    dismiss = on_taskbar and not on_start
                    if on_start:
                        self.assertFalse(dismiss, (screen_width, x, y))
                    elif on_taskbar:
                        self.assertTrue(dismiss, (screen_width, x, y))

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
        self.assertIn("internal int GridRowsForView =>", FILE_MANAGER)
        self.assertIn("GridWidthForView + TileGap", columns)
        self.assertIn("GridRowsForView", selection)
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
            painted_rows = max(1, (grid_height + 60) // (94 + 6))
            scroll_rows = max(1, (grid_height + 60) // (94 + 6))
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
        refresh = FILE_MANAGER.split("private bool RefreshInternal(string keep)", 1)[1].split(
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
        self.assertIn("ResetClickSequence();\n            // Match the painted 36px controls", FILE_MANAGER)
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
        self.assertEqual(grid.count("tileWidth, visibleTileHeight"), 2)
        self.assertIn("tileY, Math.Min(3, tileWidth), visibleTileHeight", grid)
        # Minimum Window(320x220): view 300x170, grid height 88,
        # selection used to extend 6px into the footer.
        for window_height in (220, 226, 240, 320, 520):
            view_height = window_height - 50
            grid_height = max(40, view_height - 24 - 50 - 8)
            rows = max(1, (grid_height + 60) // 100)
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
                        unmount.index('currentPath = Directory.Exists("/root") ? "/root" : "/";'))
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

        # At 320x240 no complete card fits between filters and footer.
        # Selection/Enter/Delete must not act on an invisible notification.
        self.assertIn("System.Math.Max(0, System.Math.Min(VisibleRows, rows))", NOTIFICATIONS)
        for method in ("MoveSelection", "KeepSelectionVisible", "MarkSelectedRead", "DismissSelected"):
            body = NOTIFICATIONS.split("private " + ("bool" if method == "X" else "void") + " " + method + "(", 1)[1]
            self.assertIn("if (VisibleRowCount <= 0)", body.split("private ", 1)[0])
        for screen_height, expected_rows in ((240, 0), (480, 2)):
            panel_height = screen_height - 44 - 20
            available_height = max(0, panel_height - 130 - 72)
            rows = max(0, min(8, (available_height + 7) // 89))
            self.assertEqual(rows, expected_rows)
            if rows:
                self.assertLessEqual(130 + (rows - 1) * 89 + 82, panel_height - 72)

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
        refresh = FILE_MANAGER.split("private bool RefreshInternal(string keep)", 1)[1].split(
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
        self.assertIn("GridRowsForView", page)
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
            rows = max(1, (grid_height + 60) // (94 + 6))
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
        self.assertIn("if (right && dialogMode == 0 && contextMenuVisible)", mouse)
        self.assertIn("if (!oldRight || (left && !oldLeft))", mouse)
        self.assertLess(mouse.index("if (right && dialogMode == 0 && contextMenuVisible)"),
                        mouse.index("if (left && !oldLeft && dialogMode == 0 && contextMenuVisible)"))
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
        self.assertIn('spec_from_file_location("qmp_gui_filemanager", sys.argv[3])', QEMU_SMOKE)
        self.assertIn("width, height, pixels = probe.read_ppm(sys.argv[1])", QEMU_SMOKE)
        self.assertNotIn("if len(pixels) != width * height * 3:", QEMU_SMOKE)
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


    def test_file_manager_failed_directory_scan_is_transactional(self):
        navigation = FILE_MANAGER.split("private void NavigateTo(string path)", 1)[1].split(
            "public override void HandleKeyboard(", 1)[0]
        refresh = FILE_MANAGER.split("private bool RefreshInternal(string keep)", 1)[1].split(
            "private void ApplySearchFilter()", 1)[0]
        self.assertIn("string previousPath = currentPath;", navigation)
        self.assertIn("string previousSearch = searchText;", navigation)
        self.assertIn('if (!RefreshInternal(""))', navigation)
        rollback = navigation.split('if (!RefreshInternal(""))', 1)[1]
        self.assertIn("currentPath = previousPath;", rollback)
        self.assertIn("searchText = previousSearch;", rollback)
        self.assertIn("List<FileEntry> scanned = new List<FileEntry>();", refresh)
        self.assertIn("Add(scanned, Directory.GetDirectories(targetPath), true);", refresh)
        self.assertIn("Add(scanned, Directory.GetFiles(targetPath), false);", refresh)
        self.assertIn("Sort(scanned);", refresh)
        self.assertIn("if (!hasLoadedDirectory && targetPath == \"/root\" &&", refresh)
        self.assertIn("!Directory.Exists(targetPath))", refresh)
        self.assertIn('targetPath = "/";', refresh)
        self.assertNotIn('Directory.Exists(currentPath) ? currentPath : "/"', refresh)
        self.assertLess(refresh.index("Directory.GetFiles(targetPath)"),
                        refresh.index("hasLoadedDirectory = true;"))
        self.assertLess(refresh.index("Directory.GetFiles(targetPath)"),
                        refresh.index("entries.Clear();"))
        self.assertLess(refresh.index("Sort(scanned);"),
                        refresh.index("cachedEntries.Clear();"))
        failure = refresh.split("catch (Exception ex)", 1)[1]
        self.assertIn('status = "Read error: " + ex.Message;', failure)
        self.assertIn("return false;", failure)
        for mutation in ("entries.Clear();", "cachedEntries.Clear();",
                         "selectedIndex = -1;", "scrollIndex = 0;"):
            self.assertNotIn(mutation, failure)
        self.assertNotIn("File.Write", refresh)
        self.assertNotIn("TryUnmount", refresh)
        self.assertNotIn("TryMount", refresh)

    def test_file_manager_unmount_fallback_never_reads_detached_volume(self):
        unmount = FILE_MANAGER.split("private void UnmountVolume(SidebarEntry entry)", 1)[1].split(
            "private static bool IsInsidePath(", 1)[0]
        self.assertIn("if (!StorageMountManager.TryUnmount(entry.PartitionIndex, out error))", unmount)
        self.assertIn('currentPath = Directory.Exists("/root") ? "/root" : "/";', unmount)
        self.assertIn('string unmountStatus = "Unmounted Partition " + entry.PartitionIndex;', unmount)
        self.assertIn('if (!RefreshInternal(unmountStatus) && viewingVolume && currentPath != "/")', unmount)
        self.assertIn('currentPath = "/";', unmount)
        self.assertIn('RefreshInternal(unmountStatus);', unmount)
        # No implicit mount, formatting or writes after successful unmount.
        self.assertNotIn("TryMount(", unmount)
        self.assertNotIn("File.Write", unmount)
        self.assertLess(unmount.index("if (!StorageMountManager.TryUnmount("),
                        unmount.index('currentPath = Directory.Exists("/root")'))


    def test_file_manager_successful_detach_invalidates_stale_snapshot(self):
        unmount = FILE_MANAGER.split("private void UnmountVolume(SidebarEntry entry)", 1)[1].split(
            "private static bool IsInsidePath(", 1)[0]
        failed = unmount.split("if (!StorageMountManager.TryUnmount(", 1)[1].split(
            "if (viewingVolume)", 1)[0]
        # A failed detach must preserve the old view and path.
        self.assertNotIn("entries.Clear();", failed)
        self.assertNotIn("cachedEntries.Clear();", failed)
        success = unmount.split("if (viewingVolume)", 1)[1].split(
            'string unmountStatus = "Unmounted Partition "', 1)[0]
        for statement in ("entries.Clear();", "cachedEntries.Clear();",
                          "hasLoadedDirectory = false;", "loadedDirectoryPath = null;",
                          "selectedIndex = -1;", "scrollIndex = 0;",
                          "contextMenuVisible = false;", "contextMenuSelection = -1;",
                          "ResetClickSequence();"):
            self.assertIn(statement, success)
        self.assertLess(success.index("entries.Clear();"),
                        success.index('currentPath = Directory.Exists("/root")'))
        self.assertLess(unmount.index("entries.Clear();"),
                        unmount.index("RefreshInternal(unmountStatus)"))
        self.assertIn('if (!RefreshInternal(unmountStatus) && viewingVolume && currentPath != "/")',
                      unmount)
        # Model a detached volume whose /root and / fallback scans both
        # fail: the old FAT32 entries are not left selectable or executable.
        old_entries = ["/mnt/usb/secret.txt", "/mnt/usb/docs"]
        old_cache = list(old_entries)
        viewing_volume = True
        if viewing_volume:
            old_entries.clear()
            old_cache.clear()
            selected = -1
            menu_visible = False
        self.assertEqual(old_entries, [])
        self.assertEqual(old_cache, [])
        self.assertEqual(selected, -1)
        self.assertFalse(menu_visible)

    def test_file_manager_create_dialog_is_modal_to_window_chrome(self):
        mouse = FILE_MANAGER.split("public override void HandleMouse(int mouseX", 1)[1].split(
            "private void ResetClickSequence()", 1)[0]
        guard = mouse.split("if (dialogMode != 0)", 1)[1].split(
            "Window.HandleMouse(mouseX, mouseY, left, oldLeft);", 1)[0]
        self.assertIn("ResetClickSequence();", guard)
        self.assertIn("return;", guard)
        self.assertLess(mouse.index("if (dialogMode != 0)"),
                        mouse.index("Window.HandleMouse(mouseX, mouseY, left, oldLeft);"))
        self.assertIn("if ((left && !oldLeft) || (right && !oldRight))", guard)


    def test_file_manager_refresh_keeps_selection_and_scroll_only_in_same_directory(self):
        refresh = FILE_MANAGER.split("private bool RefreshInternal(string keep)", 1)[1].split(
            "private void ApplySearchFilter()", 1)[0]
        self.assertIn("private string loadedDirectoryPath;", FILE_MANAGER)
        self.assertIn("string.Equals(loadedDirectoryPath, targetPath, StringComparison.Ordinal)", refresh)
        self.assertIn("string selectedPath = sameDirectory && selectedIndex >= 0", refresh)
        self.assertIn("int previousScroll = sameDirectory ? scrollIndex : 0;", refresh)
        self.assertIn("loadedDirectoryPath = targetPath;", refresh)
        self.assertIn("scrollIndex = previousScroll;", refresh)
        self.assertIn("string.Equals(entries[i].FullPath, selectedPath, StringComparison.Ordinal)", refresh)
        self.assertIn("EnsureSelectionVisible();", refresh)
        self.assertLess(refresh.index("Sort(scanned);"), refresh.index("loadedDirectoryPath = targetPath;"))
        self.assertIn("string firstVisiblePath = sameDirectory && scrollIndex >= 0", refresh)
        self.assertIn("string.Equals(entries[i].FullPath, firstVisiblePath, StringComparison.Ordinal)", refresh)
        self.assertIn("scrollIndex = i;", refresh)
        self.assertLess(refresh.index("Filter();"), refresh.index("if (!string.IsNullOrEmpty(selectedPath) ||"))
        self.assertLess(refresh.index("if (!string.IsNullOrEmpty(selectedPath) ||"),
                        refresh.index("EnsureSelectionVisible();"))
        self.assertNotIn("selectedIndex = i;", refresh.split("catch (Exception ex)", 1)[1])

        # Independent state model: a new snapshot has new object instances.
        # Preserve by full path, not by index or object identity; never
        # accidentally select the file that replaced a removed item.
        def refreshed(path, old_path, old_selection, old_scroll, new_paths):
            same = path == old_path
            selected_path = old_selection if same else None
            selected = next((i for i, name in enumerate(new_paths)
                             if name == selected_path), -1)
            return selected, old_scroll if same else 0

        self.assertEqual(refreshed("/root", "/root", "/root/z.txt", 8,
                                   ["/root/a.txt", "/root/z.txt"]), (1, 8))
        self.assertEqual(refreshed("/root", "/root", "/root/z.txt", 8,
                                   ["/root/a.txt", "/root/b.txt"]), (-1, 8))
        self.assertEqual(refreshed("/mnt/data", "/root", "/root/z.txt", 8,
                                   ["/mnt/data/z.txt"]), (-1, 0))

        # Insertions before the old first visible tile must move the numeric
        # scroll offset, while retaining that same file as the viewport anchor.
        def anchor_after_refresh(old_first_path, new_paths, old_scroll):
            return next((i for i, name in enumerate(new_paths)
                         if name == old_first_path), old_scroll)

        self.assertEqual(anchor_after_refresh("/root/f.txt",
                         ["/root/a.txt", "/root/b.txt", "/root/f.txt", "/root/z.txt"], 1), 2)
        self.assertEqual(anchor_after_refresh("/root/f.txt",
                         ["/root/a.txt", "/root/b.txt", "/root/z.txt"], 1), 1)


    def test_file_manager_live_filter_uses_cached_directory_snapshot(self):
        refresh = FILE_MANAGER.split("private bool RefreshInternal(string keep)", 1)[1].split(
            "private void RebuildSidebar()", 1)[0]
        filter_only = refresh.split("private void ApplySearchFilter()", 1)[1]
        self.assertIn("cachedEntries.Clear();", refresh)
        self.assertIn("cachedEntries.AddRange(scanned);", refresh)
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
        sort = FILE_MANAGER.split("private void Sort(List<FileEntry> destination)", 1)[1].split(
            "private int Compare(", 1)[0]
        compare = FILE_MANAGER.split("private int Compare(", 1)[1].split(
            "private void Filter()", 1)[0]
        self.assertIn("FileEntry[] scratch = new FileEntry[count];", sort)
        self.assertIn("for (int run = 1; run < count;", sort)
        self.assertIn("Compare(destination[left], destination[right]) <= 0", sort)
        self.assertIn("scratch[write] = destination[left++];", sort)
        self.assertIn("scratch[write] = destination[right++];", sort)
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
        self.assertIn("int desktopHeight = System.Math.Max(1, screenHeight - DesktopTaskbarHeight);",
                      COMMAND_PALETTE)
        self.assertIn("Height = System.Math.Min(Height, System.Math.Max(1, desktopHeight - 16));",
                      COMMAND_PALETTE)
        self.assertIn("X = System.Math.Max(0, (screenWidth - Width) / 2);",
                      COMMAND_PALETTE)
        self.assertIn("desktopHeight - Height));", COMMAND_PALETTE)
        for width, screen_height in ((320, 240), (640, 480), (800, 600),
                                     (1024, 768), (1366, 768), (1920, 1080)):
            desktop_height = max(1, screen_height - 44)
            palette_width = min(680, max(1, width - 16))
            palette_height = min(448, max(1, desktop_height - 16))
            x = max(0, (width - palette_width) // 2)
            y = max(0, min(max(8, (desktop_height - palette_height) // 4),
                           desktop_height - palette_height))
            self.assertGreaterEqual(x, 0)
            self.assertGreaterEqual(y, 0)
            self.assertLessEqual(x + palette_width, width)
            self.assertLessEqual(y + palette_height, desktop_height)
            self.assertLessEqual(y + palette_height, screen_height - 44)
            if screen_height == 480:
                self.assertEqual((y, palette_height), (8, 420))

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


    def test_file_manager_modal_keyboard_blocks_background_actions(self):
        keyboard = FILE_MANAGER.split("public override void HandleKeyboard(KeyEvent key)", 1)[1].split(
            "private void CopySelected(", 1)[0]
        dialog = keyboard.split("if (dialogMode != 0)", 1)[1].split(
            "if (contextMenuVisible)", 1)[0]
        menu = keyboard.split("if (contextMenuVisible)", 1)[1].split(
            "bool control =", 1)[0]
        self.assertIn(
            "bool modified = (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) != 0;",
            keyboard)
        self.assertLess(dialog.index("if (modified)"), dialog.index("FinishCreate();"))
        self.assertLess(menu.index("if (modified)"), menu.index("ConsoleKeyEx.UpArrow"))
        self.assertIn("contextMenuSelection = -1;", menu)
        self.assertIn("if (key.Key == ConsoleKeyEx.Escape)", menu)
        self.assertRegex(menu, r"(?s)if \(\(key.Key == ConsoleKeyEx.Enter \|\| key.Key == ConsoleKeyEx.NumEnter\) &&\s*key.Modifiers == 0\).*?return;\s*}\s*return;\s*}")
        # The modal branch must consume all keys before clipboard, search,
        # refresh and navigation dispatch. A modified Enter cannot create.
        self.assertLess(keyboard.index("if (contextMenuVisible)"), keyboard.index("bool control ="))
        self.assertIn(
            "bool control = (key.Modifiers & ConsoleModifiers.Control) != 0 &&\n"
            "                (key.Modifiers & (ConsoleModifiers.Alt | ConsoleModifiers.Shift)) == 0;", keyboard)
        self.assertLess(keyboard.index("if (modified)\n                return;"),
                        keyboard.index("if (key.Key == ConsoleKeyEx.Backspace)", keyboard.index("bool control =")))
        for menu_open, modified, key in (
            (True, False, "a"), (True, False, "F5"),
            (True, True, "V"), (True, True, "Enter"),
            (False, True, "Enter"), (False, True, "Backspace"),
        ):
            background_action = not menu_open and not modified
            self.assertFalse(background_action, (menu_open, modified, key))


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
        self.assertIn("int actionLeft = 4 + SidebarWidthForView - 27;", click)
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

    def test_notification_modified_navigation_cannot_dismiss_or_mark_read(self):
        keyboard = NOTIFICATIONS.split("public bool HandleKeyboard(KeyEvent key)", 1)[1].split(
            "public bool UpdateInteractions(", 1)[0]
        # Escape closes the overlay, but all other modified keys are
        # consumed before any filter, selection or destructive operation.
        self.assertLess(keyboard.index("ConsoleKeyEx.Escape"),
                        keyboard.index("if (control || alt)"))
        for action in ("ConsoleKeyEx.LeftArrow", "ConsoleKeyEx.RightArrow",
                       "ConsoleKeyEx.UpArrow", "ConsoleKeyEx.DownArrow",
                       "ConsoleKeyEx.Enter", "ConsoleKeyEx.Delete"):
            self.assertLess(keyboard.index("if (control || alt)"),
                            keyboard.index(action), action)
        self.assertEqual(keyboard.count("bool control ="), 1)
        self.assertEqual(keyboard.count("bool alt ="), 1)
        for ctrl, alt, key in ((True, False, "Delete"), (False, True, "Enter"),
                               (True, False, "LeftArrow"), (True, True, "DownArrow")):
            should_dispatch = not (ctrl or alt)
            self.assertFalse(should_dispatch, key)


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


    def test_task_manager_keyboard_pages_and_modifier_safety(self):
        keyboard = TASK_MANAGER.split("public override void HandleKeyboard(KeyEvent key)", 1)[1].split(
            "private void SetPage(", 1)[0]
        guard = "(key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) != 0"
        self.assertIn(guard, keyboard)
        self.assertLess(keyboard.index(guard), keyboard.index("ConsoleKeyEx.Delete"))
        self.assertIn("ConsoleKeyEx.PageUp) Select(-view.VisibleRows)", keyboard)
        self.assertIn("ConsoleKeyEx.PageDown) Select(view.VisibleRows)", keyboard)
        self.assertIn("ConsoleKeyEx.Home) SelectAbsolute(0)", keyboard)
        self.assertIn("ConsoleKeyEx.End) SelectAbsolute(visibleRows.Count - 1)", keyboard)
        selection = TASK_MANAGER.split("private void Select(int delta)", 1)[1].split(
            "private void EndSelectedTask(", 1)[0]
        self.assertIn("private void SelectAbsolute(int index)", selection)
        self.assertIn("Math.Max(0, Math.Min(visibleRows.Count - 1, index))", selection)
        self.assertIn("EnsureSelectionVisible();", selection)
        # Model clamping on short (one row) and HD/FHD (multiple rows)
        # without requiring a real guest to report a successful keypress.
        for count in (0, 1, 3, 21):
            for visible in (1, 3, 12):
                for selected in (-1, 0, max(0, count - 1)):
                    if count == 0:
                        continue
                    page_down = 0 if selected < 0 else min(count - 1, selected + visible)
                    page_up = count - 1 if selected < 0 else max(0, selected - visible)
                    self.assertGreaterEqual(page_down, 0)
                    self.assertLess(page_down, count)
                    self.assertGreaterEqual(page_up, 0)
                    self.assertLess(page_up, count)


    def test_task_manager_short_wide_layout_and_unpainted_rows_are_not_clickable(self):
        manager = TASK_MANAGER
        self.assertIn("Height < ListTop + FooterHeight + RowHeight + 6", manager)
        self.assertIn("private bool CompactPerformanceLayout => Width < 900 || Height < 502;", manager)
        self.assertIn("private int ListTopForView => CompactProcessLayout ? 64 : ListTop;", manager)
        process = manager.split("private void RenderProcessPage(Canvas canvas)", 1)[1].split(
            "private void DrawSummary(", 1)[0]
        table = manager.split("private void RenderTable(Canvas canvas", 1)[1].split(
            "private void DrawRow(", 1)[0]
        hit = manager.split("public int RowAt(int x, int y)", 1)[1].split(
            "public bool HandleScrollMouse(", 1)[0]
        self.assertIn("if (CompactProcessLayout)", process)
        self.assertIn("if (!CompactProcessLayout)", table)
        self.assertIn("if (CompactProcessLayout)", table)
        self.assertIn("if (rowOffset >= VisibleRows)", hit)
        self.assertLess(hit.index("if (rowOffset >= VisibleRows)"),
                        hit.index("int index = app.ScrollIndex + rowOffset;"))

        # Model the same layout for small and wide windows with minimum height.
        for width in (300, 480, 780, 1020):
            for height in (170, 190, 215, 216, 300, 450, 460, 635):
                compact = width < 550 or height < 146 + 28 + 36 + 6
                list_top = 64 if compact else 146
                list_height = max(36, height - list_top - 28 - 6)
                painted_rows = max(1, list_height // 36)
                self.assertLessEqual(list_top + list_height, height - 28)
                self.assertLessEqual(list_top + painted_rows * 36, height - 28)
                for y in range(list_top, list_top + list_height):
                    row_offset = (y - list_top) // 36
                    clickable = row_offset < painted_rows
                    self.assertEqual(clickable, y < list_top + painted_rows * 36)
                self.assertEqual(width < 900 or height < 502,
                                 not (width >= 900 and height >= 502))

    def test_window_minimize_and_restore_cancel_stale_mouse_gestures(self):
        minimize = WINDOW.split("public void Minimize()", 1)[1].split(
            "public void RestoreFromMinimized()", 1)[0]
        restore = WINDOW.split("public void RestoreFromMinimized()", 1)[1].split(
            "public void ToggleMinimize()", 1)[0]
        self.assertIn("lastTitleClickTimestamp = 0;", minimize)
        for state in ("dragging = false;", "resizing = false;", "resizeMode = 0;",
                      "hoveredResizeMode = 0;", "hoveredControl = 0;",
                      "lastTitleClickTimestamp = 0;"):
            self.assertIn(state, restore)
        self.assertLess(restore.index("lastTitleClickTimestamp = 0;"),
                        restore.index("IsMinimized = false;"))


    def test_window_hit_testing_reflows_before_click_after_resolution_change(self):
        contains = WINDOW.split("public bool ContainsPoint(int mouseX, int mouseY)", 1)[1].split(
            "public void HandleMouse(", 1)[0]
        self.assertIn("if (!Visible || IsMinimized)", contains)
        self.assertIn("SyncDesktopBounds();", contains)
        self.assertLess(contains.index("SyncDesktopBounds();"),
                        contains.index("return mouseX >= X"))
        self.assertIn("mouseX < X + Width", contains)
        self.assertIn("mouseY < Y + Height", contains)
        # A former 1040x690 window at (165,110) must not remain clickable
        # beyond an 800x600 desktop (556px usable height) before repaint.
        old = (165, 110, 1040, 690)
        desktop = (800, 556)
        margin = 0
        width = min(old[2], desktop[0] - 2 * margin)
        height = min(old[3], desktop[1] - 2 * margin)
        x = max(margin, min(old[0], desktop[0] - width - margin))
        y = max(margin, min(old[1], desktop[1] - height - margin))
        self.assertEqual((x, y, width, height), (0, 0, 800, 556))
        self.assertFalse(x <= 950 < x + width)
        self.assertTrue(x <= 799 < x + width)


    def test_command_palette_uses_full_framebuffer_height_once(self):
        # GuiManager supplies physical canvas dimensions. CommandPalette alone
        # subtracts the 44px taskbar, preserving a fifth result at 640x480.
        self.assertIn(
            "new CommandPalette((int)canvas.Width, (int)canvas.Height, appRegistry)",
            DESKTOP)
        self.assertNotIn(
            "new CommandPalette((int)canvas.Width, desktopHeight, appRegistry)",
            DESKTOP)
        self.assertIn("int desktopHeight = System.Math.Max(1, screenHeight - DesktopTaskbarHeight);",
                      COMMAND_PALETTE)
        self.assertIn("Height = System.Math.Min(Height, System.Math.Max(1, desktopHeight - 16));",
                      COMMAND_PALETTE)
        self.assertIn("visibleRows = System.Math.Max(1,", COMMAND_PALETTE)

        for screen_width, screen_height in ((320, 240), (640, 480), (800, 600),
                                            (1366, 768), (1920, 1080)):
            desktop_height = screen_height - 44
            height = min(52 + 44 + 6 * 48 + 34 + 30, max(1, desktop_height - 16))
            list_space = max(1, height - (52 + 44 + 20) - 34)
            rows = max(1, min(6, list_space // 48))
            y = max(0, min(max(8, (desktop_height - height) // 4),
                           desktop_height - height))
            self.assertGreaterEqual(y, 0)
            self.assertLessEqual(y + height, desktop_height)
            row_height = min(48, max(1, list_space // rows))
            self.assertLessEqual(rows * row_height, list_space)
            if (screen_width, screen_height) == (640, 480):
                self.assertEqual((height, rows), (420, 5))
            if screen_height >= 600:
                self.assertEqual(rows, 6)

    def test_file_manager_grid_hit_test_stays_inside_painted_tiles(self):
        # The hit-test must use the same grid geometry as the renderer,
        # including the 320x240 firmware fallback (desktop < 220px).
        hit = FILE_MANAGER.split("private int HitEntryIndex(int x, int y)", 1)[1].split(
            "private bool ContextClick(", 1)[0]
        self.assertIn("relativeX >= GridWidthForView", hit)
        self.assertIn("relativeY >= GridHeightForView", hit)
        self.assertIn("row >= visibleRows", hit)
        self.assertIn("int visibleRows = GridRowsForView;", hit)
        self.assertLess(hit.index("relativeY >= GridHeightForView"),
                        hit.index("int col = relativeX / strideX;"))
        self.assertIn("int gridW = app.GridWidthForView;", FILE_MANAGER)
        self.assertIn("int contentH = app.GridHeightForView;", FILE_MANAGER)

        for window_width, window_height in ((320, 196), (320, 220),
                                            (800, 520), (820, 520),
                                            (1366, 720), (1920, 1000)):
            view_width = window_width - 20
            view_height = window_height - 50
            grid_width = max(112, view_width - 176 - 12)
            grid_height = max(40, view_height - 24 - 50 - 8)
            cols = max(1, (grid_width + 6) // 118)
            rows = max(1, (grid_height + 60) // 100)
            for rx in range(0, max(grid_width + 2, 120)):
                col = rx // 118
                for ry in range(0, max(grid_height + 2, 100)):
                    row = ry // 100
                    painted = (rx < grid_width and ry < grid_height and
                               col < cols and row < rows and
                               rx % 118 < 112 and ry % 100 < 94)
                    hit_test = (0 <= rx < grid_width and
                                0 <= ry < grid_height and
                                col < cols and row < rows and
                                rx % 118 < 112 and ry % 100 < 94)
                    self.assertEqual(hit_test, painted,
                                     (window_width, window_height, rx, ry))

    def test_file_manager_compact_tile_does_not_overpaint_footer(self):
        grid = FILE_MANAGER.split("private void Grid(Canvas canvas", 1)[1].split(
            "private static IconType ResolveEntryIcon(", 1)[0]
        self.assertIn("if (visibleTileHeight < iconSize + 33 || tileWidth < 88)", grid)
        self.assertIn("if (visibleTileHeight >= 32 && tileWidth >= 32)", grid)
        self.assertIn("tileX + 6, tileY + 7, compactIconSize, compactIconSize", grid)
        self.assertIn("tileX + 36, tileY + 17", grid)
        self.assertIn("tileWidth - 42", grid)
        # At 320x240 the window is constrained to 320x196: grid 64px.
        # Normal 42px icon + two-line label needs 75px; compact layout
        # fits both icon and single-line text without touching the footer.
        for grid_height in (40, 41, 64, 74):
            self.assertLessEqual(7 + 24, grid_height)
            self.assertLessEqual(17 + 7, grid_height)
        self.assertEqual(max(40, (196 - 50) - 24 - 50 - 8), 64)
        self.assertEqual(max(40, (220 - 50) - 24 - 50 - 8), 88)


    def test_file_manager_last_partial_row_remains_keyboard_reachable(self):
        ensure = FILE_MANAGER.split("private void EnsureSelectionVisible()", 1)[1].split(
            "private void GoUp()", 1)[0]
        self.assertIn("int lastRow = entries.Count > 0 ? (entries.Count - 1) / cols : 0;", ensure)
        self.assertIn("int maxScroll = Math.Max(0, (lastRow - rows + 1) * cols);", ensure)
        self.assertIn("scrollIndex = maxScroll;", ensure)
        self.assertNotIn("entries.Count - visible", ensure)

        # Reproduce the previous failure: 10 entries, 3 columns, 3 rows.
        # The last file has index 9; old maxScroll rounded 1 down to 0,
        # so the selected item was outside the 0..8 visible range.
        self.assertEqual(max(0, (10 - 9) - (10 - 9) % 3), 0)
        self.assertEqual(max(0, (((10 - 1) // 3) - 3 + 1) * 3), 3)

        for count in range(1, 71):
            for cols in range(1, 7):
                for rows in range(1, 7):
                    visible = cols * rows
                    max_scroll = max(0, (((count - 1) // cols) - rows + 1) * cols)
                    for selected in range(count):
                        # Model selection from the beginning and from a
                        # previously scrolled viewport after a resize.
                        for scroll in (0, max_scroll):
                            if selected < scroll:
                                scroll = selected
                            elif selected >= scroll + visible:
                                scroll = selected - visible + cols
                            scroll -= scroll % cols
                            scroll = max(0, min(scroll, max_scroll))
                            self.assertEqual(scroll % cols, 0)
                            self.assertLessEqual(scroll, selected)
                            self.assertLess(selected, scroll + visible,
                                            (count, cols, rows, selected, scroll))


    def test_file_manager_partial_rows_use_remaining_screen_space_safely(self):
        self.assertIn("internal const int MinPartialTileHeight = 40;", FILE_MANAGER)
        self.assertIn("internal int GridRowsForView => GridWidthForView <= 0 || GridHeightForView < MinPartialTileHeight ? 0 :", FILE_MANAGER)
        self.assertIn("GridHeightForView + TileHeight + TileGap - MinPartialTileHeight", FILE_MANAGER)
        hit = FILE_MANAGER.split("private int HitEntryIndex(int x, int y)", 1)[1].split(
            "private bool ContextClick(", 1)[0]
        grid = FILE_MANAGER.split("private void Grid(Canvas canvas", 1)[1].split(
            "private static IconType ResolveEntryIcon(", 1)[0]
        selection = FILE_MANAGER.split("private void EnsureSelectionVisible()", 1)[1].split(
            "private void GoUp()", 1)[0]
        page = FILE_MANAGER.split("private void SelectPage(int direction)", 1)[1].split(
            "private void Select(int delta)", 1)[0]
        for source in (hit, grid, selection, page):
            self.assertIn("GridRowsForView", source)

        # Only show the last row if >=40px is left. The old full-row
        # calculation wasted 88px at the default 820x520 window.
        for height, expected_rows in ((40, 1), (88, 1), (139, 1),
                                      (140, 2), (339, 3), (340, 4),
                                      (388, 4), (399, 4), (400, 4),
                                      (439, 4), (440, 5), (868, 9)):
            rows = max(1, (height + 60) // 100)
            self.assertEqual(rows, expected_rows)
            for row in range(rows):
                visible = min(94, height - row * 100)
                self.assertGreaterEqual(visible, 40)
                self.assertLessEqual(row * 100 + visible, height)

        # At 800x600 a default 820x520 window has 5 columns and
        # four usable rows, including a final 88px row: 20 vs 15 tiles.
        self.assertEqual(max(1, (612 + 6) // 118), 5)
        self.assertEqual(max(1, (388 + 6) // 100), 3)
        self.assertEqual(max(1, (388 + 60) // 100), 4)



class FileManagerToolbarBounds3095(unittest.TestCase):
    def test_toolbar_uses_same_bounds_for_paint_and_click(self):
        toolbar = FILE_MANAGER.split("private void Toolbar(int x)", 1)[1].split(
            "private void SidebarClick(", 1)[0]
        render = FILE_MANAGER.split("private void RenderToolbar(Canvas canvas)", 1)[1].split(
            "private void RenderSidebar(", 1)[0]
        self.assertIn("int offset = x - 8;", toolbar)
        self.assertIn("int button = offset / 42;", toolbar)
        self.assertIn("offset % 42 >= 36", toolbar)
        self.assertIn("button >= ToolbarButtonCountForView", toolbar)
        self.assertIn("ToolbarButtonCountForView => view.Width < 44 || view.Height < 44 ? 0 :", FILE_MANAGER)
        # C# truncates negative integer division toward zero; the explicit
        # guard is required for clients narrower than the first 36px button.
        for button in range(4):
            self.assertIn(f"if (app.ToolbarButtonCountForView > {button})", render)

        # Exhaustively compare the drawn 36px rectangles with the click
        # decoder for small/standard/HD/FHD client widths.
        for width in (1, 20, 43, 44, 79, 80, 85, 86, 127, 128,
                      169, 170, 180, 220, 300, 780, 1346, 1900):
            count = min(4, max(0, (width - 44) // 42 + 1))
            painted = set()
            for button in range(count):
                painted.update(range(8 + 42 * button, 44 + 42 * button))
            for x in range(width):
                decoded = -1
                if x >= 8:
                    offset = x - 8
                    button = offset // 42
                    if button < count and offset % 42 < 36:
                        decoded = button
                self.assertEqual(x in painted, decoded >= 0,
                                 (width, x, count, decoded))
            self.assertTrue(all(8 + 42 * i + 36 <= width
                                for i in range(count)))



class FileManagerClippedTiles3095(unittest.TestCase):
    def test_grid_stays_within_client_on_narrow_framebuffers(self):
        self.assertIn("GridWidthForView => Math.Max(0, view.Width - SidebarWidthForView - 12)", FILE_MANAGER)
        grid = FILE_MANAGER.split("private void Grid(Canvas canvas", 1)[1].split(
            "private static IconType ResolveEntryIcon(", 1)[0]
        hit = FILE_MANAGER.split("private int HitEntryIndex(int x, int y)", 1)[1].split(
            "private bool ContextClick(", 1)[0]
        self.assertIn("int tileWidth = Math.Min(FileManagerApp.TileWidth, width);", grid)
        self.assertIn("tileWidth < 88", grid)
        self.assertIn("tileWidth >= 48", grid)
        self.assertIn("tileWidth - 42", grid)
        self.assertIn("relativeX >= GridWidthForView", hit)
        for view_width in range(13, 401):
            sidebar = 0 if view_width < 220 else min(176, view_width - 124)
            grid_left = sidebar + 8
            grid_width = max(1, view_width - sidebar - 12)
            tile_width = min(112, grid_width)
            self.assertGreaterEqual(tile_width, 1)
            self.assertLessEqual(grid_left + grid_width, view_width - 4)
            self.assertLessEqual(grid_left + tile_width, view_width - 4)
            if tile_width >= 48:
                self.assertLessEqual(36 + tile_width - 42, tile_width)
            if tile_width >= 32:
                self.assertLessEqual(6 + min(24, tile_width - 8), tile_width)
            cols = max(1, (grid_width + 6) // 118)
            for relative_x in range(grid_width):
                col = relative_x // 118
                if col < cols and relative_x % 118 < 112:
                    self.assertLessEqual(col * 118 + min(112, grid_width - col * 118), grid_width)



class FileManagerShortClient3095(unittest.TestCase):
    def test_grid_has_no_fake_rows_and_never_overlaps_footer(self):
        self.assertIn("GridHeightForView => Math.Max(0, view.Height - FooterHeight - ContentTop - 8)", FILE_MANAGER)
        self.assertIn("GridRowsForView => GridWidthForView <= 0 || GridHeightForView < MinPartialTileHeight ? 0 :", FILE_MANAGER)
        hit = FILE_MANAGER.split("private int HitEntryIndex(int x, int y)", 1)[1].split(
            "private bool ContextClick(", 1)[0]
        selection = FILE_MANAGER.split("private void EnsureSelectionVisible()", 1)[1].split(
            "private void GoUp()", 1)[0]
        page = FILE_MANAGER.split("private void SelectPage(int direction)", 1)[1].split(
            "private void Select(int delta)", 1)[0]
        render = FILE_MANAGER.split("internal sealed class FileManagerView", 1)[1].split(
            "public override void Render(Canvas canvas)", 1)[1].split(
            "private void RenderToolbar(", 1)[0]
        grid = FILE_MANAGER.split("private void Grid(Canvas canvas", 1)[1].split(
            "private static IconType ResolveEntryIcon(", 1)[0]
        self.assertIn("if (GridRowsForView == 0 ||", hit)
        self.assertIn("if (rows == 0)", selection)
        self.assertIn("if (rows == 0)", page)
        self.assertIn("if (contentH > 0 && gridW > 0)", render)
        self.assertIn("if (height < FileManagerApp.MinPartialTileHeight)", grid)
        for height in range(1, 1001):
            grid_height = max(0, height - 24 - 50 - 8)
            rows = 0 if grid_height < 40 else max(1, (grid_height + 60) // 100)
            self.assertLessEqual(50 + grid_height, height - 28 if height >= 82 else 50)
            if grid_height < 40:
                self.assertEqual(rows, 0)
            else:
                self.assertGreater(rows, 0)
                for row in range(rows):
                    visible = min(94, grid_height - row * 100)
                    self.assertGreaterEqual(visible, 40)
                    self.assertLessEqual(row * 100 + visible, grid_height)



class FileManagerTinyChrome3095(unittest.TestCase):
    def test_modal_shadow_input_and_caption_stay_inside_client(self):
        dialog = FILE_MANAGER.split("private void Dialog(Canvas canvas)", 1)[1]
        self.assertIn("int width = Math.Min(430, Math.Max(0, Width - 16));", dialog)
        self.assertIn("int height = Math.Min(126, Math.Max(0, Height - 16));", dialog)
        self.assertIn("if (width < 32 || height < 28)", dialog)
        self.assertIn("int fieldHeight = Math.Min(30, Math.Max(0,", dialog)
        self.assertIn("if (width >= 48 && fieldHeight >= 18)", dialog)
        self.assertIn("if (height >= 108 && width >= 200)", dialog)
        self.assertIn("SmallTextRenderer.DrawClipped(canvas, title,", dialog)
        self.assertNotIn("Math.Max(280, Width - 40)", dialog)

        # Geometry model of the production formulas. Check the shadow's
        # extra four pixels, field, caption and hint at small/normal/HD/FHD.
        for client_width in range(1, 601):
            width = min(430, max(0, client_width - 16))
            if width < 32:
                continue
            x = (client_width - width) // 2
            self.assertGreaterEqual(x, 4)
            self.assertLessEqual(x + width + 4, client_width)
            self.assertGreaterEqual(width - 24, 8)
            for client_height in (1, 16, 27, 43, 44, 45, 63, 64, 80,
                                  100, 123, 124, 125, 126, 142, 170, 400):
                height = min(126, max(0, client_height - 16))
                if height < 28:
                    continue
                y = (client_height - height) // 2
                self.assertGreaterEqual(y, 4)
                self.assertLessEqual(y + height + 4, client_height)
                field_y = min(42, max(24, height - 40))
                field_height = min(30, max(0, height - field_y - 8))
                if width >= 48 and field_height >= 18:
                    self.assertGreaterEqual(field_y, 24)
                    self.assertLessEqual(field_y + field_height, height - 8)
                    self.assertGreaterEqual(width - 36, 12)
                    self.assertLessEqual(
                        field_y + min(12, field_height - 9) + 9,
                        field_y + field_height)
                if height >= 108 and width >= 200:
                    self.assertLessEqual(94 + 9, height)

    def test_toolbar_and_footer_never_submit_negative_chrome_rectangles(self):
        toolbar = FILE_MANAGER.split("private void RenderToolbar(Canvas canvas)", 1)[1].split(
            "private void RenderSidebar(", 1)[0]
        footer = FILE_MANAGER.split("private void RenderFooter(Canvas canvas, int footerY)", 1)[1].split(
            "private void Grid(Canvas canvas", 1)[0]
        self.assertIn("if (Width <= 8 || Height <= 4)", toolbar)
        self.assertIn("Width - 8, Math.Min(40, Height - 4)", toolbar)
        self.assertIn("if (Width >= 308 && Height >= 44)", toolbar)
        self.assertIn("int pathW = Width - 188;", toolbar)
        self.assertIn("if (Width <= 8 || Height < FileManagerApp.FooterHeight + 4)", footer)
        self.assertIn("view.Height < 44", FILE_MANAGER)

        for width in range(1, 601):
            for height in (1, 4, 5, 27, 28, 43, 44, 45, 80, 170, 500):
                if width > 8 and height > 4:
                    self.assertGreater(width - 8, 0)
                    self.assertGreater(min(40, height - 4), 0)
                    self.assertLessEqual(4 + min(40, height - 4), height)
                if width >= 308 and height >= 44:
                    self.assertGreaterEqual(width - 188, 120)
                    self.assertEqual(180 + width - 188, width - 8)
                if width > 8 and height >= 28:
                    self.assertGreater(width - 8, 0)
                    self.assertGreaterEqual(height - 28, 0)
                count = (min(4, (width - 44) // 42 + 1)
                         if width >= 44 and height >= 44 else 0)
                self.assertTrue(0 <= count <= 4)
                for index in range(count):
                    self.assertLessEqual(8 + index * 42 + 36, width)



class FileManagerCompactMenuAndFooter3095(unittest.TestCase):
    def test_compact_menu_stays_above_footer_and_dismisses_after_shrink(self):
        self.assertIn("internal bool CanShowContextMenu => view.Width >= 32 &&", FILE_MANAGER)
        self.assertIn("view.Height >= FooterHeight + 12 + 9 * ContextMenuItemCount", FILE_MANAGER)
        self.assertIn("Math.Max(9, Math.Min(ContextMenuItemHeight,", FILE_MANAGER)
        update = FILE_MANAGER.split("public override void Update()", 1)[1].split(
            "public override void HandleMouse(", 1)[0]
        mouse = FILE_MANAGER.split("public override void HandleMouse(int mouseX", 1)[1].split(
            "private void ResetClickSequence()", 1)[0]
        menu = FILE_MANAGER.split("private void Menu(Canvas canvas)", 1)[1].split(
            "private void Dialog(Canvas canvas)", 1)[0]
        self.assertIn("if (contextMenuVisible && !CanShowContextMenu)", update)
        self.assertIn("if (!CanShowContextMenu)", mouse)
        self.assertIn("if (!app.CanShowContextMenu)", menu)
        self.assertIn("bool showShortcut = !string.IsNullOrEmpty(shortcut) && menuWidth >= 160;", menu)
        self.assertIn("SmallTextRenderer.DrawClipped(canvas, text,", menu)
        self.assertIn("int textOffset = Math.Max(0,", menu)

        for width in range(1, 601):
            menu_width = min(240, max(1, width - 8))
            for height in range(1, 501):
                allowed = width >= 32 and height >= 24 + 12 + 9 * 7
                if not allowed:
                    continue
                row_height = max(9, min(29, (height - 24 - 12) // 7))
                menu_height = row_height * 7
                menu_x = max(4, min(width // 2, width - menu_width - 4))
                menu_y = max(4, min(height // 2, height - menu_height - 24 - 4))
                self.assertLessEqual(menu_x + menu_width + 4, width)
                self.assertLessEqual(menu_y + menu_height + 24 + 4, height)
                self.assertGreaterEqual(row_height, 9)
                self.assertGreaterEqual((row_height - 9) // 2, 0)
                if menu_width < 160:
                    self.assertGreaterEqual(menu_width - 20, 4)

    def test_footer_labels_never_exceed_client_width(self):
        footer = FILE_MANAGER.split("private void RenderFooter(Canvas canvas, int footerY)", 1)[1].split(
            "private void Grid(Canvas canvas", 1)[0]
        self.assertIn("int statusMaxWidth = Math.Max(0, Math.Min(", footer)
        self.assertIn("X + Width - 10 - statusX", footer)
        self.assertIn("if (statusMaxWidth > 0)", footer)
        self.assertIn("if (hintMaxWidth > 0)", footer)
        for width in range(9, 601):
            for hint_width in (0, 30, 80, 180, 500):
                hint_x = max(width // 2, width - 10 - hint_width)
                status_x = 10
                status_width = max(0, min(hint_x - status_x - 12,
                                          width - 10 - status_x))
                hint_max = max(0, width - 10 - hint_x)
                if status_width:
                    self.assertLessEqual(status_x + status_width, width - 10)
                if hint_max:
                    self.assertLessEqual(hint_x + hint_max, width - 10)



class FileManagerResizeClickSequence3095(unittest.TestCase):
    def test_resize_clears_double_click_before_new_tile_layout(self):
        update = FILE_MANAGER.split("public override void Update()", 1)[1].split(
            "public override void HandleMouse(", 1)[0]
        self.assertIn(
            "if (view.Width != previousViewWidth || view.Height != previousViewHeight)",
            update)
        resize = update.split(
            "if (view.Width != previousViewWidth || view.Height != previousViewHeight)", 1
        )[1].split("if (contextMenuVisible", 1)[0]
        self.assertIn("ResetClickSequence();", resize)
        self.assertIn("EnsureSelectionVisible();", resize)
        self.assertLess(resize.index("ResetClickSequence();"),
                        resize.index("EnsureSelectionVisible();"))
        # A width-only or height-only resize must invalidate the old pair.
        for before, after in (((300, 170), (460, 170)),
                              ((460, 170), (460, 300)),
                              ((460, 300), (300, 170)),
                              ((300, 170), (300, 170))):
            self.assertEqual(before != after,
                             before[0] != after[0] or before[1] != after[1])



class FileManagerZeroWidthGrid3095(unittest.TestCase):
    def test_no_phantom_grid_on_extremely_narrow_client(self):
        self.assertIn("GridWidthForView => Math.Max(0, view.Width - SidebarWidthForView - 12)", FILE_MANAGER)
        self.assertIn("GridRowsForView => GridWidthForView <= 0 ||", FILE_MANAGER)
        render = FILE_MANAGER.split("internal sealed class FileManagerView", 1)[1].split(
            "public override void Render(Canvas canvas)", 1)[1].split(
            "private void RenderToolbar(", 1)[0]
        self.assertIn("if (contentH > 0 && gridW > 0)", render)
        for view_width in range(1, 401):
            sidebar = 0 if view_width < 220 else min(176, view_width - 124)
            grid_width = max(0, view_width - sidebar - 12)
            grid_x = sidebar + 8
            if grid_width:
                self.assertGreaterEqual(grid_width, 1)
                self.assertLessEqual(grid_x + grid_width, view_width - 4)
            else:
                self.assertLessEqual(view_width, 12)
            for view_height in (1, 40, 81, 82, 121, 122, 170, 550):
                grid_height = max(0, view_height - 24 - 50 - 8)
                rows = (0 if grid_width <= 0 or grid_height < 40
                        else max(1, (grid_height + 60) // 100))
                if grid_width <= 0:
                    self.assertEqual(rows, 0)



class NarrowCaptionControls3095(unittest.TestCase):
    def test_caption_render_and_hit_testing_share_visible_button_count(self):
        self.assertIn("private int VisibleCaptionControls()", WINDOW)
        self.assertIn("int usableWidth = Width - RightPadding;", WINDOW)
        self.assertIn("usableWidth < ButtonSize ? 0 :", WINDOW)
        self.assertIn("System.Math.Min(3, usableWidth / ButtonSize)", WINDOW)
        mouse = WINDOW.split("public void HandleMouse(int mouseX", 1)[1].split(
            "private int GetResizeMode(", 1)[0]
        render = WINDOW.split("public override void Render(Canvas canvas)", 1)[1].split(
            "private void DrawResizeHint(", 1)[0]
        for section in (mouse, render):
            self.assertIn("int visibleControls = VisibleCaptionControls();", section)
            self.assertIn("if (visibleControls >= 1", section)
            self.assertIn("if (visibleControls >= 2", section)
            self.assertIn("if (visibleControls >= 3", section)
        self.assertIn("mouseX >= X && mouseX < X + Width", mouse)
        self.assertIn(
            "int controlsLeft = X + Width - RightPadding - visibleControls * ButtonSize;",
            mouse)
        self.assertIn(
            "if (!inTitle || (visibleControls > 0 && mouseX >= controlsLeft))",
            mouse)
        self.assertIn("int titleWidth = Width - visibleControls * ButtonSize - 28;", render)
        self.assertIn("if (Height >= TitleBarHeight && titleWidth > 0)", render)

    def test_tiny_height_never_exposes_caption_controls_or_draws_title_outside(self):
        helper = WINDOW.split("private int VisibleCaptionControls()", 1)[1].split(
            "public static void ConfigureDesktop(", 1)[0]
        render = WINDOW.split("public override void Render(Canvas canvas)", 1)[1].split(
            "private void DrawResizeHint(", 1)[0]
        self.assertIn("if (Height < TitleBarHeight)", helper)
        self.assertIn("if (Width >= 3 && Height >= TitleBarHeight)", render)
        self.assertIn("if (Width >= 3 && Height > TitleBarHeight)", render)
        self.assertIn("if (Height >= TitleBarHeight && titleWidth > 0)", render)
        self.assertIn("focusProgress > 0f && Width > 24 && Height >= 4", render)
        for width in (1, 24, 33, 34, 64, 93, 94, 320, 800, 1366, 1920):
            for height in range(1, 220):
                count = (0 if height < 38 or width - 4 < 30
                         else min(3, (width - 4) // 30))
                if height < 38:
                    self.assertEqual(count, 0)
                for i in range(count):
                    left = width - 4 - (i + 1) * 30
                    self.assertGreaterEqual(left, 0)
                    self.assertLessEqual(left + 30, width)

    def test_hidden_caption_cannot_start_drag_outside_short_window(self):
        mouse = WINDOW.split("public void HandleMouse(int mouseX", 1)[1].split(
            "private int GetResizeMode(", 1)[0]
        self.assertIn("bool inTitle = Height >= TitleBarHeight &&", mouse)
        self.assertIn("mouseY >= Y && mouseY < Y + TitleBarHeight && mouseY < Y + Height", mouse)
        for height in range(1, 100):
            for local_y in range(-2, 102):
                allowed = height >= 38 and 0 <= local_y < 38 and local_y < height
                if height < 38 or local_y < 0 or local_y >= 38:
                    self.assertFalse(allowed)
                elif height >= 38:
                    self.assertTrue(allowed)

    def test_all_caption_buttons_and_titles_fit_extreme_client_widths(self):
        # The rightmost Close button has priority. At tiny widths, hidden
        # Maximize/Minimize must not intercept clicks outside the window.
        for width in range(1, 4097):
            count = 0 if width - 4 < 30 else min(3, (width - 4) // 30)
            close = width - 4 - 30
            positions = [close - i * 30 for i in range(count)]
            self.assertGreaterEqual(count, 0)
            self.assertLessEqual(count, 3)
            for x in positions:
                self.assertGreaterEqual(x, 0)
                self.assertLessEqual(x + 30, width)
            controls_left = width - 4 - count * 30
            if count:
                self.assertEqual(min(positions), controls_left)
            title_width = width - count * 30 - 28
            if title_width > 0:
                self.assertLessEqual(12 + title_width, controls_left)
            # The three standard controls must remain unchanged at 800px,
            # 1366px and Full HD; only sub-94px windows collapse buttons.
            if width >= 94:
                self.assertEqual(count, 3)
        for width, expected in ((1, 0), (33, 0), (34, 1), (63, 1),
                                (64, 2), (93, 2), (94, 3), (320, 3),
                                (800, 3), (1366, 3), (1920, 3)):
            count = 0 if width - 4 < 30 else min(3, (width - 4) // 30)
            self.assertEqual(count, expected)



class TaskbarNotificationHitbox3095(unittest.TestCase):
    def test_notification_click_matches_visible_tile_not_entire_tray_height(self):
        render = TASKBAR.split("private void RenderSystemTray(", 1)[1].split(
            "private void DrawNotificationTile(", 1)[0]
        mouse = TASKBAR.split("public void UpdateInteractions(", 1)[1]
        self.assertIn("DrawNotificationTile(canvas, trayX + 9, 34);", render)
        self.assertIn("mouseX >= notificationX && mouseX < notificationX + 34", mouse)
        self.assertIn("mouseY >= Y + 6 && mouseY < Y + Height - 6", mouse)
        self.assertIn("if (Height > 12 &&", mouse)
        for height in range(1, 101):
            for local_y in range(-2, height + 3):
                hit = height > 12 and 6 <= local_y < height - 6
                if height <= 12 or local_y < 6 or local_y >= height - 6:
                    self.assertFalse(hit)
                else:
                    self.assertTrue(hit)



class PaintedControlHitbox3095(unittest.TestCase):
    def test_file_manager_toolbar_blank_vertical_chrome_cannot_navigate(self):
        mouse = FILE_MANAGER.split("public override void HandleMouse(int mouseX", 1)[1].split(
            "private void ResetClickSequence()", 1)[0]
        render = FILE_MANAGER.split("private void RenderToolbar(Canvas canvas)", 1)[1].split(
            "private void RenderSidebar(", 1)[0]
        self.assertIn("if (y >= 7 && y < 37)", mouse)
        self.assertIn("Toolbar(x);", mouse)
        self.assertIn("UiPainter.RoundedPanel(canvas, buttonX, Y + 7, 36, 30", FILE_MANAGER)
        self.assertIn("if (app.ToolbarButtonCountForView > 0)", render)
        for view_width in (44, 80, 86, 128, 170, 220, 300, 780, 1346, 1900):
            count = min(4, max(0, (view_width - 44) // 42 + 1))
            for y in range(0, 51):
                for x in range(0, view_width, max(1, view_width // 110)):
                    button = (x - 8) // 42 if x >= 8 else -1
                    painted = (7 <= y < 37 and 0 <= button < count
                               and (x - 8) % 42 < 36)
                    decoded = (4 <= y < 44 and 7 <= y < 37
                               and 0 <= button < count
                               and (x - 8) % 42 < 36)
                    self.assertEqual(painted, decoded, (view_width, x, y))

    def test_taskbar_app_and_pager_ignore_unpainted_top_bottom_border(self):
        render = TASKBAR.split("public override void Render(Canvas canvas)", 1)[1].split(
            "private void UpdateTrayCache()", 1)[0]
        mouse = TASKBAR.split("public void UpdateInteractions(", 1)[1]
        self.assertIn("UiPainter.RoundedPanel(canvas, appX, Y + 3, AppButtonSize, Height - 6", render)
        self.assertIn("UiPainter.RoundedPanel(canvas, arrowX, Y + 3, AppButtonSize, Height - 6", render)
        self.assertIn("if (mouseY < Y + 3 || mouseY >= Y + Height - 3)", mouse)
        self.assertLess(mouse.index("if (mouseY < Y + 3"), mouse.index("int appX = StartButtonWidth"))
        for height in range(13, 101):
            for y in range(-2, height + 3):
                painted = 3 <= y < height - 3
                hit = not (y < 3 or y >= height - 3)
                self.assertEqual(painted, hit, (height, y))

    def test_notification_empty_state_uses_visible_card_height(self):
        render = NOTIFICATIONS.split("private void RenderList(Canvas canvas)", 1)[1].split(
            "private void RenderFooter(", 1)[0]
        self.assertIn("UiPainter.RoundedPanel(canvas, cardX, cy, cardWidth, CardHeight,", render)
        self.assertNotIn("UiPainter.RoundedPanel(canvas, cardX, cy, cardWidth, 92,", render)
        # The last row must end before the reserved 72px footer gap,
        # including the empty state; previous 92px placeholder crossed it.
        for panel_height in range(203, 1081):
            available = max(0, panel_height - 130 - 72)
            rows = max(0, min(8, (available + 7) // (82 + 7)))
            for row in range(rows):
                top = 130 + row * (82 + 7)
                self.assertLessEqual(top + 82, panel_height - 72,
                                     (panel_height, row, rows))



class WindowInterruptedGesture3095(unittest.TestCase):
    def test_fresh_press_discards_drag_or_resize_after_modal_consumes_release(self):
        mouse = WINDOW.split("public void HandleMouse(int mouseX", 1)[1].split(
            "private int GetResizeMode(", 1)[0]
        guard = mouse.split("SyncDesktopBounds();", 1)[1].split(
            "int visibleControls = VisibleCaptionControls();", 1)[0]
        self.assertIn("if (isClicked && !wasClicked && (dragging || resizing))", guard)
        for reset in ("dragging = false;", "resizing = false;",
                      "resizeMode = 0;", "lastTitleClickTimestamp = 0;"):
            self.assertIn(reset, guard)
        self.assertLess(mouse.index("if (isClicked && !wasClicked && (dragging || resizing))"),
                        mouse.index("if (resizing)"))
        self.assertLess(mouse.index("if (isClicked && !wasClicked && (dragging || resizing))"),
                        mouse.index("if (dragging)"))

        # Model the event boundary: an overlay can swallow a release, but
        # the next fresh press must not continue the previous gesture.
        for dragging, resizing in ((False, False), (True, False), (False, True)):
            for is_clicked, was_clicked in ((False, True), (True, True), (True, False)):
                interrupted = is_clicked and not was_clicked and (dragging or resizing)
                remaining_drag = dragging and not interrupted
                remaining_resize = resizing and not interrupted
                if is_clicked and not was_clicked:
                    self.assertFalse(remaining_drag or remaining_resize)
                elif is_clicked and was_clicked:
                    self.assertEqual((remaining_drag, remaining_resize), (dragging, resizing))



class TinyStartMenu3095(unittest.TestCase):
    def test_tiny_search_results_never_overlap_power_footer(self):
        self.assertIn("private bool TinyLayout => Height < 400 || Width < 320;", START_MENU)
        self.assertIn("private void UpdateTinyLayout()", START_MENU)
        self.assertIn("private void RenderTiny(Canvas canvas)", START_MENU)
        self.assertIn("button.Visible = show;", START_MENU)
        self.assertIn("bool powerFits = Width >= 160", START_MENU)
        self.assertIn("SetPowerVisibility(powerFits);", START_MENU)
        self.assertIn("PlaceTinyPower(shutdownButton, 0", START_MENU)
        self.assertNotIn("Button[] power =", START_MENU)
        self.assertIn("if (button == null || !button.Visible)", START_MENU)

        for width, height in ((304, 180), (184, 140), (224, 180),
                              (304, 260), (480, 340)):
            cols = 3 if width >= 270 else 2 if width >= 160 else 1
            rows = max(0, (height - 48 - 70 - 6 + 4) // (42 + 4))
            button_width = max(1, (width - 28 - (cols - 1) * 6) // cols)
            for index in range(min(12, cols * rows)):
                x = 14 + (index % cols) * (button_width + 6)
                y = 70 + (index // cols) * (42 + 4)
                self.assertGreaterEqual(x, 14)
                self.assertLessEqual(x + button_width, width - 14)
                self.assertLessEqual(y + 42, height - 48)
            if width >= 160:
                power_width = max(1, (width - 28 - 18) // 4)
                leftmost = width - 14 - 4 * power_width - 18
                self.assertGreaterEqual(leftmost, 14)
                self.assertLessEqual(height - 48 + 6 + 36, height)

    def test_tiny_start_pages_match_filter_and_do_not_hit_power_controls(self):
        self.assertIn("private int tinyFirstResult;", START_MENU)
        self.assertIn("private int CountMatchingResults()", START_MENU)
        self.assertIn("private void ChangeTinyPage(int direction)", START_MENU)
        self.assertIn("matchIndex >= tinyFirstResult", START_MENU)
        self.assertIn("matchIndex < tinyFirstResult + capacity", START_MENU)
        self.assertIn("ConsoleKeyEx.PageUp || key.Key == ConsoleKeyEx.PageDown", START_MENU)
        self.assertIn("ChangeTinyPage(key.Key == ConsoleKeyEx.PageDown ? 1 : -1);",
                      START_MENU)
        self.assertIn("ChangeTinyPage(-1);", START_MENU)
        self.assertIn("ChangeTinyPage(1);", START_MENU)
        self.assertIn("tinyFirstResult = 0;", START_MENU)

        for capacity in (1, 2, 3, 4, 6, 9):
            for count in range(1, 14):
                pages = max(1, (count + capacity - 1) // capacity)
                first = 0
                seen = set()
                for _ in range(pages):
                    seen.update(range(first, min(first + capacity, count)))
                    first = ((first // capacity + 1) % pages) * capacity
                self.assertEqual(seen, set(range(count)))
                self.assertEqual(first, 0)

    def test_start_menu_consumes_press_after_first_action(self):
        handler = START_MENU.split("public void UpdateInteractions(", 1)[1].split(
            "private static bool UpdateButtonInteractions(", 1)[0]
        self.assertIn("if (UpdateButtonInteractions(pinnedButtons", handler)
        self.assertIn("if (UpdateButtonInteractions(toolButtons", handler)
        for action in ("lockButton", "exitButton", "rebootButton"):
            self.assertIn("if (UpdatePowerInteraction(" + action, handler)
        self.assertIn("return true;", START_MENU.split(
            "private static bool UpdateButtonInteractions(", 1)[1])
        self.assertIn("return true;", START_MENU.split(
            "private static bool UpdatePowerInteraction(", 1)[1])
        self.assertIn("GuiStartMenuSmoke", code(
            ".github/workflows/gui-responsive-3095.yml"))


if __name__ == "__main__":
    unittest.main(verbosity=2)
