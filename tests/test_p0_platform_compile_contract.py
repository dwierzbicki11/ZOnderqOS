#!/usr/bin/env python3
"""Cross-architecture source contracts for ZonderqOS Gen 3 CI.

These tests complement (do not replace) real x64/ARM64 compiler jobs.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


class PlatformBoundaryTests(unittest.TestCase):
    def test_arm64_has_explicit_compile_symbol(self):
        project = (ROOT / "ZonderqOS.csproj").read_text(encoding="utf-8")
        self.assertIn('<DefineConstants>$(DefineConstants);ZONDERQ_ARCH_ARM64</DefineConstants>', project)
        self.assertIn('<Compile Remove="source/Platform/X64/**/*.cs" />', project)

    def test_x64_power_io_is_architecture_guarded(self):
        source = (ROOT / "source/Core/BootSessionHealth.cs").read_text(encoding="utf-8")
        start = source.index("#if !ZONDERQ_ARCH_ARM64")
        end = source.index("#endif", start)
        ports = [i for i in range(len(source)) if source.startswith("ZonderqOS.Platform.X64.X64PortIo.", i)]
        self.assertTrue(ports, "x64 power path must remain present")
        self.assertTrue(all(start < i < end for i in ports),
                        "all direct x86 power port calls must stay behind the architecture guard")

    def test_window_smoke_stubs_production_onclick(self):
        source = (ROOT / "tests/GuiWindowInputSmoke/Program.cs").read_text(encoding="utf-8")
        self.assertIn("internal sealed class Button : Widget", source)
        self.assertIn("public System.Action OnClick { get; set; }", source)


if __name__ == "__main__":
    unittest.main()
