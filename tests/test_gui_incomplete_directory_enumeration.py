"""Static regression gate: never publish a partial FAT32 directory listing.

This is a source-contract test, not a Cosmos/QEMU runtime test.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "source/GUI/Application/FileManagerApp.cs"


class IncompleteEnumerationContract(unittest.TestCase):
    def test_null_directory_or_file_array_aborts_before_publish(self):
        source = SOURCE.read_text(encoding="utf-8")
        method = source.split("private bool RefreshInternal(string keep)", 1)[1]
        method = method.split("private void ", 1)[0]
        directories = method.index("string[] directories = Directory.GetDirectories(targetPath);")
        files = method.index("string[] files = Directory.GetFiles(targetPath);")
        guard = method.index("if (directories == null || files == null)")
        failure = method.index('throw new IOException("Incomplete directory enumeration");')
        add_directories = method.index("Add(scanned, directories, true);")
        add_files = method.index("Add(scanned, files, false);")
        publish = method.index("entries.AddRange(scanned);")
        self.assertTrue(directories < files < guard < failure < add_directories < add_files < publish)

    def test_cached_snapshot_is_not_cleared_before_enumeration(self):
        source = SOURCE.read_text(encoding="utf-8")
        method = source.split("private bool RefreshInternal(string keep)", 1)[1]
        self.assertLess(method.index("throw new IOException(\"Incomplete directory enumeration\");"),
                        method.index("entries.Clear();"))


if __name__ == "__main__":
    unittest.main()
