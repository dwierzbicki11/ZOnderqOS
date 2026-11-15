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

    def test_cosmos_cli_define_override_cannot_drop_project_symbols(self):
        """`cosmos build` passes -p:DefineConstants=ARCH_<X>, a global property that
        silently replaced every csproj DefineConstants (ZONDERQ_COSMOS_INPUT_API ->
        CS0246 KeyEvent). TreatAsLocalProperty keeps the project symbols."""
        project = (ROOT / "ZonderqOS.csproj").read_text(encoding="utf-8")
        first = project.splitlines()[0]
        self.assertIn('TreatAsLocalProperty="DefineConstants"', first)

    def test_only_project_defined_arch_symbols_are_used(self):
        """Regression: GuiManager used `#if !ARM64`, a symbol never defined,
        so the x64-only SerialMemoryTelemetry call leaked into ARM64 builds."""
        import re
        allowed = {"ZONDERQ_ARCH_ARM64"}
        bad = []
        for path in (ROOT / "source").rglob("*.cs"):
            for n, line in enumerate(path.read_text(encoding="utf-8", errors="ignore").splitlines(), 1):
                if re.match(r"\s*#\s*(if|elif)\b", line):
                    for sym in re.findall(r"\b[A-Z_][A-Z0-9_]*(?:ARM64|X64|X86|AMD64)\b", line):
                        if sym not in allowed:
                            bad.append(f"{path.relative_to(ROOT)}:{n}: {sym}")
        self.assertEqual(bad, [], "undefined architecture symbols in preprocessor directives")

    def test_x64_references_outside_platform_are_guarded(self):
        for path in (ROOT / "source").rglob("*.cs"):
            rel = path.relative_to(ROOT).as_posix()
            if rel.startswith("source/Platform/X64/"):
                continue
            depth = 0
            guarded = []
            for n, line in enumerate(path.read_text(encoding="utf-8", errors="ignore").splitlines(), 1):
                s = line.strip()
                if s.startswith("#if"):
                    guarded.append("!ZONDERQ_ARCH_ARM64" in s)
                elif s.startswith("#endif") and guarded:
                    guarded.pop()
                elif "Platform.X64." in s and not s.startswith("//"):
                    self.assertTrue(any(guarded), f"{rel}:{n} references Platform.X64 without ARM64 guard")

    def test_window_smoke_stubs_production_onclick(self):
        source = (ROOT / "tests/GuiWindowInputSmoke/Program.cs").read_text(encoding="utf-8")
        self.assertIn("internal sealed class Button : Widget", source)
        self.assertIn("public System.Action OnClick { get; set; }", source)


if __name__ == "__main__":
    unittest.main()
