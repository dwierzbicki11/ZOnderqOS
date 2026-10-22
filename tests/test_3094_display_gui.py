#!/usr/bin/env python3
"""Cosmos 3.0.94 GUI + physical display-mode negotiation regressions."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
PROJECT = (ROOT/"ZonderqOS.csproj").read_text()
KERNEL = (ROOT/"Kernel.cs").read_text()
SELECTOR = (ROOT/"source/GUI/DisplayModeSelector.cs").read_text()
MAIN = (ROOT/"source/GUI/GuiManager.cs").read_text()
LOGIN = (ROOT/"source/GUI/LoginScreenManager.cs").read_text()
INITIAL = (ROOT/"source/GUI/InitialSetupManager.cs").read_text()
PROBE = (ROOT/"source/GUI/ReadOnlyDesktop.cs").read_text()
LIMINE = (ROOT/"Bootloader/limine.conf").read_text()
DISPLAY_INFO = (ROOT/"source/Shell/Commands/CmdDisplayInfo.cs").read_text()
COMMANDS = (ROOT/"source/Shell/Command.cs").read_text()

class NativeResolution3094(unittest.TestCase):
    def test_version(self):
        self.assertIn('Sdk="Cosmos.Sdk/3.0.94"', PROJECT)
        self.assertIn('Version="3.0.94"', PROJECT)

    def test_mode_comes_from_display(self):
        self.assertIn('Canvas.GetFullScreen()', SELECTOR)
        self.assertIn('canvas.AvailableModes', SELECTOR)
        self.assertIn('Canvas.GetFullScreen(chosen)', SELECTOR)
        self.assertIn('canvas.Mode', SELECTOR)
        self.assertIn('pixels <= bestPixels', SELECTOR)
        self.assertIn('mode.ColorDepth != 32', SELECTOR)
        self.assertIn('3840L * 2160L', SELECTOR)
        self.assertIn('canvas.Width', SELECTOR)
        self.assertIn('canvas.Height', SELECTOR)
        for source in (MAIN, LOGIN, INITIAL, PROBE):
            self.assertIn('DisplayModeSelector.OpenNativeOrBest()', source)
        self.assertIn('MouseManager.SetScreenSize(canvas.Width, canvas.Height)', MAIN)

    def test_1366x768_framebuffer_requested_by_bootloader(self):
        # Limine v10's per-entry "resolution", NOT "interface_resolution".
        # An unsupported mode falls back to firmware/driver's real mode.
        self.assertIn('protocol: limine', LIMINE)
        self.assertIn('resolution: 1366x768x32', LIMINE)
        self.assertIn('path: boot():/boot/ZonderqOS.elf', LIMINE)
        self.assertNotIn('interface_resolution:', LIMINE)
        self.assertNotIn('resolution: 800x600', LIMINE)
        self.assertIn('canvas.Width', SELECTOR)
        self.assertIn('canvas.Height', SELECTOR)

    def test_displayinfo_is_diagnostic(self):
        self.assertIn('class CmdDisplayInfo : ICommand', DISPLAY_INFO)
        self.assertIn('canvas.Name', DISPLAY_INFO)
        self.assertIn('canvas.AvailableModes', DISPLAY_INFO)
        self.assertIn('current.Width', DISPLAY_INFO)
        self.assertIn('current.Height', DISPLAY_INFO)
        self.assertNotIn('canvas.Mode =', DISPLAY_INFO)
        self.assertIn('new CmdDisplayInfo()', COMMANDS)

    def test_full_desktop_never_falls_back_to_efi(self):
        self.assertIn('if (!Disk.Initialize())', KERNEL)
        self.assertLess(KERNEL.index('if (!Disk.Initialize())'), KERNEL.index('UserManager.Initialize()'))
        self.assertIn('GuiManager manager = new GuiManager()', KERNEL)
        self.assertIn('!DiagnosticConsoleBoot && SystemSettings.BootToGui', KERNEL)

    def test_separate_readonly_probe(self):
        self.assertIn('#if ZONDERQ_READONLY_HARDWARE_PROBE', KERNEL)
        self.assertIn('RunReadOnlyInputConsole()', KERNEL)

if __name__ == "__main__":
    unittest.main(verbosity=2)
