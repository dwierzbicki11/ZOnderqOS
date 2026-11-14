#!/usr/bin/env python3
"""Source-contract checks for low-allocation idle paths.

These checks protect the production code structure. They are not kernel/QEMU
measurements and cannot establish absence of a memory leak.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
UPDATER = (ROOT / "source/Core/Updates/UpdateManager.cs").read_text()
GUARDIAN = (ROOT / "source/Core/MemoryGuardian.cs").read_text()


class IdleMemoryBackgroundContracts(unittest.TestCase):
    def test_manual_checks_still_persist_last_check_time(self):
        self.assertIn(
            "return CheckForUpdatesCore(forceRefresh, true, out package, out message);",
            UPDATER,
        )
        self.assertIn(
            "if (persistCheckTime)\n                    SaveConfig();",
            UPDATER,
        )

    def test_background_checks_do_not_rewrite_settings_each_poll(self):
        self.assertIn(
            "CheckForUpdatesCore(false, false, out pkg, out msg);", UPDATER
        )
        self.assertNotIn(
            "bool found = CheckForUpdates(false, out pkg, out msg);", UPDATER
        )

    def test_manifest_candidate_paths_are_reused(self):
        self.assertIn(
            "private static readonly string[] ManifestCandidatePaths", UPDATER
        )
        self.assertIn(
            "for (int p = 0; p < ManifestCandidatePaths.Length; p++)", UPDATER
        )
        self.assertNotIn("string[] candidatePaths = new string[]", UPDATER)

    def test_guardian_timestamp_formatting_is_lazy(self):
        self.assertIn(
            "private static DateTime lastCheckTimeUtc = DateTime.MinValue;",
            GUARDIAN,
        )
        self.assertIn("lastCheckTimeUtc = now;", GUARDIAN)
        self.assertIn("FormatTime(checkedAt)", GUARDIAN)
        self.assertNotIn("lastCheckTime = FormatTime(now);", GUARDIAN)

    def test_no_forced_gc_was_added(self):
        self.assertNotIn("GC.Collect(", GUARDIAN)
        self.assertNotIn("GarbageCollector.Collect(", GUARDIAN)


if __name__ == "__main__":
    unittest.main()
