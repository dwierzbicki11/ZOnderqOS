#!/usr/bin/env python3
"""Host regression tests for IntegrityGuard + SystemSettings integration."""
from pathlib import Path
import subprocess
import unittest

ROOT = Path(__file__).resolve().parents[1]


class IntegrityGuardRegressionTests(unittest.TestCase):
    def test_fresh_install_and_settings_save_scenarios(self):
        res = subprocess.run(
            ["dotnet", "run", "--project", str(ROOT / "tests/IntegrityGuardSmoke/IntegrityGuardSmoke.csproj")],
            cwd=ROOT, capture_output=True, text=True, timeout=120,
        )
        self.assertEqual(res.returncode, 0, res.stdout + res.stderr)
        self.assertIn("[INTEGRITY-GUARD] PASS", res.stdout)

    def test_system_settings_notifies_integrity_guard_after_save(self):
        src = (ROOT / "source/Core/SystemSettings.cs").read_text(encoding="utf-8")
        self.assertGreaterEqual(src.count("IntegrityGuard.NotifyManagedFileSaved(SettingsPath)"), 2)


if __name__ == "__main__":
    unittest.main()
