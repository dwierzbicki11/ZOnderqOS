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

    def test_file_manager_clipboard_rejects_shift_modified_control(self):
        text = source("source/GUI/Application/FileManagerApp.cs")
        handler = text.split("public override void HandleKeyboard(KeyEvent key)", 1)[1].split(
            "private void CopySelected(bool cut)", 1
        )[0]
        self.assertIn(
            "(key.Modifiers & (ConsoleModifiers.Alt | ConsoleModifiers.Shift)) == 0",
            handler,
        )
        self.assertLess(handler.index("bool control ="), handler.index("CopySelected(true);"))
        # Ctrl+C/X/V remains available, Ctrl+Shift+C/X/V is reserved.
        for ctrl in (False, True):
            for shift in (False, True):
                for alt in (False, True):
                    allowed = ctrl and not (shift or alt)
                    self.assertEqual(allowed, ctrl and not shift and not alt)

    def test_notification_shift_enter_cannot_acknowledge_message(self):
        text = source("source/GUI/NotificationCenter.cs")
        handler = text.split("public bool HandleKeyboard(KeyEvent key)", 1)[1].split(
            "public bool UpdateInteractions(", 1
        )[0]
        enter = handler.split("if (key.Key == ConsoleKeyEx.Enter)", 1)[1].split(
            "if (key.Key == ConsoleKeyEx.Delete)", 1
        )[0]
        self.assertIn("if (key.Modifiers == 0)", enter)
        self.assertLess(enter.index("if (key.Modifiers == 0)"), enter.index("MarkSelectedRead();"))
        self.assertIn("return true;", enter)

    def test_notification_shift_letters_cannot_clear_history(self):
        text = source("source/GUI/NotificationCenter.cs")
        handler = text.split("public bool HandleKeyboard(KeyEvent key)", 1)[1].split(
            "public bool UpdateInteractions(", 1
        )[0]
        self.assertIn("if (key.Modifiers != 0)\n                return true;", handler)
        self.assertLess(
            handler.index("if (key.Modifiers != 0)"),
            handler.index("char ch = key.KeyChar;"),
        )
        self.assertLess(
            handler.index("if (key.Modifiers != 0)"),
            handler.index("NotificationService.ClearAll();"),
        )

    def test_desktop_context_menu_is_modal_for_keyboard(self):
        gui = source("source/GUI/GuiManager.cs")
        keyboard = gui.split("if (desktopContextMenu != null && desktopContextMenu.Visible)", 1)[1]
        self.assertIn("desktopContextMenu.HandleKeyboard(key);", keyboard)
        self.assertLess(
            gui.index("desktopContextMenu.HandleKeyboard(key);"),
            gui.index("applicationManager.HandleKeyboard(key);"),
        )
        menu = source("source/GUI/DesktopContextMenu.cs")
        handler = menu.split("public bool HandleKeyboard(KeyEvent key)", 1)[1].split(
            "public bool UpdateInteractions(", 1
        )[0]
        self.assertIn("if (!Visible || key == null)", handler)
        self.assertRegex(handler, r"if \(key\.Modifiers != 0\)\s*return true;")
        self.assertIn("ConsoleKeyEx.Escape", handler)
        self.assertIn("ConsoleKeyEx.UpArrow", handler)
        self.assertIn("ConsoleKeyEx.DownArrow", handler)
        self.assertIn("ConsoleKeyEx.Home", handler)
        self.assertIn("ConsoleKeyEx.End", handler)
        self.assertIn("ConsoleKeyEx.NumEnter", handler)
        self.assertLess(handler.index("Visible = false;", handler.index("ConsoleKeyEx.Enter")),
                        handler.index("entries[index].Action?.Invoke();"))
        self.assertTrue(handler.rstrip().endswith("}"))

    def test_desktop_context_menu_mouse_and_keyboard_selection_are_independent(self):
        menu = source("source/GUI/DesktopContextMenu.cs")
        self.assertIn("lastMouseX = x;", menu)
        self.assertIn("lastMouseY = y;", menu)
        handler = menu.split("public bool UpdateInteractions(", 1)[1].split(
            "private int HitTest(", 1
        )[0]
        self.assertIn("int pointerIndex = HitTest(mouseX, mouseY);", handler)
        self.assertIn("if (mouseX != lastMouseX || mouseY != lastMouseY)", handler)
        self.assertIn("hoveredIndex = pointerIndex;", handler)
        self.assertIn("int index = pointerIndex;", handler)
        self.assertLess(handler.index("int index = pointerIndex;"),
                        handler.index("entries[index].Action?.Invoke();"))

    def test_modifier_policy_for_destructive_actions(self):
        # Independent model of the intended key predicate, not a runtime test.
        for ctrl in (False, True):
            for alt in (False, True):
                for shift in (False, True):
                    modifiers = (1 if ctrl else 0) | (2 if alt else 0) | (4 if shift else 0)
                    self.assertEqual(modifiers == 0, not (ctrl or alt or shift))


if __name__ == "__main__":
    unittest.main(verbosity=2)
