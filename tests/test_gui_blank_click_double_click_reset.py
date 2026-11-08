"""Regression contract: blank chrome and outside-client presses break a pending double-click."""
from pathlib import Path
import unittest

SOURCE = Path(__file__).resolve().parents[1] / "source/GUI/Application/FileManagerApp.cs"


class FileManagerBlankClickTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = SOURCE.read_text(encoding="utf-8")
        cls.mouse = cls.source.split("public override void HandleMouse(int mouseX", 1)[1].split("private void ResetClickSequence()", 1)[0]

    def test_outside_client_press_resets_click(self):
        self.assertIn(
            "if (x < 0 || y < 0 || x >= view.Width || y >= view.Height)\n"
            "            {\n"
            "                if (left && !oldLeft)\n"
            "                    ResetClickSequence();\n"
            "                return;\n"
            "            }", self.mouse)

    def test_blank_chrome_press_resets_click(self):
        self.assertIn(
            "if (y < ContentTop || y >= contentBottom)\n"
            "            {\n"
            "                ResetClickSequence();\n"
            "                return;\n"
            "            }", self.mouse)

    def test_reset_precedes_double_click_comparison(self):
        self.assertLess(self.mouse.index("if (y < ContentTop || y >= contentBottom)"),
                        self.mouse.index("if (selectedIndex == index && lastClickIndex == index"))


if __name__ == "__main__":
    unittest.main()
