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


if __name__ == "__main__":
    unittest.main(verbosity=2)
