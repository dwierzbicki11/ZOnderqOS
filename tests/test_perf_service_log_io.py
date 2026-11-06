"""Host-only regression tests for the service-log A/B harness.

Run: python3 -m unittest discover -s tests -p 'test_perf_service_log_io.py'
Also run with python3 -O to ensure validation is not optimized away.
"""
import importlib.util
from pathlib import Path
import tempfile
import unittest

MODULE = Path(__file__).resolve().parents[1] / "tools" / "perf_service_log_io.py"
spec = importlib.util.spec_from_file_location("perf_service_log_io", MODULE)
benchmark = importlib.util.module_from_spec(spec)
spec.loader.exec_module(benchmark)


class ServiceLogBenchmarkTests(unittest.TestCase):
    def compare(self, iterations, initial=b""):
        with tempfile.TemporaryDirectory() as root:
            results = {}
            for mode in ("stat", "tracked"):
                path = Path(root) / (mode + ".log")
                path.write_bytes(initial)
                results[mode] = benchmark.run(str(path), iterations, mode)
            a, b = results["stat"], results["tracked"]
            self.assertEqual(a[2:], b[2:], "rotations and bytes must match")
            self.assertEqual(a[1], iterations)
            self.assertEqual(b[1], 0)
            self.assertLessEqual(len(a[3]), benchmark.LIMIT)
            return a, b

    def test_single_append(self):
        a, _ = self.compare(1)
        self.assertEqual(a[2], 0)
        self.assertEqual(a[3], benchmark.ENTRY)

    def test_multiple_rotations(self):
        a, _ = self.compare(6000)
        self.assertGreaterEqual(a[2], 2)

    def test_existing_log_at_limit(self):
        a, _ = self.compare(1, b"x" * benchmark.LIMIT)
        self.assertEqual(a[2], 1)
        self.assertEqual(a[3], benchmark.ROTATED)

    def test_existing_log_just_below_limit(self):
        a, _ = self.compare(1, b"x" * (benchmark.LIMIT - len(benchmark.ENTRY)))
        self.assertEqual(a[2], 0)
        self.assertEqual(len(a[3]), benchmark.LIMIT)

    def test_rotation_boundary(self):
        a, _ = self.compare(1, b"x" * (benchmark.LIMIT - len(benchmark.ENTRY) + 1))
        self.assertEqual(a[2], 1)
        self.assertEqual(a[3], benchmark.ROTATED)


if __name__ == "__main__":
    unittest.main()
