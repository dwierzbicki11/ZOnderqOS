"""Static regression gate for FAT32 directory enumeration containment."""
import pathlib
import unittest

SOURCE = pathlib.Path(__file__).resolve().parents[1] / "source/GUI/Application/FileManagerApp.cs"


class EnumerationContainment(unittest.TestCase):
    def test_direct_child_check_precedes_publication(self):
        source = SOURCE.read_text(encoding="utf-8")
        method = source.split("private void Add(List<FileEntry> destination", 1)[1].split("\n        }", 1)[0]
        self.assertIn("string parentPath)", method)
        self.assertIn("string entryParent =", method)
        self.assertIn("StringComparison.OrdinalIgnoreCase", method)
        self.assertLess(method.index("FileClipboard.IsSafeAbsolutePath(normalized)"),
                        method.index("string entryParent ="))
        self.assertLess(method.index("string entryParent ="),
                        method.index("destination.Add(new FileEntry("))

    def test_parent_normalization_is_outside_entry_loop(self):
        source = SOURCE.read_text(encoding="utf-8")
        method = source.split("private void Add(List<FileEntry> destination", 1)[1].split("\n        }", 1)[0]
        normalization = method.index("string canonicalParent = parentPath.Replace(")
        loop = method.index("for (int i = 0; i < paths.Length; i++)")
        self.assertLess(normalization, loop)
        self.assertEqual(method.count("string canonicalParent ="), 1)
        self.assertIn("if (paths == null || string.IsNullOrEmpty(parentPath))", method)

    def test_scan_supplies_actual_target_directory(self):
        source = SOURCE.read_text(encoding="utf-8")
        self.assertIn("Add(scanned, directories, true, targetPath);", source)
        self.assertIn("Add(scanned, files, false, targetPath);", source)


if __name__ == "__main__":
    unittest.main()
