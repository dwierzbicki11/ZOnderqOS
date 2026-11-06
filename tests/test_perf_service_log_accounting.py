"""Regression tests for service log benchmark assumptions and invalidation.

Host-only: these tests do not establish Cosmos/FAT32 performance.
"""
import importlib.util
from pathlib import Path
import tempfile
import unittest

MODULE = Path(__file__).resolve().parents[1] / "tools" / "perf_service_log_io.py"
spec = importlib.util.spec_from_file_location("perf_service_log_io", MODULE)
benchmark = importlib.util.module_from_spec(spec)
spec.loader.exec_module(benchmark)


class ServiceLogAccountingAssumptions(unittest.TestCase):
    def test_restart_reads_existing_length(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / "service.log"
            path.write_bytes(b"x" * (benchmark.LIMIT - len(benchmark.ENTRY) + 1))
            elapsed, stats, rotations, data = benchmark.run(str(path), 1, "tracked")
            self.assertGreaterEqual(elapsed, 0)
            self.assertEqual(stats, 1)  # Initial size read is counted and timed.
            self.assertEqual(rotations, 1)
            self.assertEqual(data, benchmark.ROTATED)

    def test_external_append_invalidates_cached_length(self):
        # An external writer can increase file length after a cached snapshot.
        # A production optimization must revalidate or otherwise coordinate writes.
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / "service.log"
            path.write_bytes(b"x" * (benchmark.LIMIT - len(benchmark.ENTRY)))
            cached = path.stat().st_size
            with path.open("ab") as stream:
                stream.write(b"external change")
            with path.open("ab") as stream:
                stream.write(benchmark.ENTRY)
            self.assertEqual(cached + len(benchmark.ENTRY), benchmark.LIMIT)
            self.assertGreater(path.stat().st_size, benchmark.LIMIT)

    def test_external_truncation_invalidates_cached_length(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / "service.log"
            path.write_bytes(b"x" * benchmark.LIMIT)
            cached = path.stat().st_size
            path.write_bytes(b"")
            with path.open("ab") as stream:
                stream.write(benchmark.ENTRY)
            self.assertGreater(cached + len(benchmark.ENTRY), benchmark.LIMIT)
            self.assertEqual(path.stat().st_size, len(benchmark.ENTRY))

    def test_failed_append_must_not_advance_accounting(self):
        # Simulate an append that fails before any data reaches the file.
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / "service.log"
            path.write_bytes(b"prefix")
            before = path.stat().st_size
            try:
                raise OSError("injected write failure")
            except OSError:
                pass
            self.assertEqual(path.stat().st_size, before)


if __name__ == "__main__":
    unittest.main()
