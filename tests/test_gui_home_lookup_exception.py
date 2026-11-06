"""Static regression gate for home lookup failures in the File Manager."""
from pathlib import Path
import unittest

SOURCE = Path(__file__).resolve().parents[1] / "source/GUI/Application/FileManagerApp.cs"


class HomeLookupExceptionContract(unittest.TestCase):
    def test_home_metadata_lookup_is_inside_exception_guard(self):
        source = SOURCE.read_text(encoding="utf-8")
        method = source.split("private static string ResolveSessionHome()", 1)[1].split(
            "public FileManagerApp(", 1
        )[0]
        start = method.index("try\n")
        lookup = method.index("UserManager.GetHomeDirectory(SecurityContext.CurrentUser)")
        exists = method.index("Directory.Exists(home)")
        handler = method.index("catch (Exception)")
        fallback = method.index('return "/";', handler)
        self.assertLess(start, lookup)
        self.assertLess(lookup, exists)
        self.assertLess(exists, handler)
        self.assertLess(handler, fallback)


if __name__ == "__main__":
    unittest.main()
