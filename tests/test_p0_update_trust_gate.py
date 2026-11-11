#!/usr/bin/env python3
"""P0 fail-closed update gate regression checks (static contract).

This is not an end-to-end QEMU security test. The install entry point must
refuse all legacy HMAC updates until an independently reviewed asymmetric
manifest and payload verifier is integrated.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
SOURCE = (ROOT / "source/Core/Updates/UpdateManager.cs").read_text(encoding="utf-8")


class UpdateTrustGateTests(unittest.TestCase):
    def test_unconfigurable_gate_stays_disabled(self):
        self.assertRegex(
            SOURCE,
            r"private static bool HasVerifiedAsymmetricUpdateTrust\(\)\s*\{\s*return false;\s*\}",
        )

    def test_gate_precedes_all_update_install_steps(self):
        method = SOURCE.split("public static bool ApplyUpdateStep(", 1)[1]
        guard = method.index("if (!HasVerifiedAsymmetricUpdateTrust())")
        first_install_step = method.index("switch (step)")
        self.assertLess(guard, first_install_step)
        guarded_branch = method[guard:first_install_step]
        self.assertIn("Status = UpdateStatus.Error;", guarded_branch)
        self.assertIn("return false;", guarded_branch)
        self.assertIn("SecurityLogger.LogEvent", guarded_branch)

    def test_every_stage_uses_single_guarded_entry(self):
        method = SOURCE.split("public static bool ApplyUpdateStep(", 1)[1]
        method = method.split("public static bool ApplyUpdate(", 1)[0]
        self.assertRegex(method, r"case 0:")
        self.assertRegex(method, r"case 5:")
        apply = SOURCE.split("public static bool ApplyUpdate(", 1)[1]
        self.assertIn("ApplyUpdateStep(", apply)

    def test_explicit_trust_requirement(self):
        self.assertIn("podpis asymetryczny calego manifestu", SOURCE)
        self.assertIn("weryfikacja wszystkich plikow", SOURCE)


if __name__ == "__main__":
    unittest.main()
