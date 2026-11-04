"""Regression contract for context-menu keyboard focus in File Manager."""
from pathlib import Path
import unittest

SOURCE = Path(__file__).resolve().parents[1] / "source/GUI/Application/FileManagerApp.cs"


class FileManagerContextFocusTests(unittest.TestCase):
    def test_background_window_discards_hidden_context_selection(self):
        source = SOURCE.read_text(encoding="utf-8")
        update = source.split("public override void Update()", 1)[1].split("private void SyncViewGeometry()", 1)[0]
        inactive = update.split("if (!Window.IsActive)", 1)[1]
        self.assertIn("ResetClickSequence();", inactive)
        self.assertIn("contextMenuVisible = false;", inactive)
        self.assertIn("contextMenuSelection = -1;", inactive)
        self.assertLess(inactive.index("contextMenuVisible = false;"),
                        inactive.index("contextMenuSelection = -1;"))


if __name__ == "__main__":
    unittest.main()
