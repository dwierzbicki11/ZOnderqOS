#!/usr/bin/env python3
"""Source-level safety contract for File Manager when no tile is paintable.

This does NOT execute the Cosmos GUI or establish QEMU input behavior.
"""
from pathlib import Path
import unittest

SOURCE = (Path(__file__).resolve().parents[1] /
          "source/GUI/Application/FileManagerApp.cs").read_text(encoding="utf-8")


def section(start, end):
    return SOURCE.split(start, 1)[1].split(end, 1)[0]


def visible_rows(client_width, client_height):
    sidebar = (0 if client_width < 220 else
               min(176, client_width - 112 - 12))
    grid_width = max(0, client_width - sidebar - 12)
    grid_height = max(0, client_height - 24 - 50 - 8)
    if grid_width <= 0 or grid_height < 40:
        return 0
    return max(1, (grid_height + 94 + 6 - 40) // 100)


class InvisibleSelectionContract(unittest.TestCase):
    def test_no_painted_rows_clears_selection_and_modal_actions(self):
        body = section("private void EnsureSelectionVisible()",
                       "private void GoUp()")
        zero_rows = body.split("if (rows == 0)", 1)[1].split(
            "int visible = cols * rows;", 1)[0]
        for operation in ("selectedIndex = -1;", "scrollIndex = 0;",
                          "contextMenuVisible = false;",
                          "contextMenuSelection = -1;", "ResetClickSequence();"):
            self.assertIn(operation, zero_rows)
        self.assertLess(zero_rows.index("selectedIndex = -1;"),
                        zero_rows.index("return;"))

    def test_keyboard_reflows_before_processing_shortcuts(self):
        body = section("public override void HandleKeyboard(KeyEvent key)",
                       "private void CopySelected(bool cut)")
        self.assertLess(body.index("SyncViewGeometry();"),
                        body.index("bool modified ="))
        self.assertLess(body.index("SyncViewGeometry();"),
                        body.index("CopySelected(false);"))
        self.assertLess(body.index("SyncViewGeometry();"),
                        body.index("OpenEntry(selectedIndex);"))

    def test_window_bounds_sync_precedes_file_manager_keyboard_geometry(self):
        geometry = section("private void SyncViewGeometry()",
                           "public override void HandleMouse(int x, int y")
        self.assertIn("Window.EnsureDesktopBounds();", geometry)
        self.assertLess(geometry.index("int previousViewX = view.X;"),
                        geometry.index("Window.EnsureDesktopBounds();"))
        self.assertLess(geometry.index("Window.EnsureDesktopBounds();"),
                        geometry.index("view.Width = Math.Max(1, Window.Width - 20);"))
        self.assertLess(geometry.index("Window.EnsureDesktopBounds();"),
                        geometry.index("if (view.X != previousViewX || view.Y != previousViewY)"))
        window = (Path(__file__).resolve().parents[1] /
                  "source/GUI/Window.cs").read_text(encoding="utf-8")
        public_sync = window.split("public void EnsureDesktopBounds()", 1)[1].split(
            "public Window(", 1
        )[0]
        self.assertIn("SyncDesktopBounds();", public_sync)

    def test_invisible_files_cannot_be_selected_or_opened(self):
        for start, end in (
            ("private void SelectBoundary(bool last)", "private void SelectPage("),
            ("private void Select(int delta)", "private void EnsureSelectionVisible()"),
            ("private void OpenEntry(int index)", "private void BeginCreate("),
        ):
            self.assertIn("GridRowsForView == 0", section(start, end))
        self.assertIn("GridRowsForView == 0",
                      section("private int HitEntryIndex(", "private bool ContextClick("))
        clipboard = section("private void CopySelected(bool cut)",
                            "private void PasteClipboard()")
        self.assertIn("if (GridRowsForView == 0)", clipboard)
        self.assertLess(clipboard.index("if (GridRowsForView == 0)"),
                        clipboard.index("FileClipboard.Set("))

    def test_resize_cancels_filename_editor_when_input_cannot_be_drawn(self):
        body = section("private void SyncViewGeometry()",
                       "public override void HandleMouse(int x, int y")
        guard = body.split("if (dialogMode != 0 && (view.Width < 64 || view.Height < 66))",
                           1)[1].split("// The menu may have been opened", 1)[0]
        self.assertIn("dialogMode = 0;", guard)
        self.assertIn('dialogName = "";', guard)
        self.assertIn("ResetClickSequence();", guard)
        # Production Dialog() only paints the input for a 48px-wide
        # dialog with an 18px-high field; it reserves 16px of client inset.
        for width in range(1, 100):
            for height in range(1, 120):
                dialog_width = min(430, max(0, width - 16))
                dialog_height = min(126, max(0, height - 16))
                offset = min(42, max(24, dialog_height - 40))
                field_height = min(30, max(0, dialog_height - offset - 8))
                painted = dialog_width >= 48 and field_height >= 18
                if width >= 64 and height >= 66:
                    self.assertTrue(painted)

    def test_paste_is_blocked_before_any_filesystem_mutation(self):
        body = section("private void PasteClipboard()",
                       "private int Columns()")
        self.assertIn("if (GridRowsForView == 0)", body)
        self.assertLess(body.index("if (GridRowsForView == 0)"),
                        body.index("FileClipboard.TryPaste("))

    def test_geometry_model_has_zero_rows_for_tiny_clients(self):
        for width, height in ((180, 80), (200, 90), (300, 110)):
            self.assertEqual(visible_rows(width, height), 0)
        for width, height in ((300, 122), (780, 470),
                              (620, 430), (1346, 718), (1900, 1030)):
            self.assertGreater(visible_rows(width, height), 0)
        # Model boundary sweep: there is no hidden row when the remaining
        # painted grid is shorter than the compact 40px tile.
        for width in (1, 50, 180, 219, 220, 300, 620, 780, 1346, 1900):
            for height in range(1, 160):
                grid_height = max(0, height - 82)
                if grid_height < 40:
                    self.assertEqual(visible_rows(width, height), 0)


    def test_compact_menu_disables_mutations_without_visible_destination(self):
        actions = section("private bool ContextActionEnabled(int item)",
                          "private void ExecuteContextAction(int item)")
        self.assertIn("if (GridRowsForView == 0 && item >= 0 && item <= 4)", actions)
        self.assertLess(actions.index("GridRowsForView == 0"),
                        actions.index("return FileClipboard.HasItem;"))
        self.assertIn("return item >= 3 && item < ContextMenuItemCount;", actions)

        # At 99-121px of client height the compact context menu fits,
        # but the first file tile (40px minimum) does not.
        for height in range(99, 122):
            self.assertEqual(visible_rows(300, height), 0)
        self.assertGreater(visible_rows(300, 122), 0)

    def test_creation_entrypoints_reject_hidden_grid(self):
        begin = section("private void BeginCreate(int mode)",
                        "private void FinishCreate()")
        finish = section("private void FinishCreate()",
                         "private void RefreshAll()")
        for body in (begin, finish):
            self.assertIn("if (GridRowsForView == 0)", body)
        self.assertLess(begin.index("if (GridRowsForView == 0)"),
                        begin.index("dialogMode = mode;"))
        self.assertLess(finish.index("if (GridRowsForView == 0)"),
                        finish.index("FileManagerFileCreation.CreateEmptyFile(path)"))
        self.assertIn("dialogMode = 0;", finish)
        self.assertIn('dialogName = "";', finish)

        geometry = section("private void SyncViewGeometry()",
                           "public override void HandleMouse(int x, int y")
        self.assertIn("if (dialogMode != 0 && GridRowsForView == 0)", geometry)
        cancel = geometry.split(
            "if (dialogMode != 0 && GridRowsForView == 0)", 1
        )[1].split("if (contextMenuVisible && !CanShowContextMenu)", 1)[0]
        self.assertIn("dialogMode = 0;", cancel)
        self.assertIn('dialogName = "";', cancel)
        self.assertIn("ResetClickSequence();", cancel)

    def test_right_left_chord_cannot_activate_volume_or_file_actions(self):
        mouse = section(
            "public override void HandleMouse(int mouseX, int mouseY, bool left, bool oldLeft, bool right, bool oldRight)",
            "private void ResetClickSequence()")
        guard = mouse.split("if (right && left)", 1)[1].split(
            "Window.HandleMouse(mouseX, mouseY, left, oldLeft);", 1)[0]
        self.assertIn("ResetClickSequence();", guard)
        self.assertIn("return;", guard)
        self.assertLess(mouse.index("if (right && left)"),
                        mouse.index("Window.HandleMouse(mouseX, mouseY, left, oldLeft);"))
        self.assertLess(mouse.index("if (right && left)"),
                        mouse.index("SidebarClick(x, y);"))
        self.assertLess(mouse.index("if (right && left)"),
                        mouse.index("Toolbar(x);"))
        # Source-level ordering only; a QEMU HID chord test is still needed.
        self.assertLess(mouse.index("if (right && left)"),
                        mouse.index("if (right && !oldRight)"))

    def test_context_menu_paint_matches_disabled_action_policy(self):
        menu = section("private void Menu(Canvas canvas)",
                       "private void DrawMenuItem(Canvas canvas")
        self.assertIn("bool canEdit = app.GridRowsForView > 0;", menu)
        self.assertIn("canEdit && FileClipboard.HasItem", menu)
        self.assertIn('"New file", "", x, y + 3 * rowHeight, canEdit, 3', menu)
        self.assertIn('"New folder", "", x, y + 4 * rowHeight, canEdit, 4', menu)
        # Refresh and Go up remain usable at tiny resolutions.
        self.assertIn('"Refresh", "F5", x, y + 5 * rowHeight, true, 5', menu)
        self.assertIn('"Go up", "Backspace", x, y + 6 * rowHeight, true, 6', menu)


if __name__ == "__main__":
    unittest.main(verbosity=2)
