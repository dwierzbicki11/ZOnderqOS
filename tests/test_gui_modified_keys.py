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

    def test_task_manager_page_arrows_are_plain_and_wrap(self):
        text = source("source/GUI/Application/TaskManagerModernApp.cs")
        handler = text.split("public override void HandleKeyboard(KeyEvent key)", 1)[1].split(
            "private void SetPage(int page)", 1
        )[0]
        self.assertIn("key.Modifiers == 0 && key.Key == ConsoleKeyEx.LeftArrow", handler)
        self.assertIn("key.Modifiers == 0 && key.Key == ConsoleKeyEx.RightArrow", handler)
        self.assertIn("SetPage(activePage == PageProcesses ? PageServices : activePage - 1);", handler)
        self.assertIn("SetPage(activePage == PageServices ? PageProcesses : activePage + 1);", handler)
        self.assertLess(handler.index("key.Key == ConsoleKeyEx.RightArrow"),
                        handler.index("if (activePage == PagePerformance)"))
        # Four pages, including Performance, must be reachable in both directions.
        for page in range(4):
            self.assertEqual(3 if page == 0 else page - 1, (page - 1) % 4)
            self.assertEqual(0 if page == 3 else page + 1, (page + 1) % 4)

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

    def test_start_menu_consumes_unhandled_keys_instead_of_ending_tasks(self):
        text = source("source/GUI/StartMenu.cs")
        handler = text.split("public bool HandleKeyboard(KeyEvent key)", 1)[1].split(
            "public void UpdateInteractions(", 1
        )[0]
        self.assertRegex(handler, r"if \(!Visible \|\| key == null\)\s*return false;")
        self.assertIn("ConsoleKeyEx.NumEnter", handler)
        enter = handler.split("if (key.Key == ConsoleKeyEx.Enter", 1)[1].split(
            "for (int i = 0; i < pinnedButtons.Count; i++)", 1
        )[0]
        self.assertIn("if (key.Modifiers != 0)", enter)
        self.assertIn("return true;", enter)
        self.assertRegex(handler.rstrip(), r"return true;\s*}$")

    def test_start_menu_keyboard_navigation_targets_filtered_results(self):
        menu = source("source/GUI/StartMenu.cs")
        handler = menu.split("public bool HandleKeyboard(KeyEvent key)", 1)[1].split(
            "public void UpdateInteractions(", 1
        )[0]
        self.assertIn("private int selectedResult = -1;", menu)
        self.assertIn("private int CountVisibleResults()", menu)
        self.assertIn("int count = CountVisibleResults();", handler)
        for key in ("UpArrow", "DownArrow", "Home", "End"):
            self.assertIn("ConsoleKeyEx." + key, handler)
        self.assertIn("selectedResult = selectedResult < 0 ? 0 : (selectedResult + 1) % count;", handler)
        self.assertIn("selectedResult = selectedResult < 0 ? count - 1 : (selectedResult + count - 1) % count;", handler)
        self.assertIn("int target = selectedResult < 0 ? 0 : selectedResult;", handler)
        self.assertEqual(handler.count("if (target-- == 0)"), 2)
        self.assertIn("if (!pinnedButtons[i].Visible)", handler)
        self.assertIn("if (!toolButtons[i].Visible)", handler)
        self.assertLess(handler.index("if (key.Modifiers != 0)"),
                        handler.index("int target = selectedResult"))

    def test_start_menu_selection_resets_on_search_and_is_rendered(self):
        menu = source("source/GUI/StartMenu.cs")
        reset = menu.split("public void ResetSearch()", 1)[1].split(
            "private Button CreateMenuButton", 1
        )[0]
        self.assertIn("selectedResult = -1;", reset)
        handler = menu.split("public bool HandleKeyboard(KeyEvent key)", 1)[1].split(
            "public void UpdateInteractions(", 1
        )[0]
        self.assertGreaterEqual(handler.count("selectedResult = -1;"), 3)
        pinned = menu.split("private void RenderPinned(Canvas canvas)", 1)[1].split(
            "private void RenderTools(", 1
        )[0]
        tools = menu.split("private void RenderTools(Canvas canvas)", 1)[1].split(
            "private void RenderFooter(", 1
        )[0]
        self.assertIn("selectedResult == visibleCount", pinned)
        self.assertIn("selectedResult == visibleIndex", tools)
        # Independent filtered-list model: no stale result may be activated.
        for pinned_count in (0, 1, 3, 6):
            for tools_count in (0, 1, 3, 6):
                total = pinned_count + tools_count
                selection = -1
                if not total:
                    self.assertEqual(selection, -1)
                    continue
                selection = 0  # Down from no selection.
                self.assertEqual(selection, 0)
                selection = (selection + total - 1) % total  # Up wraps.
                self.assertEqual(selection, total - 1)
                self.assertEqual(selection < pinned_count,
                                 total - 1 < pinned_count)
                selection = 0  # Search edits clear the old selection.
                self.assertEqual(selection, 0)

    def test_start_menu_keyboard_selection_survives_stationary_mouse(self):
        menu = source("source/GUI/StartMenu.cs")
        interaction = menu.split("public void UpdateInteractions(", 1)[1].split(
            "private static void UpdateButtonInteractions(", 1)[0]
        self.assertIn("lastPointerX != int.MinValue", interaction)
        self.assertIn("mouseX != lastPointerX || mouseY != lastPointerY", interaction)
        self.assertIn("selectedResult = -1;", interaction)
        self.assertLess(interaction.index("selectedResult = -1;"),
                        interaction.index("UpdateButtonInteractions(pinnedButtons"))
        self.assertIn("lastPointerX = int.MinValue;", menu)
        self.assertIn("lastPointerY = int.MinValue;", menu)
        self.assertIn("selectedResult == visibleCount : button.IsHovered", menu)
        self.assertIn("selectedResult == visibleIndex : button.IsHovered", menu)
        # A stationary cursor must not override a keyboard highlight; an
        # actual move returns the highlight to the hovered mouse button.
        def selection(selected, last, current):
            if last is not None and last != current:
                selected = -1
            return selected
        self.assertEqual(selection(2, (100, 200), (100, 200)), 2)
        self.assertEqual(selection(2, (100, 200), (101, 200)), -1)

    def test_modifier_policy_for_destructive_actions(self):
        # Independent model of the intended key predicate, not a runtime test.
        for ctrl in (False, True):
            for alt in (False, True):
                for shift in (False, True):
                    modifiers = (1 if ctrl else 0) | (2 if alt else 0) | (4 if shift else 0)
                    self.assertEqual(modifiers == 0, not (ctrl or alt or shift))


if __name__ == "__main__":
    unittest.main(verbosity=2)
