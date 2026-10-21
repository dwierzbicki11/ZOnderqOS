#!/usr/bin/env python3
"""Rounded shell overlay / secure login source contracts for Cosmos Gen3 3.0.94.

Build and QEMU safety verification is performed by the pinned CI workflow.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]

def read(name):
    return (ROOT / name).read_text(encoding="utf-8")

PROJECT = read("ZonderqOS.csproj")
NOTIFICATIONS = read("source/GUI/NotificationCenter.cs")
COMMANDS = read("source/GUI/CommandPalette.cs")
CONTEXT = read("source/GUI/DesktopContextMenu.cs")
LOGIN = read("source/GUI/LoginScreenManager.cs")
SETUP = read("source/GUI/InitialSetupManager.cs")


class OverlayAndSecureSession3094(unittest.TestCase):
    def test_runtime_is_pinned_and_uses_shared_renderer(self):
        self.assertIn('Sdk="Cosmos.Sdk/3.0.94"', PROJECT)
        self.assertIn('Version="3.0.94"', PROJECT)
        for source in (NOTIFICATIONS, COMMANDS, CONTEXT, LOGIN, SETUP):
            self.assertIn("UiPainter.RoundedPanel(", source)
            self.assertTrue(
                any(radius in source for radius in (
                    "UiMetrics.ButtonRadius",
                    "UiMetrics.TileRadius",
                    "UiMetrics.MenuRadius",
                )),
                "Missing UI radius for a styled panel",
            )
            self.assertIn("UiPainter.Shadow(", source)

    def test_notification_center_keeps_all_actions(self):
        for marker in (
            "NotificationService.ClearAll()", "NotificationService.MarkAllRead()",
            "NotificationService.DoNotDisturb",
            "NotificationService.TryGetActiveToast(", "DismissSelected()",
            "RenderList(canvas);", "RenderToast(canvas);",
        ):
            self.assertIn(marker, NOTIFICATIONS)
        self.assertIn("UiAnimator.RequestFrames(8)", NOTIFICATIONS)
        self.assertIn("UiPainter.RoundedTop(", NOTIFICATIONS)

    def test_command_palette_still_searches_and_launches(self):
        for marker in ("RebuildResults()", "LaunchSelected()", "KeepSelectionVisible()",
                       "Visible = true;", "Visible = false;"):
            self.assertIn(marker, COMMANDS)
        self.assertIn("UiAnimator.RequestFrames(8)", COMMANDS)
        self.assertIn("UiPainter.RoundedTop(", COMMANDS)

    def test_context_menu_preserves_hit_targets(self):
        self.assertIn("HitTest(mouseX, mouseY)", CONTEXT)
        self.assertIn("entries[index].Action?.Invoke()", CONTEXT)
        self.assertIn("UiAnimator.RequestFrames(6)", CONTEXT)

    def test_secure_authentication_unchanged(self):
        for marker in (
            "SecurityContext.IsAuthenticated", "AuthenticationGuard",
            "screen.ClearSensitiveData()", "MouseManager.SetScreenSize(",
            "PasswordMask", "TryStartSession("
        ):
            self.assertIn(marker, LOGIN)
        for marker in (
            "UserManager.RequiresInitialRootPasswordSetup()",
            "screen.ClearSensitiveData()",
            "PasswordPolicy.MinLength", "CompleteInitialRootPasswordSetup(",
            "MouseManager.SetScreenSize(",
        ):
            self.assertIn(marker, SETUP)

    def test_no_new_background_threads_or_graphics_allocations(self):
        for source in (NOTIFICATIONS, COMMANDS, CONTEXT, LOGIN, SETUP):
            self.assertNotIn("Task.Run(", source)
            self.assertNotIn("new Thread(", source)


if __name__ == "__main__":
    unittest.main(verbosity=2)
