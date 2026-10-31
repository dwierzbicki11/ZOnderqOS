#!/usr/bin/env python3
"""Source-level keyboard safety contracts; these do not execute a Cosmos guest."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


def source(path):
    return (ROOT / path).read_text(encoding="utf-8")


class ModifiedKeySafetyTests(unittest.TestCase):
    def test_file_manager_requires_plain_enter_for_all_actions(self):
        text = source("source/GUI/Application/FileManagerApp.cs")
        handler = text.split("public override void HandleKeyboard(KeyEvent key)", 1)[1].split(
            "private void CopySelected(bool cut)", 1
        )[0]
        # Dialog creation, context-menu action, and opening the selected entry.
        self.assertEqual(
            handler.count("if (key.Key == ConsoleKeyEx.Enter && key.Modifiers == 0)"), 3
        )
        self.assertNotIn("if (key.Key == ConsoleKeyEx.Enter)", handler)
        self.assertIn("if (key.KeyChar != '\\0' && !char.IsControl(key.KeyChar)", handler)
        # Shift is still available for typing uppercase filenames and filters.
        self.assertIn("ConsoleModifiers.Control | ConsoleModifiers.Alt", handler)

    def test_task_manager_requires_plain_delete_to_end_task(self):
        text = source("source/GUI/Application/TaskManagerModernApp.cs")
        handler = text.split("public override void HandleKeyboard(KeyEvent key)", 1)[1].split(
            "private void SetPage(int page)", 1
        )[0]
        self.assertIn(
            "else if (key.Key == ConsoleKeyEx.Delete && key.Modifiers == 0) EndSelectedTask();",
            handler,
        )
        self.assertNotIn("else if (key.Key == ConsoleKeyEx.Delete) EndSelectedTask();", handler)
        self.assertIn("if (key.Key == ConsoleKeyEx.PageDown)", handler)

    def test_notification_center_requires_plain_delete(self):
        text = source("source/GUI/NotificationCenter.cs")
        handler = text.split("public bool HandleKeyboard(KeyEvent key)", 1)[1].split(
            "public bool UpdateInteractions(", 1
        )[0]
        deletion = handler.split("if (key.Key == ConsoleKeyEx.Delete)", 1)[1].split(
            "// Only unmodified letters", 1
        )[0]
        self.assertIn("if (key.Modifiers == 0)", deletion)
        self.assertIn("DismissSelected();", deletion)
        self.assertLess(deletion.index("if (key.Modifiers == 0)"), deletion.index("DismissSelected();"))
        self.assertIn("return true;", deletion)

    def test_modifier_policy_for_destructive_actions(self):
        # Independent model of the intended key predicate, not a runtime test.
        for ctrl in (False, True):
            for alt in (False, True):
                for shift in (False, True):
                    modifiers = (1 if ctrl else 0) | (2 if alt else 0) | (4 if shift else 0)
                    self.assertEqual(modifiers == 0, not (ctrl or alt or shift))


if __name__ == "__main__":
    unittest.main(verbosity=2)
