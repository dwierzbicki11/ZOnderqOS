"""Regression guard: FAT32 create must fail closed on incomplete name scans."""
import pathlib
import unittest

SOURCE = (pathlib.Path(__file__).resolve().parents[1] /
          "source/GUI/Application/FileManagerApp.cs")


class Fat32CreateEnumerationTests(unittest.TestCase):
    def test_null_enumeration_rejected_before_collision_check_and_write(self):
        source = SOURCE.read_text(encoding="utf-8")
        start = source.index("private void FinishCreate()")
        end = source.index("private void RefreshAll()", start)
        body = source[start:end]
        files = body.index("string[] existingFiles = Directory.GetFiles(currentPath);")
        dirs = body.index("string[] existingDirectories = Directory.GetDirectories(currentPath);")
        guard = body.index("if (existingFiles == null || existingDirectories == null)")
        collision = body.index("FileManagerNamePolicy.HasCaseInsensitiveCollision(name,")
        create = body.index("FileManagerFileCreation.CreateEmptyFile(path);")
        self.assertLess(files, guard)
        self.assertLess(dirs, guard)
        self.assertLess(guard, collision)
        self.assertLess(collision, create)
        self.assertIn('status = "Cannot verify FAT32 names; create cancelled";', body[guard:collision])
        self.assertIn("return;", body[guard:collision])


if __name__ == "__main__":
    unittest.main()
