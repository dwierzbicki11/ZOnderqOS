"""Static regression contract: detached FAT32 refresh must fail closed.

This is a source contract, not a QEMU boot or an interactive GUI test.
"""
from pathlib import Path
import unittest

SOURCE = Path(__file__).resolve().parents[1] / "source/GUI/Application/FileManagerApp.cs"


class RefreshDetachPublishTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        text = SOURCE.read_text(encoding="utf-8")
        cls.refresh = text.split("private bool RefreshInternal(string keep)", 1)[1].split(
            "private void ApplySearchFilter()", 1
        )[0]

    def test_detach_check_after_final_acl_before_publication(self):
        code = self.refresh
        acl = code.index("bool publishReadAllowed;")
        detached = code.index("if (!OptionalDirectoryExists(targetPath))", acl)
        publish = code.index("entries.AddRange(scanned);", detached)
        self.assertLess(acl, detached)
        self.assertLess(detached, publish)

    def test_detach_fails_closed_without_publishing(self):
        code = self.refresh
        start = code.index("if (!OptionalDirectoryExists(targetPath))")
        block = code[start:code.index("// An explicit F5", start)]
        self.assertIn("ClearDeniedListing();", block)
        self.assertIn("return false;", block)
        self.assertIn("loadedDirectoryPath, targetPath", block)


if __name__ == "__main__":
    unittest.main()
