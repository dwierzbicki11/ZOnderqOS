#!/usr/bin/env python3
"""Static regression for GUI session ownership and idle telemetry caching.

Host-side source contracts complement (do not replace) 60-minute real RAM tests.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
GUI = (ROOT / "source/GUI/GuiManager.cs").read_text(encoding="utf-8")
TASKS = (ROOT / "source/GUI/Application/TaskManagerModernApp.cs").read_text(encoding="utf-8")


class GuiMemoryLifecycle(unittest.TestCase):
    def test_shutdown_callback_detached_after_session_end(self):
        self.assertIn("SystemPower.OnBeforeShutdown = RenderShutdownScreen;", GUI)
        body = GUI.split("public void Run()", 1)[1].split(
            "private void RenderFrame(", 1)[0]
        self.assertIn("finally", body)
        finally_body = body.rsplit("finally", 1)[1]
        self.assertIn("SystemPower.OnBeforeShutdown?.Target, this", finally_body)
        self.assertIn("SystemPower.OnBeforeShutdown = null;", finally_body)
        self.assertLess(finally_body.index("OnBeforeShutdown?.Target, this"),
                        finally_body.index("OnBeforeShutdown = null;"))
        self.assertIn("object.ReferenceEquals(Instance, this)", finally_body)

    def test_session_closes_applications_and_releases_framebuffers(self):
        last = GUI.split("public void Run()", 1)[1].split(
            "private void RenderFrame(", 1)[0].rsplit("finally", 1)[1]
        for code in ("applicationManager?.CloseAll();",
                     "wallpaperCanvas = null;", "cursorDamageRenderer = null;",
                     "canvas = null;", "applicationManager = null;"):
            self.assertIn(code, last)

    def test_task_manager_formats_telemetry_on_sample_not_each_frame(self):
        # Prevent regression to per-frame string construction in the
        # Task Manager process/memory graph rendering path.
        self.assertIn("cachedMemoryDetailText", TASKS)
        self.assertIn("cachedGcCommittedText", TASKS)
        self.assertIn("cachedUsableRamText", TASKS)
        self.assertIn("samplingPaused", TASKS)


if __name__ == "__main__":
    unittest.main()
