#!/usr/bin/env python3
"""Source-contract tests for Gen 3 cross-architecture boot and GUI teardown.

These are static checks; the GitHub build gates compile production code for
x64/ARM64 and host GUI smoke tests exercise Window behaviour separately.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
BOOT = (ROOT / "source/Core/BootSessionHealth.cs").read_text(encoding="utf-8")
X64 = (ROOT / "source/Platform/X64/PlatformPower.cs").read_text(encoding="utf-8")
ARM = (ROOT / "source/Platform/Arm64/PlatformPower.cs").read_text(encoding="utf-8")
PROJECT = (ROOT / "ZonderqOS.csproj").read_text(encoding="utf-8")
WINDOW_SMOKE = (ROOT / "tests/GuiWindowInputSmoke/Program.cs").read_text(encoding="utf-8")


class GeneralStabilityContracts(unittest.TestCase):
    def test_arch_specific_power_backend_selected_by_existing_project(self):
        self.assertIn('Compile Remove="source/Platform/X64/**/*.cs"', PROJECT)
        self.assertIn('Compile Remove="source/Platform/Arm64/**/*.cs"', PROJECT)
        for backend in (X64, ARM):
            self.assertIn("namespace ZonderqOS.Platform", backend)
            self.assertIn("class PlatformPower", backend)
            self.assertIn("void TryShutdownViaFirmware()", backend)

    def test_arm_shutdown_never_uses_x86_io(self):
        self.assertNotIn("X64PortIo", ARM)
        self.assertNotIn("WriteWord(", ARM)
        self.assertIn("X64.X64PortIo.WriteWord(", X64)

    def test_common_boot_health_has_no_direct_x64_dependency(self):
        self.assertIn("ZonderqOS.Platform.PlatformPower.TryShutdownViaFirmware();", BOOT)
        self.assertNotIn("ZonderqOS.Platform.X64", BOOT)
        self.assertIn("Cosmos.Kernel.System.Power.Halt();", BOOT)

    def test_interrupted_boot_marker_rotation_is_recovered(self):
        self.assertIn("if (TryReadPreviousStateFile(StatePath))", BOOT)
        self.assertIn("if (hasBackup && TryReadPreviousStateFile(BackupPath))", BOOT)
        self.assertIn("new FileInfo(path)", BOOT)
        self.assertIn("new StreamReader(path)", BOOT)
        self.assertIn('string.Equals(state, "running"', BOOT)
        self.assertIn('string.Equals(state, "clean"', BOOT)
        self.assertLess(BOOT.index("ReadPreviousState();"), BOOT.index('TryWriteState("running"'))

    def test_existing_backup_survives_when_primary_is_missing(self):
        self.assertIn("if (File.Exists(StatePath))", BOOT)
        # Do not delete .bak unless there is a valid primary to rotate.
        begin = BOOT.index("// Preserve an existing backup")
        end = BOOT.index("File.Move(TempPath, StatePath)", begin)
        area = BOOT[begin:end]
        self.assertRegex(area, r"if \(File.Exists\(StatePath\)\)\s*\{\s*if \(File.Exists\(BackupPath\)\)")
        self.assertIn("File.Move(StatePath, BackupPath);", area)

    def test_host_gui_smoke_checks_detached_callbacks(self):
        self.assertIn("private static void ClosingWindowDetachesButtonCallbacks()", WINDOW_SMOKE)
        self.assertIn("button.OnClick == null", WINDOW_SMOKE)
        self.assertIn("ClosingWindowDetachesButtonCallbacks();", WINDOW_SMOKE)


if __name__ == "__main__":
    unittest.main()
