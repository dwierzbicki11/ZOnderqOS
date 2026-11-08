"""Static regression checks for resize-dismissed File Manager context menus.

Run: python3 -m unittest tests/test_gui_context_resize_dismiss.py
"""
import pathlib
import unittest

SOURCE = pathlib.Path(__file__).resolve().parents[1] / "source/GUI/Application/FileManagerApp.cs"


class ContextResizeDismiss(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = SOURCE.read_text(encoding="utf-8")

    def test_resize_dismiss_cancels_pending_double_click(self):
        marker = "if (contextMenuVisible && !CanShowContextMenu)"
        self.assertEqual(self.source.count(marker), 1)
        block = self.source.split(marker, 1)[1].split("}", 1)[0]
        self.assertLess(block.index("contextMenuVisible = false;"),
                        block.index("ResetClickSequence();"))
        self.assertIn("contextMenuSelection = -1;", block)

    def test_escape_context_menu_has_single_modal_path(self):
        start = self.source.index("public override void HandleKeyboard(KeyEvent key)")
        body = self.source[start:]
        self.assertIn("if (contextMenuVisible)", body)
        self.assertIn("ResetClickSequence();", body)
        self.assertNotIn(
            "if (key.Key == ConsoleKeyEx.Escape)\n            {\n                if (contextMenuVisible)",
            body,
        )


if __name__ == "__main__":
    unittest.main()
