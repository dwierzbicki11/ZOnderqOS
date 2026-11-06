"""Host-only correctness guards for the same-handle service log benchmark."""
import importlib.util
import contextlib
import io
from unittest import mock
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / 'tools' / 'perf_service_log_stream.py'
spec = importlib.util.spec_from_file_location('perf_service_log_stream', SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ServiceLogStreamTests(unittest.TestCase):
    def test_invalid_arguments(self):
        with tempfile.TemporaryDirectory() as root:
            path = str(Path(root) / 'log')
            for count, mode in ((0, 'stat'), (-1, 'stream'), (1, 'unknown')):
                with self.subTest(count=count, mode=mode):
                    with self.assertRaises(ValueError):
                        module.run(path, count, mode)
        for iterations, repeats in ((0, 1), (1, 0), (-1, 1)):
            with self.assertRaises(ValueError):
                module.benchmark(iterations, repeats)

    def test_single_append_and_existing_log(self):
        for mode in ('stat', 'stream'):
            with self.subTest(mode=mode), tempfile.TemporaryDirectory() as root:
                path = Path(root) / 'service.log'
                path.write_bytes(b'preexisting\n')
                elapsed, reads, rotations, data = module.run(str(path), 1, mode)
                self.assertGreaterEqual(elapsed, 0)
                self.assertEqual(reads, 1 if mode == 'stat' else 0)
                self.assertEqual(rotations, 0)
                self.assertEqual(data, b'preexisting\n' + module.ENTRY)

    def test_rotation_boundary_and_multiple_rotations(self):
        for initial in (module.LIMIT - len(module.ENTRY),
                        module.LIMIT - len(module.ENTRY) + 1):
            results = []
            for mode in ('stat', 'stream'):
                with tempfile.TemporaryDirectory() as root:
                    path = Path(root) / 'service.log'
                    path.write_bytes(b'x' * initial)
                    result = module.run(str(path), 1, mode)
                    self.assertEqual(result[2], int(initial + len(module.ENTRY) > module.LIMIT))
                    self.assertEqual(result[3], path.read_bytes())
                    results.append(result[2:])
            self.assertEqual(results[0], results[1])
        with tempfile.TemporaryDirectory() as root:
            results = [module.run(str(Path(root) / (mode + '.log')), 6000, mode)
                       for mode in ('stat', 'stream')]
            self.assertGreater(results[0][2], 1)
            self.assertEqual(results[0][2:], results[1][2:])
            self.assertLessEqual(len(results[0][3]), module.LIMIT)

    def test_cli_reports_paired_ratios_not_ratio_of_medians(self):
        samples = {'stat': [20.0, 90.0, 40.0],
                   'stream': [10.0, 30.0, 80.0]}
        output = io.StringIO()
        with mock.patch.object(module, 'benchmark', return_value=samples), \
             mock.patch('sys.argv', ['perf_service_log_stream.py', '--iterations', '1', '--repeats', '3']), \
             contextlib.redirect_stdout(output):
            module.main()
        report = output.getvalue()
        self.assertIn('paired_ratio_stat_over_stream: median=2.000 min=0.500 max=3.000', report)
        self.assertIn('stream_faster_pairs=2/3', report)

    def test_benchmark_repeats(self):
        samples = module.benchmark(12, 3)
        self.assertEqual(set(samples), {'stat', 'stream'})
        self.assertEqual(len(samples['stat']), 3)
        self.assertEqual(len(samples['stream']), 3)
        self.assertTrue(all(value >= 0 for series in samples.values() for value in series))


if __name__ == '__main__':
    unittest.main()
