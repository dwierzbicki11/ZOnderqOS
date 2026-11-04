import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('metrics', Path(__file__).resolve().parents[1] / 'tools' / 'perf_compare_metrics.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class MetricComparisonTests(unittest.TestCase):
    def setUp(self):
        self.before = [dict(run_id=str(i), environment={'qemu': '8.2', 'vcpus': 1, 'workload': 'idle-60s'}, metrics={'frame_ms': x, 'gc_count': 0}) for i, x in enumerate((10, 12, 14))]
        self.after = [dict(run_id=str(i), environment={'qemu': '8.2', 'vcpus': 1, 'workload': 'idle-60s'}, metrics={'frame_ms': x, 'gc_count': 0}) for i, x in enumerate((9, 13, 11))]

    def test_comparison(self):
        data = module.compare(self.before, self.after)['metrics']['frame_ms']
        self.assertEqual(data['before_median'], 12)
        self.assertEqual(data['after_median'], 11)
        self.assertEqual(data['paired_median_delta'], -1)
        self.assertEqual((data['faster_pairs'], data['slower_pairs']), (2, 1))
        self.assertIsNone(module.compare(self.before, self.after)['metrics']['gc_count']['change_percent'])

    def test_reject_mismatch(self):
        for field, value in [('run_id', 'wrong'), ('environment', {'vcpus': 8}), ('metrics', {'other': 1})]:
            changed = [dict(row) for row in self.after]
            changed[0] = dict(changed[0], **{field: value})
            with self.subTest(field=field), self.assertRaises(ValueError):
                module.compare(self.before, changed)

    def test_reject_bad_values(self):
        for value in (True, float('nan'), float('inf'), -1, '1', 10**400):
            changed = [dict(row) for row in self.after]
            changed[0] = dict(changed[0], metrics={'frame_ms': value, 'gc_count': 0})
            with self.subTest(value=value), self.assertRaises(ValueError):
                module.compare(self.before, changed)

    def test_environment_types_and_nonfinite_values(self):
        for value in (True, 1.0, float('nan'), float('inf'), object()):
            changed = [dict(row) for row in self.after]
            changed[0] = dict(changed[0], environment={'qemu': '8.2', 'vcpus': value, 'workload': 'idle-60s'})
            with self.subTest(value=repr(value)), self.assertRaises(ValueError):
                module.compare(self.before, changed)

    def test_reject_nonfinite_derived_values(self):
        # Every input is finite, but percentage scaling can overflow.
        for before, after in ((1e-308, 1e308), (1e-300, 1e100)):
            old = [dict(row, metrics={'frame_ms': before}) for row in self.before]
            new = [dict(row, metrics={'frame_ms': after}) for row in self.after]
            with self.subTest(before=before, after=after), self.assertRaises(ValueError):
                module.compare(old, new)

    def test_reject_unpaired(self):
        with self.assertRaises(ValueError): module.compare(self.before, self.after[:2])
        with self.assertRaises(ValueError): module.compare(self.before[:2], self.after[:2])
        changed = [dict(row) for row in self.after]
        changed[1]['run_id'] = '0'
        with self.assertRaises(ValueError): module.compare(self.before, changed)


if __name__ == '__main__':
    unittest.main()
