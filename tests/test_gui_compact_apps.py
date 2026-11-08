#!/usr/bin/env python3
"""Host-only geometry and modal-input regression checks for compact GUI apps.

These checks model pixel bounds and inspect the production C# integration.
They do NOT prove a rendered QEMU framebuffer or working input devices.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
CALCULATOR = (ROOT / "source/GUI/Application/CalculatorApp.cs").read_text(encoding="utf-8")
NOTEPAD = (ROOT / "source/GUI/Application/NotepadApp.cs").read_text(encoding="utf-8")
ABOUT = (ROOT / "source/GUI/Application/AboutApp.cs").read_text(encoding="utf-8")


def fit_window(requested_width, requested_height, screen_width, screen_height):
    desktop_height = max(1, screen_height - 44)
    margin = 12 if screen_width >= 1000 and desktop_height >= 700 else 0
    available_width = max(1, screen_width - margin * 2)
    available_height = max(1, desktop_height - margin * 2)
    width = max(min(320, available_width), min(requested_width, available_width))
    height = max(min(220, available_height), min(requested_height, available_height))
    return width, height


class CompactCalculatorTests(unittest.TestCase):
    def test_grid_is_painted_and_clicked_only_when_it_fits(self):
        self.assertIn("private bool HasButtonGrid => Window.Width >= 280 && Window.Height >= MinGridHeight;", CALCULATOR)
        self.assertIn("if (!HasButtonGrid)\n                return -1;", CALCULATOR)
        self.assertIn("if (!HasButtonGrid)\n            {", CALCULATOR)
        self.assertIn("private const int MinGridHeight = GridTopOffset + 7 * 18 + 6 * ButtonGap + 12;", CALCULATOR)
        self.assertIn("buttonWidth = System.Math.Max(1, availableWidth / Columns);", CALCULATOR)
        self.assertIn("buttonHeight = System.Math.Max(18, System.Math.Min(58,", CALCULATOR)

        for screen_width, screen_height in (
            (200, 200), (320, 240), (640, 480), (800, 600),
            (1366, 768), (1920, 1080),
        ):
            for requested_width, requested_height in (
                (500, 690), (320, 220), (800, 400), (200, 200),
            ):
                width, height = fit_window(requested_width, requested_height,
                                           screen_width, screen_height)
                if width > 24 and height > 55:
                    self.assertLessEqual(12 + max(1, width - 24), width)
                    self.assertLessEqual(43 + max(1, height - 55), height)
                has_grid = width >= 280 and height >= 416
                if has_grid:
                    key_width = max(1, (width - 32 - 7 * 3) // 4)
                    key_height = max(18, min(58, (height - 236 - 16 - 7 * 6) // 7))
                    self.assertLessEqual(16 + 4 * key_width + 3 * 7, width)
                    self.assertLessEqual(236 + 7 * key_height + 6 * 7, height)
                if height < 245:
                    self.assertFalse(has_grid)

    def test_compact_readout_does_not_render_full_height_display(self):
        self.assertIn("if (Window.Height < 245)", CALCULATOR)
        self.assertIn("RenderHistory(canvas, contentX, contentY, contentWidth);", CALCULATOR)
        self.assertIn("RenderDisplay(canvas, contentX, contentY, contentWidth);", CALCULATOR)
        self.assertLess(CALCULATOR.index("if (Window.Height < 245)"),
                        CALCULATOR.index("RenderDisplay(canvas, contentX, contentY, contentWidth);"))
        self.assertIn("int contentWidth = System.Math.Max(1, Window.Width - 24);", CALCULATOR)


class CompactNotepadTests(unittest.TestCase):
    def test_editor_view_fits_parent_at_every_supported_mode(self):
        self.assertIn("view.Width = Math.Max(1, Window.Width - 20);", NOTEPAD)
        self.assertIn("view.Height = Math.Max(1, Window.Height - 50);", NOTEPAD)
        for sw, sh in ((320, 240), (640, 480), (800, 600),
                       (1366, 768), (1920, 1080)):
            for requested_width, requested_height in ((980, 650), (320, 220)):
                width, height = fit_window(requested_width, requested_height, sw, sh)
                view_width = max(1, width - 20)
                view_height = max(1, height - 50)
                self.assertLessEqual(10 + view_width, width)
                self.assertLessEqual(40 + view_height, height)

    def test_file_picker_is_not_launched_or_hit_outside_compact_client(self):
        self.assertIn('if (view.Width < 360 || view.Height < 310)', NOTEPAD)
        self.assertIn('status = "Enlarge window to open a file";', NOTEPAD)
        self.assertIn('if (app.DialogMode != 1 || Width < 360 || Height < 310)', NOTEPAD)
        self.assertIn('if (Width < 360 || Height < 310)', NOTEPAD)
        picker_keys = NOTEPAD.split("private void HandleOpenDialogKeyboard(KeyEvent key)", 1)[1].split(
            "private void HandleOpenDialogMouse(", 1
        )[0]
        self.assertLess(picker_keys.index("UpdateLayout();"),
                        picker_keys.index("if (view.Width < 360 || view.Height < 310)"))
        self.assertLess(picker_keys.index("if (view.Width < 360 || view.Height < 310)"),
                        picker_keys.index("if (key.Key == ConsoleKeyEx.Backspace"))
        for width in (180, 300, 360, 400, 620, 960):
            for height in (106, 146, 170, 310, 386, 595):
                can_show = width >= 360 and height >= 310
                if can_show:
                    dialog_width = min(640, max(360, width - 80))
                    dialog_height = min(460, max(310, height - 70))
                    self.assertLessEqual(dialog_width, width)
                    self.assertLessEqual(dialog_height, height)

    def test_hidden_toolbar_buttons_have_no_click_targets(self):
        self.assertIn("if (Width >= 70)", NOTEPAD)
        self.assertIn("if (Width >= 140)", NOTEPAD)
        self.assertIn("if (Width >= 210)", NOTEPAD)
        self.assertIn("if (Width >= 298)", NOTEPAD)
        self.assertIn("if (Width >= 298 && x >= 210 && x < 294) return 4;", NOTEPAD)

    def test_modal_dialog_blocks_background_toolbar_and_stays_in_bounds(self):
        handler = NOTEPAD.split(
            "public override void HandleMouse(int mouseX, int mouseY, bool left, bool oldLeft)", 1
        )[1].split("public override void Render(Canvas canvas)", 1)[0]
        self.assertLess(handler.index("if (dialogMode != 0)\n                return;"),
                        handler.index("int action = view.ToolbarActionAt(x, y);"))
        self.assertIn("if (Width < 280 || Height < requiredHeight)", NOTEPAD)
        self.assertIn("Math.Max(1, Width - 12), Math.Max(1, Height - 12)", NOTEPAD)
        self.assertIn("if (app.DialogMode == 2 && Width >= 100 && Height >= 110)", NOTEPAD)
        self.assertIn('SmallTextRenderer.DrawClipped(canvas, app.DialogText ?? ""', NOTEPAD)
        self.assertIn("if (view.Width < 100 || view.Height < 110)", NOTEPAD)
        self.assertIn('status = "Enlarge window to confirm save path";', NOTEPAD)

class CompactAboutTests(unittest.TestCase):
    def test_about_panel_rows_do_not_exceed_client_height(self):
        self.assertIn('"WERSJA " + curVer + "  /  COSMOS SDK 3.0.95 X64"', ABOUT)
        self.assertIn("curVer = global::ZonderqOS.SystemCore.Updates.UpdateManager.CurrentVersion", ABOUT)
        self.assertIn("curBuild = global::ZonderqOS.SystemCore.Updates.UpdateManager.CurrentBuild", ABOUT)
        self.assertIn('"KOMPILACJA: BUILD " + curBuild', ABOUT)
        self.assertNotIn("SDK 3.0.94", ABOUT)
        for threshold in (64, 80, 110, 140, 168):
            self.assertIn(f"height >= {threshold}", ABOUT)
        for screen_width, screen_height in (
            (320, 240), (640, 480), (800, 600),
            (1366, 768), (1920, 1080),
        ):
            _, window_height = fit_window(560, 300, screen_width, screen_height)
            panel_height = max(1, window_height - 72)
            visible_lines = [offset for threshold, offset in
                             ((110, 96), (140, 124), (168, 152))
                             if panel_height >= threshold]
            for offset in visible_lines:
                self.assertLess(offset + 14, panel_height + 1)


if __name__ == "__main__":
    unittest.main(verbosity=2)
