#!/usr/bin/env python3
"""Source contract: optional QEMU-only failed unlock memory diagnosis."""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CS = (ROOT / "ZonderqOS.csproj").read_text()
LOCK = (ROOT / "source/GUI/LockScreenManager.cs").read_text()
SEC = (ROOT / "source/Security/SecurityLogger.cs").read_text()
PROBE = (ROOT / "source/Platform/X64/AuthFailureMemoryProbe.cs").read_text()


class FailedUnlockRamContracts(unittest.TestCase):
    def test_compile_disabled_by_default_and_only_ramroot_x64(self):
        self.assertIn("<ZonderqAuthFailMemoryProbe Condition=", CS)
        self.assertIn(">false</ZonderqAuthFailMemoryProbe>", CS)
        self.assertIn("'$(ZonderqRamRoot)' != 'true'", CS)
        self.assertIn("'$(CosmosArch)' != 'x64'", CS)
        self.assertTrue(PROBE.startswith("#if ZONDERQ_AUTH_FAIL_MEM_DIAG"))

    def test_failed_path_preserves_auth_and_alert(self):
        self.assertIn("UserManager.ValidateCredentialsChars(username, password, passwordLength)", LOCK)
        self.assertIn("AuthenticationGuard.RecordFailure(username);", LOCK)
        self.assertIn('SecurityLogger.LogEvent("WARN", "Failed graphical unlock attempt', LOCK)
        self.assertIn('Mark("after_verify_fail")', LOCK)
        self.assertIn('Mark("before_failure_log")', LOCK)
        self.assertIn('Mark("after_failure_log")', LOCK)

    def test_distinguishes_disk_flush_from_auth_write(self):
        self.assertIn('Mark("before_system_log")', SEC)
        self.assertIn('Mark("after_system_log")', SEC)
        self.assertIn('Mark("after_auth_queue_flush")', SEC)
        self.assertIn('Mark("after_auth_log_write")', SEC)
        self.assertIn("FlushPendingLocked();", SEC)
        self.assertIn("AppendFormattedEntryLocked(logEntry);", SEC)

    def test_probe_does_not_create_buffers_write_disk_or_collect(self):
        for forbidden in ("new byte[", "new char[", "new string", "File.", "Directory.",
                          "GC.Collect(", "Heap.Collect(", "password", "username"):
            self.assertNotIn(forbidden, PROBE)
        self.assertIn('WriteAscii("[AUTH_FAIL_MEM] phase=")', PROBE)
        self.assertIn('WriteAscii(" p_gc=")', PROBE)
        self.assertIn('WriteAscii(" comm_kb=")', PROBE)

    def test_existing_safety_behavior_kept(self):
        self.assertIn("if (ok)", LOCK)
        self.assertIn("AuthenticationGuard.RecordSuccess(username);", LOCK)
        self.assertIn("ClearPassword();", LOCK)
        self.assertIn("AppendFormattedEntryLocked(logEntry);", SEC)


if __name__ == "__main__":
    unittest.main()
