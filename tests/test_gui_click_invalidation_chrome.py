"""Regression contracts for File Manager double-click invalidation.

These static checks do not replace a Cosmos build or an interactive QEMU test.
"""
from pathlib import Path
import re
import unittest

SOURCE = Path(__file__).resolve().parents[1] / "source/GUI/Application/FileManagerApp.cs"


def method_body(source, signature):
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 0
    for index in range(opening, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[opening + 1:index]
    raise AssertionError("Unbalanced method body")


class FileManagerClickInvalidationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = SOURCE.read_text(encoding="utf-8")

    def test_blank_toolbar_press_clears_click_anchor(self):
        body = method_body(self.source, "public override void HandleMouse(int mouseX")
        toolbar = body.index("if (y >= ToolbarTop && y < ToolbarTop + ToolbarHeight)")
        reset = body.index("ResetClickSequence();", toolbar)
        button_gate = body.index("if (y >= 7 && y < 37)", toolbar)
        early_return = body.index("return;", toolbar)
        self.assertLess(reset, button_gate)
        self.assertLess(reset, early_return)

    def test_dialog_escape_clears_click_anchor(self):
        body = method_body(self.source, "public override void HandleKeyboard(KeyEvent key)")
        dialog = body.index("if (dialogMode != 0)")
        escape = body.index("if (key.Key == ConsoleKeyEx.Escape)", dialog)
        reset = body.index("ResetClickSequence();", escape)
        early_return = body.index("return;", escape)
        self.assertLess(reset, early_return)
        self.assertIn('dialogName = "";', body[escape:reset])


if __name__ == "__main__":
    unittest.main()
