#!/usr/bin/env python3
"""Pure host-side QMP framebuffer parser/diff tests; no guest is booted."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location(
    "qmp_gui_filemanager", ROOT / "tools/qmp-gui-filemanager.py")
PROBE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PROBE)


class FramebufferProbeTests(unittest.TestCase):
    def test_identical_frames_do_not_count_as_gui_interaction(self):
        width, height = 80, 64
        pixels = bytes(width * height * 3)
        changed, samples = PROBE.difference(
            (width, height, pixels), (width, height, pixels))
        self.assertGreater(samples, 0)
        self.assertEqual(changed, 0)

    def test_center_region_change_is_detected(self):
        width, height = 80, 64
        before = bytes(width * height * 3)
        after = bytes([255]) * (width * height * 3)
        changed, samples = PROBE.difference(
            (width, height, before), (width, height, after))
        self.assertEqual(changed, samples)
        self.assertGreater(changed / samples, 0.12)

    def test_only_bottom_taskbar_change_does_not_pass(self):
        width, height = 80, 64
        before = bytes(width * height * 3)
        after = bytearray(before)
        for y in range(height - 6, height):
            for x in range(width):
                offset = (y * width + x) * 3
                after[offset:offset + 3] = b"\xff\xff\xff"
        changed, _ = PROBE.difference(
            (width, height, before), (width, height, bytes(after)))
        self.assertEqual(changed, 0)

    def test_read_ppm_validates_exact_pixel_count(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "capture.ppm"
            path.write_bytes(b"P6\n# QMP framebuffer\n4 3\n255\n" + bytes(36))
            self.assertEqual(PROBE.read_ppm(str(path)), (4, 3, bytes(36)))
            path.write_bytes(b"P6\n4 3\n255\n" + bytes(33))
            with self.assertRaises(ValueError):
                PROBE.read_ppm(str(path))

    def test_changed_framebuffer_dimensions_are_rejected(self):
        with self.assertRaises(ValueError):
            PROBE.difference((80, 64, bytes(80 * 64 * 3)),
                             (81, 64, bytes(81 * 64 * 3)))


if __name__ == "__main__":
    unittest.main(verbosity=2)
