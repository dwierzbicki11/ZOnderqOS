"""Contract checks for context-menu double-click invalidation in Gen3 GUI.

Run: python3 -m unittest tests/test_gui_context_double_click_invalidation.py
"""
import pathlib
import re
import unittest

SOURCE = pathlib.Path(__file__).resolve().parents[1] / "source/GUI/Application/FileManagerApp.cs"


def method_body(source, signature):
    start = source.index(signature)
    brace = source.index("{", start)
    depth = 0
    for i in range(brace, len(source)):
        if source[i] == "{":
            depth += 1
        elif source[i] == "}":
            depth -= 1
            if depth == 0:
                return source[brace + 1:i]
    raise AssertionError("Unterminated method: " + signature)


class ContextDoubleClickInvalidation(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = SOURCE.read_text(encoding="utf-8")

    def test_context_action_resets_before_dispatch(self):
        body = method_body(self.source, "private void ExecuteContextAction(int item)")
        reset = body.index("ResetClickSequence();")
        guard = body.index("if (!ContextActionEnabled(item))")
        dispatch = body.index("if (item == 0)")
        self.assertLess(reset, guard)
        self.assertLess(reset, dispatch)
        self.assertLess(body.index("contextMenuVisible = false;"), reset)

    def test_keyboard_escape_cancels_context_click_anchor(self):
        body = method_body(self.source, "public override void HandleKeyboard(KeyEvent key)")
        menu = body.index("if (contextMenuVisible)")
        escape = body.index("if (key.Key == ConsoleKeyEx.Escape)", menu)
        end = body.index("return;", escape)
        block = body[escape:end]
        self.assertIn("contextMenuVisible = false;", block)
        self.assertIn("contextMenuSelection = -1;", block)
        self.assertIn("ResetClickSequence();", block)

    def test_context_enter_uses_shared_action_dispatch(self):
        body = method_body(self.source, "public override void HandleKeyboard(KeyEvent key)")
        menu = body.index("if (contextMenuVisible)")
        self.assertIn("ExecuteContextAction(contextMenuSelection);", body[menu:])


if __name__ == "__main__":
    unittest.main()
