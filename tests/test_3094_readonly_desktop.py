#!/usr/bin/env python3
"""Cosmos 3.0.94 read-only GUI USB integration source-contract gate.

Not a substitute for GUI tests on real Broadwell hardware.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
DESKTOP = (ROOT / "source/GUI/ReadOnlyDesktop.cs").read_text()
WINDOW = (ROOT / "source/GUI/Window.cs").read_text()
KERNEL = (ROOT / "Kernel.cs").read_text()
PROJECT = (ROOT / "ZonderqOS.csproj").read_text()

class Gen3UsbDesktop(unittest.TestCase):
    def test_pinned_3094_diskless_provision(self):
        self.assertIn('Sdk="Cosmos.Sdk/3.0.94"', PROJECT)
        self.assertIn("#if ZONDERQ_READONLY_HARDWARE_PROBE && ZONDERQ_COSMOS_INPUT_API", DESKTOP)
        for unsafe in ("File.", "Directory.", "TryMount(", "WriteBlock(",
                       "Disk.Initialize(", "GuiManager", "ServiceManager",
                       "IconManager.Preload", "IconManager.DrawScaled",
                       "UserManager"):
            self.assertNotIn(unsafe, DESKTOP)

    def test_actual_production_widgets_and_mouse(self):
        for feature in ("new Window(", "new Button(", "main.Render(canvas)",
                        "help.Render(canvas)", "Cursor.Draw(canvas,",
                        "MouseManager.X", "MouseManager.Y",
                        "MouseManager.LeftButton", "MouseManager.RightButton",
                        "KeyboardManager.TryReadKey", "DisplayModeSelector.OpenNativeOrBest()",
                        "canvas.Display()"):
            self.assertIn(feature, DESKTOP)

    def test_icon_cache_not_called_in_probe_caption(self):
        self.assertIn("#if ZONDERQ_READONLY_HARDWARE_PROBE", WINDOW)
        self.assertIn("canvas.DrawRectangle(Color.White, maximizeX", WINDOW)
        self.assertIn("canvas.DrawLine(Color.White, closeX", WINDOW)

    def test_console_route_only(self):
        self.assertIn('command == "desktop" || command == "gui"', KERNEL)
        self.assertIn("ReadOnlyDesktop.Run()", KERNEL)
        self.assertIn("ReadOnlyHardwareProbe", PROJECT)
        self.assertIn("No storage writes or privileged shell commands are enabled.", KERNEL)

if __name__ == "__main__":
    unittest.main(verbosity=2)
