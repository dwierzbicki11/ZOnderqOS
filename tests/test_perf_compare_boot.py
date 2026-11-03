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
