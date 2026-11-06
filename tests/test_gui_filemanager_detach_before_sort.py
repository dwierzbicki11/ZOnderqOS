"""Contract checks: detached FAT32 folders must not spend time sorting stale names."""
from pathlib import Path
import unittest

SOURCE = Path(__file__).resolve().parents[1] / "source/GUI/Application/FileManagerApp.cs"


class DetachedFolderSortGate(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = SOURCE.read_text(encoding="utf-8")
        cls.refresh = cls.source.split("private bool RefreshInternal(string keep)", 1)[1].split("private void Sort(", 1)[0]

    def test_detach_checked_before_sort_and_before_publish(self):
        refresh = self.refresh
        scan = refresh.index("string[] files = Directory.GetFiles(targetPath);")
        sort = refresh.index("Sort(scanned);")
        publish = refresh.index("currentPath = targetPath;")
        checks = []
        start = 0
        while True:
            pos = refresh.find("if (!OptionalDirectoryExists(targetPath))", start)
            if pos < 0:
                break
            checks.append(pos)
            start = pos + 1
        self.assertEqual(len(checks), 2)
        self.assertLess(scan, checks[0])
        self.assertLess(checks[0], sort)
        self.assertLess(sort, checks[1])
        self.assertLess(checks[1], publish)

    def test_early_detach_never_publishes_snapshot(self):
        refresh = self.refresh
        start = refresh.index("if (!OptionalDirectoryExists(targetPath))")
        end = refresh.index("// Sorting can be expensive", start)
        gate = refresh[start:end]
        self.assertIn("ClearDeniedListing();", gate)
        self.assertIn('status = "Location unavailable; press F5 to refresh";', gate)
        self.assertIn("return false;", gate)


if __name__ == "__main__":
    unittest.main()
