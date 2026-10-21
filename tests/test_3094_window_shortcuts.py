#!/usr/bin/env python3
"""Keyboard window manager contracts for Cosmos Gen 3 / 3.0.94.

These are source and pure-model checks, NOT a substitute for actual keyboard
events, C# compilation, QEMU GUI, or physical Alt+Tab testing.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


def source(path):
    return (ROOT / path).read_text(encoding="utf-8")


GUI = source("source/GUI/GuiManager.cs")
MANAGER = source("source/GUI/Application/ApplicationManager.cs")
NOTEPAD = source("source/GUI/Application/NotepadApp.cs")
WINDOW = source("source/GUI/Window.cs")
PROJECT = source("ZonderqOS.csproj")


def cycle_model(order, minimized, reverse=False):
    """Model ApplicationManager.CycleWindow ordering for live app objects."""
    current = next((name for name in reversed(order) if name not in minimized), None)
    if current is None:
        if not order:
            return None
        candidate = order[-1]
        minimized.discard(candidate)
        return candidate

    current_index = order.index(current)
    count = len(order)
    if count < 2:
        return None
    index = (current_index + (1 if reverse else count - 1)) % count
    candidate = order[index]
    if not reverse:
        order.remove(current)
        order.insert(0, current)
    order.remove(candidate)
    order.append(candidate)
    minimized.discard(candidate)
    return candidate


class WindowShortcuts3094(unittest.TestCase):
    def test_exact_cosmos_sdk(self):
        self.assertIn('Sdk="Cosmos.Sdk/3.0.94"', PROJECT)
        self.assertIn('Version="3.0.94"', PROJECT)

    def test_modifiers_and_precedence(self):
        self.assertIn("if (HandleWindowShortcut(key))", GUI)
        self.assertLess(GUI.index("if (HandleWindowShortcut(key))"),
                        GUI.index("commandPalette.HandleKeyboard(key);"))
        self.assertLess(GUI.index("if (HandleWindowShortcut(key))"),
                        GUI.index("applicationManager.HandleKeyboard(key);"))
        self.assertIn("if (!alt || control)", GUI)
        self.assertIn("key.Key == ConsoleKeyEx.Tab", GUI)
        self.assertIn("key.Key != ConsoleKeyEx.F4", GUI)
        self.assertIn("ConsoleModifiers.Shift", GUI)
        self.assertIn("applicationManager?.CycleWindow(reverse);", GUI)
        self.assertIn("applicationManager?.RequestCloseActiveWindow();", GUI)

    def test_overlays_take_precedence_before_close(self):
        method = GUI.split("private bool HandleWindowShortcut(KeyEvent key)", 1)[1]
        method = method.split("private void LogoutSession()", 1)[0]
        for name in ("commandPalette", "notificationCenter", "startMenu", "desktopContextMenu"):
            self.assertIn(name, method)
        self.assertLess(method.index("commandPalette.Close();"),
                        method.index("applicationManager?.RequestCloseActiveWindow();"))

    def test_session_and_dirty_document_not_bypassed(self):
        self.assertIn("SecurityContext.IsAuthenticated", MANAGER)
        self.assertIn("active.Window.CloseAction();", MANAGER)
        self.assertIn("if (!active.IsRunning)", MANAGER)
        self.assertIn("Window.CloseAction = RequestClose;", NOTEPAD)
        self.assertIn("CloseAction?.Invoke();", WINDOW)

    def test_no_repeated_two_window_bounce(self):
        order = ["A", "B", "C"]
        minimized = set()
        self.assertEqual([cycle_model(order, minimized) for _ in range(4)],
                         ["B", "A", "C", "B"])
        order = ["A", "B", "C"]
        self.assertEqual([cycle_model(order, set(), reverse=True) for _ in range(4)],
                         ["A", "B", "C", "A"])

    def test_minimized_window_selection(self):
        order = ["A", "B", "C"]
        minimized = {"C"}
        self.assertEqual(cycle_model(order, minimized), "A")
        self.assertEqual(cycle_model(order, minimized), "C")
        self.assertNotIn("C", minimized)
        self.assertEqual(cycle_model(order, minimized), "B")

    def test_all_minimized_and_empty_lists(self):
        self.assertIsNone(cycle_model([], set()))
        order = ["A", "B"]
        minimized = {"A", "B"}
        self.assertEqual(cycle_model(order, minimized), "B")
        self.assertEqual(minimized, {"A"})


if __name__ == "__main__":
    unittest.main()
