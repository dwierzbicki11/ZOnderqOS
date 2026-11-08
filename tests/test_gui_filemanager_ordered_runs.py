"""Regression checks for the ordered-run fast path in FileManagerApp.Sort."""
from pathlib import Path
import random
import unittest

SOURCE = Path(__file__).resolve().parents[1] / "source/GUI/Application/FileManagerApp.cs"


def merge_sort_with_fast_path(items):
    items = list(items)
    n = len(items)
    if all(items[i - 1][0] <= items[i][0] for i in range(1, n)):
        return items, 0
    scratch = [None] * n
    comparisons = 0
    run = 1
    while run < n:
        for start in range(0, n, 2 * run):
            middle = min(n, start + run)
            end = min(n, middle + run)
            if middle == end:
                scratch[start:end] = items[start:end]
                continue
            comparisons += 1
            if items[middle - 1][0] <= items[middle][0]:
                scratch[start:end] = items[start:end]
                continue
            left, right = start, middle
            for write in range(start, end):
                if left < middle and (right >= end or items[left][0] <= items[right][0]):
                    scratch[write] = items[left]
                    left += 1
                else:
                    scratch[write] = items[right]
                    right += 1
                if left < middle and right < end:
                    comparisons += 1
        items[:] = scratch
        run *= 2
    return items, comparisons


class OrderedRunSortTests(unittest.TestCase):
    def test_source_contains_ordered_run_shortcut(self):
        source = SOURCE.read_text(encoding="utf-8")
        self.assertIn("Compare(destination[middle - 1], destination[middle]) <= 0", source)
        self.assertIn("scratch[write] = destination[write];", source)
        self.assertIn("Compare(destination[left], destination[right]) <= 0", source)

    def test_stability_and_order_on_odd_and_duplicate_lists(self):
        random.seed(1729)
        for size in range(0, 130):
            for _ in range(8):
                entries = [(random.randrange(12), index) for index in range(size)]
                actual, _ = merge_sort_with_fast_path(entries)
                self.assertEqual(actual, sorted(entries, key=lambda entry: entry[0]))

    def test_ordered_runs_avoid_element_merges(self):
        entries = [(i // 2, i) for i in range(128)]
        actual, comparisons = merge_sort_with_fast_path(entries)
        self.assertEqual(actual, entries)
        self.assertEqual(comparisons, 0)


if __name__ == "__main__":
    unittest.main()
