"""Regression contract for the GUI file manager's stable array merge sort."""
from pathlib import Path
import random
import unittest

SOURCE = Path(__file__).resolve().parents[1] / "source/GUI/Application/FileManagerApp.cs"


def merge_sort(items):
    """Mirror the bottom-up merge control flow; retain identity for stable ties."""
    count = len(items)
    if count < 2:
        return list(items)
    source, target = list(items), [None] * count

    def key(entry):
        return (not entry[0], entry[1].lower())

    run = 1
    while run < count:
        start = 0
        while start < count:
            middle = start + min(run, count - start)
            end = middle + min(run, count - middle)
            left, right = start, middle
            if middle == end or key(source[middle - 1]) <= key(source[middle]):
                target[start:end] = source[start:end]
            else:
                for write in range(start, end):
                    if left < middle and (right >= end or key(source[left]) <= key(source[right])):
                        target[write] = source[left]
                        left += 1
                    else:
                        target[write] = source[right]
                        right += 1
            start = end
        source, target = target, source
        run = count if run > count // 2 else run * 2
    return source


class FileManagerMergeSortTests(unittest.TestCase):
    def test_production_swaps_arrays_and_publishes_once(self):
        source = SOURCE.read_text(encoding="utf-8")
        start = source.index("private void Sort(List<FileEntry> destination)")
        end = source.index("private int Compare(FileEntry a, FileEntry b)", start)
        body = source[start:end]
        self.assertIn("FileEntry[] source = destination.ToArray();", body)
        self.assertIn("FileEntry[] target = new FileEntry[count];", body)
        self.assertIn("source = target;", body)
        self.assertIn("target = previous;", body)
        self.assertIn("destination[i] = source[i];", body)
        self.assertNotIn("destination[i] = scratch[i];", body)

    def test_random_stable_sort_and_odd_sizes(self):
        rng = random.Random(3095)
        for count in (0, 1, 2, 3, 5, 7, 8, 9, 31, 32, 33, 257, 1025):
            for _ in range(8):
                items = [(rng.choice((True, False)), rng.choice(("Alpha", "alpha", "beta", "BETA", "z")), i)
                         for i in range(count)]
                expected = sorted(items, key=lambda e: (not e[0], e[1].lower()))
                self.assertEqual(merge_sort(items), expected)


if __name__ == "__main__":
    unittest.main()
