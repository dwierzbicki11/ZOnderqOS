#!/usr/bin/env python3
"""Source-level safeguards for the Cosmos 3.0.94 graphical boot splash.

This is not a replacement for a compiled QEMU / real-hardware boot test.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
KERNEL = (ROOT / "Kernel.cs").read_text()
SPLASH = (ROOT / "source/GUI/BootSplash.cs").read_text()
MESSAGES = (ROOT / "source/Common/WriteMessage.cs").read_text()
PROJECT = (ROOT / "ZonderqOS.csproj").read_text()


class BootSplash3094(unittest.TestCase):
    def test_remains_on_canonical_cosmos(self):
        self.assertIn('Sdk="Cosmos.Sdk/3.0.94"', PROJECT)
        self.assertIn('Version="3.0.94"', PROJECT)

    def test_no_disk_assets_or_background_thread_during_boot(self):
        self.assertIn("DisplayModeSelector.OpenNativeOrBest()", SPLASH)
        self.assertIn("PCScreenFont.DefaultFont", SPLASH)
        self.assertIn("canvas.Display()", SPLASH)
        self.assertIn("Canvas.DisableFullScreen()", SPLASH)
        for forbidden in ("System.IO", "File.", "Directory.", "new Thread(", "Task.Run(", "IconManager.Preload("):
            self.assertNotIn(forbidden, SPLASH)

    def test_boot_pipeline_and_diagnostic_isolation(self):
        self.assertIn("if (!DiagnosticConsoleBoot)", KERNEL)
        self.assertIn("BootSplash.Start();", KERNEL)
        self.assertIn("BootSplash.Advance(27", KERNEL)
        self.assertIn("BootSplash.Complete(handOffToGui);", KERNEL)
        self.assertIn("BootSplash.Abort();", KERNEL)
        self.assertLess(KERNEL.index("InputBusDiagnostics.PrintReport();"), KERNEL.index("BootSplash.Start();"))
        self.assertLess(KERNEL.index("BootSplash.Start();"), KERNEL.index("if (!Disk.Initialize())"))
        self.assertIn("!DiagnosticConsoleBoot && SystemSettings.BootToGui", KERNEL)
        self.assertIn("KernelPanic.Show(ex, \"BOOT\", false);", KERNEL)

    def test_suppressed_console_still_persists_logs_and_shows_errors(self):
        self.assertLess(MESSAGES.index("SystemLogger.Log(logLevel, module, message)"),
                        MESSAGES.index("BootSplash.IsActive"))
        self.assertIn("if (logLevel >= SystemLogLevel.Error)", MESSAGES)
        self.assertIn("BootSplash.Abort();", MESSAGES)
        self.assertIn("WriteMessage.WriteOK(\"ZonderqOS kernel successfully booted.", KERNEL)


if __name__ == "__main__":
    unittest.main(verbosity=2)
