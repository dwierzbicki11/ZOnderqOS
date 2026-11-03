import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('perf_compare', Path(__file__).resolve().parents[1] / 'tools' / 'perf_compare_boot.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class CompareTests(unittest.TestCase):
    def setUp(self):
        self.before = [{'first_boot_ms': n, 'second_boot_ms': n-100} for n in (900, 1000, 1100)]
        self.after = [{'first_boot_ms': n, 'second_boot_ms': n-100} for n in (800, 900, 1000)]

    def test_medians(self):
        result = module.compare(self.before, self.after)
        self.assertEqual(result['first_boot_ms']['delta_ms'], -100)
        self.assertEqual(result['second_boot_ms']['change_percent'], -100/9)

    def test_paired_deltas_and_reversals(self):
        before = [{"first_boot_ms": n, "second_boot_ms": n} for n in (100, 200, 300)]
        after = [{"first_boot_ms": n, "second_boot_ms": n} for n in (90, 210, 280)]
        result = module.compare(before, after)
        self.assertEqual(result["first_boot_ms"]["paired_median_delta_ms"], -10)
        self.assertEqual(result["first_boot_ms"]["paired_delta_range_ms"], [-20, 10])
        self.assertEqual(result["first_boot_ms"]["faster_pairs"], 2)
        self.assertEqual(result["first_boot_ms"]["slower_pairs"], 1)
        self.assertEqual(result["first_boot_ms"]["equal_pairs"], 0)

    def test_equal_pairs(self):
        result = module.compare(self.before, self.before)
        self.assertEqual(result["second_boot_ms"]["equal_pairs"], 3)
        self.assertEqual(result["second_boot_ms"]["paired_median_delta_ms"], 0)

    def test_explicit_run_identity(self):
        before = [dict(row, run_id=f"pair-{i}") for i, row in enumerate(self.before)]
        after = [dict(row, run_id=f"pair-{i}") for i, row in enumerate(self.after)]
        self.assertEqual(module.compare(before, after)["pairs"], 3)
        for changed in (
            list(reversed(after)),
            [dict(row, run_id="duplicate") for row in after],
            [dict(row, run_id="") if i == 0 else row for i, row in enumerate(after)],
            [dict(row) if i == 0 else row for i, row in enumerate(after)],
        ):
            with self.subTest(changed=changed), self.assertRaises(ValueError):
                module.compare(before, changed)

    def test_unpaired(self):
        with self.assertRaises(ValueError): module.compare(self.before, self.after[:2])

    def test_too_few(self):
        with self.assertRaises(ValueError): module.compare(self.before[:2], self.after[:2])

    def test_reject_zero_and_bool(self):
        for invalid in (0, True, '1', -5):
            changed = [dict(row) for row in self.before]
            changed[0]['first_boot_ms'] = invalid
            with self.assertRaises(ValueError): module.compare(changed, self.after)

if __name__ == '__main__': unittest.main()
