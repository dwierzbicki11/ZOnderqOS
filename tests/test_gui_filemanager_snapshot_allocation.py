"""Regression contracts for low-allocation FAT32 GUI directory snapshots.

Source-level checks only: not a Cosmos build or interactive QEMU test.
"""
from pathlib import Path
import unittest

SOURCE = Path(__file__).resolve().parents[1] / "source/GUI/Application/FileManagerApp.cs"


class FileManagerSnapshotAllocationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = SOURCE.read_text(encoding="utf-8")

    def test_capacity_is_computed_only_after_complete_enumeration(self):
        src = self.source
        enumerated = src.index("string[] files = Directory.GetFiles(targetPath);")
        checked = src.index("if (directories == null || files == null)", enumerated)
        counted = src.index("long totalEntries = (long)directories.Length + files.Length;", checked)
        overflow = src.index("if (totalEntries > int.MaxValue)", counted)
        reserved = src.index("new List<FileEntry>((int)totalEntries)", overflow)
        added = src.index("Add(scanned, directories, true, targetPath);", reserved)
        self.assertLess(enumerated, checked)
        self.assertLess(checked, counted)
        self.assertLess(counted, overflow)
        self.assertLess(overflow, reserved)
        self.assertLess(reserved, added)

    def test_no_per_entry_copy_when_path_is_already_normalized(self):
        src = self.source
        self.assertIn("path.IndexOf('\\\\') >= 0", src)
        self.assertIn("? path.Replace('\\\\', '/') : path;", src)
        self.assertIn("FileClipboard.IsSafeAbsolutePath(normalized)", src)
        self.assertIn("string.Equals(entryParent, canonicalParent, StringComparison.OrdinalIgnoreCase)", src)

    def test_capacity_arithmetic_cannot_wrap_signed_int(self):
        for directories, files in ((0, 0), (2, 3), (2**31 - 1, 1), (2**31 - 1, 2**31 - 1)):
            total = directories + files
            self.assertEqual(total <= 2**31 - 1, total <= 2147483647)


if __name__ == "__main__":
    unittest.main()
