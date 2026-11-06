"""Regression gate for FAT32 directory-entry path normalization.

Run: python3 tests/test_gui_filemanager_entry_normalization.py
"""
from pathlib import Path
import unittest

SOURCE = Path(__file__).resolve().parents[1] / "source/GUI/Application/FileManagerApp.cs"


def accepted_child(raw, parent):
    if not raw or any(ord(c) < 32 or ord(c) == 127 for c in raw):
        return None
    normalized = raw.replace("\\", "/")
    if not normalized.startswith("/"):
        return None
    clean = normalized.rstrip("/")
    slash = clean.rfind("/")
    if slash < 0:
        return None
    entry_parent = clean[:slash] or "/"
    if entry_parent.casefold() != parent.casefold():
        return None
    name = clean[slash + 1:]
    return (name, clean) if name and name not in (".", "..") else None


class DirectoryEntryNormalizationTests(unittest.TestCase):
    def test_production_uses_single_trimmed_path(self):
        source = SOURCE.read_text(encoding="utf-8")
        self.assertIn("string cleanPath = normalized.TrimEnd('/');", source)
        self.assertIn("int lastSlash = cleanPath.LastIndexOf('/');", source)
        self.assertIn("string name = cleanPath.Substring(lastSlash + 1);", source)
        self.assertNotIn("Path.GetFileName(cleanPath)", source)
        self.assertNotIn("normalized.TrimEnd('/').LastIndexOf('/')", source)
        self.assertNotIn("Path.GetFileName(normalized.TrimEnd('/'))", source)

    def test_production_publishes_canonical_path(self):
        source = SOURCE.read_text(encoding="utf-8")
        self.assertIn("destination.Add(new FileEntry(name, cleanPath, directory));", source)
        self.assertNotIn("destination.Add(new FileEntry(name, normalized, directory));", source)

    def test_file_entry_publication_is_executable_code(self):
        source = SOURCE.read_text(encoding="utf-8")
        statement = "destination.Add(new FileEntry(name, cleanPath, directory));"
        # A previous edit inserted literal backslash-n characters into a
        # // comment, swallowing the Add call while still passing assertIn.
        lines = [line for line in source.splitlines() if statement in line]
        self.assertEqual(len(lines), 1)
        self.assertEqual(lines[0].strip(), statement)
        self.assertNotIn(r"\\n                    destination.Add", source)

    def test_directory_children_and_trailing_separator(self):
        for raw, parent, expected in [
            ("/DATA/one.txt", "/DATA", "one.txt"),
            ("/DATA/sub/", "/DATA", "sub"),
            ("/DATA/sub////", "/DATA", "sub"),
            ("/DATA/", "/DATA", None),
            ("/", "/", None),
            ("/single.txt", "/", "single.txt"),
            ("/DATA/UPPER.TXT", "/data", "UPPER.TXT"),
            ("/DATA/other/nested", "/DATA", None),
            ("/DATABASE/file", "/DATA", None),
            ("/DATA/..", "/DATA", None),
            ("/DATA/.", "/DATA", None),
            ("/DATA/hi\\there", "/DATA", None),
            ("/DATA/bad\x00name", "/DATA", None),
            ("/data/Case.TXT", "/DATA", "Case.TXT"),
        ]:
            with self.subTest(raw=raw):
                self.assertEqual(accepted_child(raw, parent),
                                 (expected, raw.replace("\\", "/").rstrip("/")) if expected else None)


if __name__ == "__main__":
    unittest.main()
