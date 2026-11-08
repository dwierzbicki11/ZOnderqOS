"""Static contract for per-entry FAT32 enumeration fault isolation.

This is a source-level gate, not a boot or runtime filesystem test.
"""
from pathlib import Path
import unittest

SOURCE = Path(__file__).resolve().parents[1] / "source/GUI/Application/FileManagerApp.cs"


class FileManagerMalformedEntries3095(unittest.TestCase):
    def test_control_characters_are_rejected_before_publishing(self):
        source = SOURCE.read_text(encoding="utf-8")
        start = source.index("private void Add(List<FileEntry> destination")
        end = source.index("private void Sort(List<FileEntry> destination", start)
        body = source[start:end]
        self.assertIn("bool invalidCharacter = false;", body)
        self.assertIn("path[character] < ' '", body)
        self.assertIn("if (invalidCharacter)", body)
        self.assertLess(body.index("if (invalidCharacter)"), body.index("destination.Add("))

    def test_invalid_entries_do_not_abort_directory_scan(self):
        source = SOURCE.read_text(encoding="utf-8")
        start = source.index("private void Add(List<FileEntry> destination")
        end = source.index("private void Sort(List<FileEntry> destination", start)
        body = source[start:end]
        self.assertIn("string.IsNullOrWhiteSpace(path)", body)
        self.assertIn("try\n                {", body)
        self.assertIn('string name = cleanPath.Substring(lastSlash + 1);', body)
        self.assertIn('name == "." || name == ".."', body)
        self.assertIn("catch (Exception)", body)
        self.assertIn("destination.Add(new FileEntry(name, cleanPath, directory));", body)
        self.assertLess(body.index("string name = cleanPath.Substring"), body.index("destination.Add("))


if __name__ == "__main__":
    unittest.main()
