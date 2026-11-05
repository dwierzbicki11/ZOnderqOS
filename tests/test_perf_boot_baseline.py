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
        log = ("2026-11-02 heartbeat\n"
               "2026-11-02 free_pages=1024\n"
               "2026-11-02 free_pages=1000\n"
               "2026-11-02 free_pages=1010\n")
        result = perf.memory_samples(log)
        self.assertEqual(result["samples"], 3)
        self.assertEqual(result["first_free_pages"], 1024)
        self.assertEqual(result["last_free_pages"], 1010)
        self.assertEqual(result["minimum_free_pages"], 1000)
        self.assertEqual(result["maximum_free_pages"], 1024)
        self.assertEqual(result["last_minus_first_free_pages"], -14)
        self.assertEqual(result["decreasing_intervals"], 1)
        self.assertEqual(result["increasing_intervals"], 1)
        self.assertEqual(result["unchanged_intervals"], 0)
        self.assertEqual(result["total_decrease_pages"], 24)
        self.assertEqual(result["total_increase_pages"], 10)
        self.assertEqual(result["largest_interval_decrease_pages"], 24)

    def test_memory_trend_constant_and_single_sample(self):
        constant = perf.memory_samples("free_pages=8\nfree_pages=8\nfree_pages=8\n")
        self.assertEqual(constant["last_minus_first_free_pages"], 0)
        self.assertEqual(constant["unchanged_intervals"], 2)
        self.assertEqual(constant["decreasing_intervals"], 0)
        self.assertEqual(constant["increasing_intervals"], 0)
        single = perf.memory_samples("free_pages=7\n")
        self.assertEqual(single["last_minus_first_free_pages"], 0)
        self.assertEqual(single["unchanged_intervals"], 0)
        self.assertEqual(single["total_decrease_pages"], 0)
        self.assertEqual(single["total_increase_pages"], 0)
        self.assertEqual(single["largest_interval_decrease_pages"], 0)

    def test_reject_embedded_or_malformed_marker_values(self):
        log = ("XPERF_BOOT_INIT_MS=12\nPERF_BOOT_INIT_MS=12ms\n"
               "PERF_BOOT_INIT_MS=12.5\nPERF_BOOT_INIT_MS=12_3\n"
               "PERF_BOOT_INIT_MS=12+3\nPERF_BOOT_INIT_MS=42\n")
        self.assertEqual(perf.samples(log), [42])

    def test_reject_embedded_or_malformed_memory_values(self):
        log = ("not_free_pages=500\nfree_pages=1000bytes\n"
               "free_pages=12.5\nfree_pages=10_00\nfree_pages=700\n")
        result = perf.memory_samples(log)
        self.assertEqual(result["samples"], 1)
        self.assertEqual(result["last_free_pages"], 700)

    def test_reject_punctuation_attached_to_boot_measurements(self):
        log = ("PERF_BOOT_INIT_MS=12/3\nPERF_BOOT_INIT_MS=14%\n"
               "PERF_BOOT_INIT_MS=15,garbage\nPERF_BOOT_INIT_MS=16;bad\n"
               "[PERF] PERF_BOOT_INIT_MS=42 next=ok\n")
        self.assertEqual(perf.samples(log), [42])

    def test_reject_punctuation_attached_to_memory_measurements(self):
        log = ("free_pages=123/4\nfree_pages=125%\n"
               "free_pages=126,bad\nfree_pages=127;bad\n"
               "[MEM] free_pages=800 free_bytes=3276800\n")
        result = perf.memory_samples(log)
        self.assertEqual(result["samples"], 1)
        self.assertEqual(result["last_free_pages"], 800)

    def test_missing_memory_samples(self):
        with self.assertRaises(ValueError):
            perf.memory_samples("heartbeat only")

    def test_does_not_parse_invalid_or_negative_values(self):
        self.assertEqual(perf.samples("PERF_BOOT_INIT_MS=-5\nPERF_BOOT_INIT_MS=NaN"), [])


if __name__ == "__main__":
    unittest.main()
