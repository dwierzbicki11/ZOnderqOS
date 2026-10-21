#!/usr/bin/env python3
"""Contract tests for the low-overhead rounded desktop on Cosmos 3.0.94.

These are source-level regressions; full compilation and QEMU boot are
performed separately by experimental-cosmos-3094-usb.yml.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
get = lambda p: (ROOT / p).read_text(encoding="utf-8")
PROJECT = get("ZonderqOS.csproj")
PAINTER = get("source/GUI/UiPainter.cs")
COLORS = get("source/GUI/UiPalette.cs")
METRICS = get("source/GUI/UiMetrics.cs")
ANIMATOR = get("source/GUI/UiAnimator.cs")
BUTTON = get("source/GUI/Button.cs")
WINDOW = get("source/GUI/Window.cs")
START = get("source/GUI/StartMenu.cs")
TASKBAR = get("source/GUI/Taskbar.cs")
DESKTOP = get("source/GUI/GuiManager.cs")

class ModernUi3094(unittest.TestCase):
    def test_cosmos_version_is_pinned(self):
        self.assertIn('Sdk="Cosmos.Sdk/3.0.94"', PROJECT)
        self.assertIn('Version="3.0.94"', PROJECT)

    def test_clipped_rounded_renderer_without_fullscreen_alpha(self):
        self.assertIn("RoundedPanel(", PAINTER)
        self.assertIn("RoundedTop(", PAINTER)
        self.assertIn("Shadow(", PAINTER)
        self.assertIn("CornerInset(", PAINTER)
        self.assertIn("Math.Min(canvas.Width, right)", PAINTER)
        self.assertIn("Math.Min(canvas.Height, bottom)", PAINTER)
        for forbidden in ("new int[", "new Color[", "DrawPoint(", "Math.Cos(", "Math.Sin(", "Thread.Sleep(", "Task.Run("):
            self.assertNotIn(forbidden, PAINTER)

    def test_shared_theme_and_component_geometry(self):
        self.assertIn("SystemTheme.Accent", COLORS)
        self.assertIn("WindowRadius = 12", METRICS)
        self.assertIn("ButtonRadius = 8", METRICS)
        self.assertIn("UiPainter.RoundedPanel(", BUTTON)
        self.assertIn("UiPainter.RoundedPanel(", WINDOW)
        self.assertIn("UiPainter.RoundedPanel(", START)
        self.assertIn("UiPainter.RoundedPanel(", TASKBAR)
        self.assertIn("UiPainter.RoundedTop(", WINDOW)
        self.assertIn("UiPainter.RoundedTop(", START)

    def test_existing_window_interactions_are_preserved(self):
        for required in ("ToggleMaximize()", "ToggleMinimize()", "BeginResize(", "ApplyResize(", "CloseAction?.Invoke();", "MoveTo("):
            self.assertIn(required, WINDOW)
        self.assertIn("if (!IsMaximized)", WINDOW)
        self.assertIn("IsMinimized", WINDOW)

    def test_animation_uses_bounded_idle_budget(self):
        self.assertIn("RequestFrames(int frames = 9)", ANIMATOR)
        self.assertIn("frames > 12 ? 12 : frames", ANIMATOR)
        self.assertIn("FrameRendered()", ANIMATOR)
        self.assertIn("frameCounter % 2 == 0", DESKTOP)
        self.assertIn("heartbeat || animationFrame", DESKTOP)
        self.assertIn("UiAnimator.FrameRendered();", DESKTOP)
        self.assertIn("UiAnimator.Approach", BUTTON)
        for forbidden in ("new Thread(", "Thread.Sleep(", "Task.Run(", "Timer("):
            self.assertNotIn(forbidden, ANIMATOR)

if __name__ == "__main__":
    unittest.main(verbosity=2)
