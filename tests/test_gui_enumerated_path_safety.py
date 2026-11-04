"""Regression contract: malformed FAT32 entries must not become clickable tiles."""
from pathlib import Path
import unittest

SOURCE = Path(__file__).resolve().parents[1] / "source/GUI/Application/FileManagerApp.cs"


class EnumeratedPathSafety(unittest.TestCase):
    def test_validate_absolute_path_before_publishing_tile(self):
        source = SOURCE.read_text(encoding="utf-8")
        start = source.index("private void Add(List<FileEntry> destination")
        end = source.index("private void Sort(", start)
        add = source[start:end]
        self.assertLess(add.index("string normalized = path.Replace"),
                        add.index("FileClipboard.IsSafeAbsolutePath(normalized)"))
        self.assertLess(add.index("FileClipboard.IsSafeAbsolutePath(normalized)"),
                        add.index("destination.Add(new FileEntry"))
        self.assertIn("if (!FileClipboard.IsSafeAbsolutePath(normalized))\n                        continue;", add)


if __name__ == "__main__":
    unittest.main()
