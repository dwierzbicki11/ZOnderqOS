#!/usr/bin/env python3
"""Stage 2 Stabilization and GC memory teardown regression tests.

Verifies deterministic object and buffer teardown across GUI applications,
Window child widget unlinking, and dynamic idle loop sleep in GuiManager.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
WINDOW = (ROOT / "source/GUI/Window.cs").read_text(encoding="utf-8")
APP = (ROOT / "source/GUI/Application/Application.cs").read_text(encoding="utf-8")
IMAGE_VIEWER = (ROOT / "source/GUI/Application/ImageViewerApp.cs").read_text(encoding="utf-8")
PAINT = (ROOT / "source/GUI/Application/PaintApp.cs").read_text(encoding="utf-8")
HEX_VIEWER = (ROOT / "source/GUI/Application/HexViewerApp.cs").read_text(encoding="utf-8")
FILE_MANAGER = (ROOT / "source/GUI/Application/FileManagerApp.cs").read_text(encoding="utf-8")
BASIC_IDE = (ROOT / "source/GUI/Application/BasicIdeApp.cs").read_text(encoding="utf-8")
GUI_MGR = (ROOT / "source/GUI/GuiManager.cs").read_text(encoding="utf-8")


class Stage2StabilizationTests(unittest.TestCase):
    def test_window_clear_children_unlinks_button_clicks(self):
        self.assertIn("public void ClearChildren()", WINDOW)
        clear_body = WINDOW.split("public void ClearChildren()", 1)[1].split("private void MoveTo", 1)[0]
        self.assertIn("btn.OnClick = null;", clear_body)
        self.assertIn("Children.Clear();", clear_body)

    def test_application_close_invokes_clear_children_and_nulls_close_action(self):
        close_body = APP.split("public virtual void Close()", 1)[1].split("}", 1)[0]
        self.assertIn("Window.ClearChildren();", close_body)
        self.assertIn("Window.CloseAction = null;", close_body)
        self.assertIn("Window.Visible = false;", close_body)

    def test_image_viewer_close_releases_large_buffers(self):
        self.assertIn("public override void Close()", IMAGE_VIEWER)
        close_body = IMAGE_VIEWER.split("public override void Close()", 1)[1].split("}", 1)[0]
        self.assertIn("sourcePixels = null;", close_body)
        self.assertIn("previewBitmap = null;", close_body)
        self.assertIn("base.Close();", close_body)

    def test_paint_app_close_releases_canvas_buffer_and_guards_null(self):
        self.assertIn("public override void Close()", PAINT)
        close_body = PAINT.split("public override void Close()", 1)[1].split("}", 1)[0]
        self.assertIn("canvasBuffer = null;", close_body)
        self.assertIn("base.Close();", close_body)
        self.assertIn("canvasBuffer == null", PAINT)

    def test_hex_viewer_close_releases_file_bytes(self):
        self.assertIn("public override void Close()", HEX_VIEWER)
        close_body = HEX_VIEWER.split("public override void Close()", 1)[1].split("}", 1)[0]
        self.assertIn("fileBytes = null;", close_body)
        self.assertIn("base.Close();", close_body)

    def test_file_manager_close_clears_cached_entries(self):
        self.assertIn("public override void Close()", FILE_MANAGER)
        close_body = FILE_MANAGER.split("public override void Close()", 1)[1].split("}", 1)[0]
        self.assertIn("entries.Clear();", close_body)
        self.assertIn("cachedEntries.Clear();", close_body)
        self.assertIn("sidebarEntries.Clear();", close_body)
        self.assertIn("base.Close();", close_body)

    def test_basic_ide_close_clears_lines_and_stops_speaker(self):
        close_body = BASIC_IDE.split("public override void Close()", 1)[1].split("}", 1)[0]
        self.assertIn("Speaker.Stop();", close_body)
        self.assertIn("sourceLines?.Clear();", close_body)
        self.assertIn("outputLines?.Clear();", close_body)
        self.assertIn("base.Close();", close_body)

    def test_gui_manager_idle_dynamic_sleep(self):
        self.assertIn("Thread.Sleep(15);", GUI_MGR)
        self.assertIn("Thread.Sleep(25);", GUI_MGR)


if __name__ == "__main__":
    unittest.main()
