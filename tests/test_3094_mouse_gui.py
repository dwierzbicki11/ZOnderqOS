#!/usr/bin/env python3
"""Static safety/API checks for the Cosmos 3.0.94 read-only USB mouse GUI.
Real HID movement still requires QEMU event injection or physical hardware.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
KERNEL = (ROOT / "Kernel.cs").read_text()
GUI = (ROOT / "source/Core/MouseGuiDiagnostic.cs").read_text()
PROJECT = (ROOT / "ZonderqOS.csproj").read_text()


class MouseGui3094Contract(unittest.TestCase):
    def test_3094_and_readonly_only(self):
        self.assertIn('Sdk="Cosmos.Sdk/3.0.94"', PROJECT)
        self.assertIn('CosmosEnableMouse>true', PROJECT)
        self.assertIn('CosmosEnableUsb>true', PROJECT)
        self.assertIn("#if ZONDERQ_READONLY_HARDWARE_PROBE && ZONDERQ_COSMOS_INPUT_API", GUI)
        for forbidden in ("Disk.Initialize(", "VfsManager.TryMount", "WriteBlock(", "File.Write", "Directory.Create", "UserManager.Initialize"):
            self.assertNotIn(forbidden, GUI)

    def test_pointer_is_live_manager_state(self):
        for member in ("MouseManager.X", "MouseManager.Y", "MouseManager.LeftButton",
                       "MouseManager.RightButton", "MouseManager.MiddleButton",
                       "MouseManager.SetScreenSize"):
            self.assertIn(member, GUI)
        self.assertIn("canvas.Display()", GUI)
        self.assertIn("FIRST MOVEMENT OBSERVED", GUI)
        self.assertIn("FIRST BUTTON EVENT OBSERVED", GUI)
        self.assertIn("Key.Escape", GUI)

    def test_readonly_console_routes_to_graphics(self):
        self.assertIn('command == "mousegui" || command == "cursor"', KERNEL)
        self.assertIn("MouseGuiDiagnostic.Run()", KERNEL)


if __name__ == "__main__":
    unittest.main(verbosity=2)
