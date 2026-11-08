#!/usr/bin/env python3
"""Stage 3 Fault Isolation, Single-Instance Restoration and Shortcut Tests.

Verifies application crash isolation in ApplicationManager, single-instance
window activation, and Ctrl+Shift+Escape Task Manager shortcut.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
APP_MGR = (ROOT / "source/GUI/Application/ApplicationManager.cs").read_text(encoding="utf-8")
GUI_MGR = (ROOT / "source/GUI/GuiManager.cs").read_text(encoding="utf-8")


class Stage3FaultIsolationTests(unittest.TestCase):
    def test_application_fault_isolation_methods_guarded(self):
        self.assertIn("private void HandleApplicationFault(", APP_MGR)
        self.assertIn("HandleApplicationFault(application, \"Update\", ex);", APP_MGR)
        self.assertIn("HandleApplicationFault(application, \"Render\", ex);", APP_MGR)
        self.assertIn("HandleApplicationFault(target, \"Mouse\", ex);", APP_MGR)
        self.assertIn("HandleApplicationFault(active, \"Keyboard\", ex);", APP_MGR)

    def test_fault_isolation_posts_notification_and_logs(self):
        fault_body = APP_MGR.split("private void HandleApplicationFault(", 1)[1]
        self.assertIn("global::ZonderqOS.SystemLogger.Log(", fault_body)
        self.assertIn("global::ZonderqOS.NotificationService.Post(", fault_body)
        self.assertIn("Close(application);", fault_body)

    def test_task_manager_global_shortcut(self):
        self.assertIn("private static bool IsTaskManagerShortcut(KeyEvent key)", GUI_MGR)
        shortcut_body = GUI_MGR.split("private static bool IsTaskManagerShortcut(KeyEvent key)", 1)[1].split("}", 1)[0]
        self.assertIn("ConsoleKeyEx.Escape", shortcut_body)
        self.assertIn("ConsoleModifiers.Control", shortcut_body)
        self.assertIn("ConsoleModifiers.Shift", shortcut_body)
        self.assertIn("if (IsTaskManagerShortcut(key))", GUI_MGR)
        self.assertIn("LaunchTaskManager(165, 110);", GUI_MGR)

    def test_single_instance_task_manager_and_settings(self):
        task_body = GUI_MGR.split("private void LaunchTaskManager(int x, int y)", 1)[1].split("private void LaunchSettings", 1)[0]
        self.assertIn("if (existing.Window != null && existing.Window.IsMinimized)", task_body)
        self.assertIn("existing.Window.RestoreFromMinimized();", task_body)
        self.assertIn("applicationManager.Activate(existing);", task_body)

        settings_body = GUI_MGR.split("private void LaunchSettings(int x, int y)", 1)[1].split("private void LaunchConfigManager", 1)[0]
        self.assertIn("if (existing.Window != null && existing.Window.IsMinimized)", settings_body)
        self.assertIn("existing.Window.RestoreFromMinimized();", settings_body)
        self.assertIn("applicationManager.Activate(existing);", settings_body)


if __name__ == "__main__":
    unittest.main()
