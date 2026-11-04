"""Regression guard: cached FAT32 search results must not survive detach.

This is a source contract, not an interactive QEMU boot test.
"""
from pathlib import Path
import unittest

SOURCE = Path(__file__).resolve().parents[1] / "source/GUI/Application/FileManagerApp.cs"


class CachedSearchDetachContract(unittest.TestCase):
    def test_search_revalidates_directory_before_publishing_cached_tiles(self):
        source = SOURCE.read_text(encoding="utf-8")
        start = source.index("private void ApplySearchFilter()")
        end = source.index("private static bool OptionalDirectoryExists(", start)
        body = source[start:end]
        self.assertIn("if (!OptionalDirectoryExists(currentPath))", body)
        check = body.index("if (!OptionalDirectoryExists(currentPath))")
        clear = body.index("ClearDeniedListing();", check)
        stop = body.index("return;", clear)
        publish = body.index("entries.AddRange(filtered);")
        self.assertLess(check, clear)
        self.assertLess(clear, stop)
        self.assertLess(stop, publish)

    def test_detach_probe_is_fail_closed(self):
        source = SOURCE.read_text(encoding="utf-8")
        start = source.index("private static bool OptionalDirectoryExists(")
        end = source.index("private void RebuildSidebar()", start)
        body = source[start:end]
        self.assertIn("Directory.Exists(path)", body)
        self.assertIn("catch (Exception)", body)
        self.assertIn("return false;", body)


if __name__ == "__main__":
    unittest.main()
