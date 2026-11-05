"""Regression contract for File Manager cached-search snapshot allocation.

The empty-query path restores the full sorted snapshot. Reserving its
known size avoids repeated List<T> growth on every cleared search.
Filtered queries deliberately remain demand-sized.
"""
from pathlib import Path
import re
import unittest

SOURCE = Path(__file__).resolve().parents[1] / "source/GUI/Application/FileManagerApp.cs"


class FileManagerSearchAllocationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = SOURCE.read_text(encoding="utf-8")
        cls.method = cls.source.split("private void ApplySearchFilter()", 1)[1].split(
            "private static bool OptionalDirectoryExists", 1
        )[0]

    def test_unfiltered_snapshot_reserves_exact_known_capacity(self):
        self.assertRegex(
            self.method,
            r"List<FileEntry> filtered\s*=\s*string\.IsNullOrEmpty\(searchText\)"
            r"\s*\?\s*new List<FileEntry>\(cachedEntries\.Count\)"
            r"\s*:\s*new List<FileEntry>\(\)",
        )

    def test_no_publish_before_final_permission_and_mount_checks(self):
        self.assertLess(self.method.index("publishAllowed = CanReadInSession(currentPath)"),
                        self.method.index("entries.Clear();"))
        self.assertLess(self.method.index("OptionalDirectoryExists(currentPath)"),
                        self.method.index("entries.AddRange(filtered);"))
        self.assertLess(self.method.index("entries.AddRange(filtered);"),
                        self.method.index("EnsureSelectionVisible();"))


if __name__ == "__main__":
    unittest.main()
