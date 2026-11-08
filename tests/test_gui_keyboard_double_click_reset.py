"""Source contract: keyboard tile navigation must cancel a pending mouse double-click."""
from pathlib import Path
import unittest

SOURCE = Path(__file__).resolve().parents[1] / "source/GUI/Application/FileManagerApp.cs"


class KeyboardDoubleClickResetTests(unittest.TestCase):
    def test_navigation_resets_mouse_sequence_before_selection(self):
        source = SOURCE.read_text(encoding="utf-8")
        start = source.index("public override void HandleKeyboard(KeyEvent key)")
        end = source.index("private void CopySelected(bool cut)", start)
        body = source[start:end]
        guard = body.index("if (key.Key == ConsoleKeyEx.LeftArrow ||")
        reset = body.index("ResetClickSequence();", guard)
        first_navigation = body.index("if (key.Key == ConsoleKeyEx.LeftArrow)", reset)
        self.assertLess(guard, reset)
        self.assertLess(reset, first_navigation)
        for key in ("LeftArrow", "RightArrow", "UpArrow", "DownArrow",
                    "Home", "End", "PageUp", "PageDown"):
            self.assertIn("key.Key == ConsoleKeyEx." + key, body[guard:reset])


if __name__ == "__main__":
    unittest.main()
