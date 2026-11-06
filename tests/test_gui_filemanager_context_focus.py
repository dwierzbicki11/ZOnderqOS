"""Regression contract: file navigation dismisses stale context-menu focus."""
from pathlib import Path
import unittest

SOURCE = Path(__file__).resolve().parents[1] / "source/GUI/Application/FileManagerApp.cs"


class FileManagerContextFocusTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = SOURCE.read_text(encoding="utf-8")

    def method(self, name, next_name):
        start = self.source.index("private void " + name + "(")
        end = self.source.index("private void " + next_name + "(", start)
        return self.source[start:end]

    def test_home_end_clears_menu_focus(self):
        body = self.method("SelectBoundary", "SelectPage")
        self.assertIn("contextMenuVisible = false;\n            contextMenuSelection = -1;", body)
        self.assertLess(body.index("contextMenuSelection = -1;"),
                        body.index("selectedIndex = last ?"))

    def test_arrow_and_page_navigation_clear_menu_focus(self):
        body = self.method("Select", "EnsureSelectionVisible")
        self.assertIn("contextMenuVisible = false;\n            contextMenuSelection = -1;", body)
        self.assertLess(body.index("contextMenuSelection = -1;"),
                        body.index("if (selectedIndex < 0)"))


if __name__ == "__main__":
    unittest.main()
