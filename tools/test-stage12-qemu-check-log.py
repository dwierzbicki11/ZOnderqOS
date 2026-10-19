#!/usr/bin/env python3
"""Regression tests for serial-log arguments; these do not boot QEMU."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parent.parent


class CheckLogArgumentsTest(unittest.TestCase):
    def check_log(self, stage, contents, expected_status, legacy=False, missing=False, absent_iso=False):
        with tempfile.TemporaryDirectory() as directory:
            temp = Path(directory)
            log = temp / "serial log.txt"
            if not missing:
                log.write_text(contents)
            iso = temp / "not-used.iso"
            # A present ISO must never be validated as serial evidence either.
            if not absent_iso:
                iso.write_text("CPU EXCEPTION: wrong input\n")
            args = [str(log)] if legacy else [str(iso), str(log)]
            result = subprocess.run(
                ["bash", f"tools/smoke-stage{stage}-qemu.sh", "--check-log", *args],
                cwd=ROOT, capture_output=True, text=True, timeout=10,
                env={**os.environ, "STAGE23_QEMU_CPUS": "4",
                     "STAGE23_QEMU_CORES": "2", "STAGE23_QEMU_THREADS": "2"},
            )
            self.assertEqual(result.returncode, expected_status, result.stdout + result.stderr)
            self.assertNotIn("wrong input", result.stderr)
            if missing:
                self.assertIn(f"serial log not found: {log}", result.stderr)
            elif expected_status == 1:
                self.assertIn("kernel failure", result.stderr)
            else:
                self.assertNotIn("serial log not found", result.stderr)

    def test_two_argument_incomplete_log(self):
        self.check_log("12", "", 2)

    def test_legacy_single_argument(self):
        self.check_log("12", "", 2, legacy=True)

    def test_fatal_serial_is_rejected(self):
        self.check_log("12", "CPU EXCEPTION: regression\n", 1)

    def test_missing_serial_reports_log_path(self):
        self.check_log("12", "", 1, missing=True)

    def test_stage23_forwards_log_through_wrappers(self):
        self.check_log("23", "", 2)

    def test_unused_iso_may_be_absent(self):
        self.check_log("23", "", 2, absent_iso=True)


if __name__ == "__main__":
    unittest.main()
