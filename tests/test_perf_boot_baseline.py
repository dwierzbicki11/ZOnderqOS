#!/usr/bin/env python3
"""Contract tests for the guest-clock QEMU initialization baseline parser."""
import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "tools" / "perf_boot_baseline.py"
spec = importlib.util.spec_from_file_location("perf_boot_baseline", SCRIPT)
perf = importlib.util.module_from_spec(spec)
spec.loader.exec_module(perf)


class PerfBootBaselineTests(unittest.TestCase):
    def test_realistic_persisted_logs(self):
        first = "[SYS] ZonderqOS kernel successfully booted.\n[PERF] PERF_BOOT_INIT_MS=1234\n"
        second = first + "[SYS] ZonderqOS kernel successfully booted.\n" + (
            "[PERF] PERF_BOOT_INIT_MS=1300\n")
        self.assertEqual(perf.compare(first, second)["first_boot_ms"], 1234)
        self.assertEqual(perf.compare(first, second)["second_boot_ms"], 1300)
        self.assertEqual(perf.compare(first, second)["second_minus_first_ms"], 66)

    def test_missing_first_marker(self):
        with self.assertRaises(ValueError):
            perf.compare("booted", "PERF_BOOT_INIT_MS=20")

    def test_missing_second_marker(self):
        with self.assertRaises(ValueError):
            perf.compare("PERF_BOOT_INIT_MS=20", "PERF_BOOT_INIT_MS=20")

    def test_second_log_must_preserve_first(self):
        with self.assertRaises(ValueError):
            perf.compare("PERF_BOOT_INIT_MS=20", "PERF_BOOT_INIT_MS=21\nPERF_BOOT_INIT_MS=22")

    def test_reject_zero(self):
        with self.assertRaises(ValueError):
            perf.compare("PERF_BOOT_INIT_MS=0", "PERF_BOOT_INIT_MS=0\nPERF_BOOT_INIT_MS=22")

    def test_reject_duplicate_boot_marker(self):
        with self.assertRaises(ValueError):
            perf.compare("PERF_BOOT_INIT_MS=12\nPERF_BOOT_INIT_MS=13",
                         "PERF_BOOT_INIT_MS=12\nPERF_BOOT_INIT_MS=13\nPERF_BOOT_INIT_MS=14")

    def test_accept_crlf_and_log_prefixes(self):
        result = perf.compare(
            "[2026-11-02][PERF] PERF_BOOT_INIT_MS=987\r\n",
            "[2026-11-02][PERF] PERF_BOOT_INIT_MS=987\r\n"
            "[2026-11-02][PERF] PERF_BOOT_INIT_MS=1001\r\n")
        self.assertEqual(result["second_boot_ms"], 1001)

    def test_physical_free_page_samples(self):
        log = ("2026-11-02 heartbeat\\n"
               "2026-11-02 free_pages=1024\\n"
               "2026-11-02 free_pages=1000\\n"
               "2026-11-02 free_pages=1010\\n")
        result = perf.memory_samples(log)
        self.assertEqual(result["samples"], 3)
        self.assertEqual(result["first_free_pages"], 1024)
        self.assertEqual(result["last_free_pages"], 1010)
        self.assertEqual(result["minimum_free_pages"], 1000)
        self.assertEqual(result["maximum_free_pages"], 1024)

    def test_missing_memory_samples(self):
        with self.assertRaises(ValueError):
            perf.memory_samples("heartbeat only")

    def test_does_not_parse_invalid_or_negative_values(self):
        self.assertEqual(perf.samples("PERF_BOOT_INIT_MS=-5\nPERF_BOOT_INIT_MS=NaN"), [])


if __name__ == "__main__":
    unittest.main()
