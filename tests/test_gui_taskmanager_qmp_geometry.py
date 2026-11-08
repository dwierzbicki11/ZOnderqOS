#!/usr/bin/env python3
"""Pure geometry and input-boundary regressions for the QMP Task Manager probe.

No QEMU, guest disk, host partition or display server is required.
"""
import importlib.util
from pathlib import Path
import unittest

MODULE = Path(__file__).resolve().parents[1] / "tools" / "qmp-gui-taskmanager.py"
spec = importlib.util.spec_from_file_location("qmp_gui_taskmanager_geometry", MODULE)
task = importlib.util.module_from_spec(spec)
spec.loader.exec_module(task)


class TaskManagerGeometryTests(unittest.TestCase):
    def test_window_stays_inside_work_area(self):
        for width, height in ((640, 480), (800, 600), (1024, 768),
                              (1366, 768), (1920, 1080), (2560, 1440)):
            with self.subTest(resolution=(width, height)):
                x, y, w, h = task.expected_taskmanager_bounds(width, height)
                self.assertGreater(w, 0)
                self.assertGreater(h, 0)
                self.assertGreaterEqual(x, 0)
                self.assertGreaterEqual(y, 0)
                self.assertLessEqual(x + w, width)
                self.assertLessEqual(y + h, height - 44)
                for page in (0, 1):
                    tx, ty = task.nav_target((x, y, w, h), page)
                    self.assertLessEqual(x, tx)
                    self.assertLess(tx, x + w)
                    self.assertLessEqual(y, ty)
                    self.assertLess(ty, y + h)

    def test_compact_navigation_targets_are_distinct(self):
        bounds = (0, 0, 800, 220)
        self.assertNotEqual(task.nav_target(bounds, 0),
                            task.nav_target(bounds, 1))

    def test_resize_steps_preserve_delta_and_limit_each_packet(self):
        for delta in (-1000, -470, -49, -48, -1, 0, 1, 48, 49, 470, 1000):
            with self.subTest(delta=delta):
                steps = list(task.resize_steps(delta))
                self.assertEqual(sum(steps), delta)
                self.assertTrue(all(0 < abs(step) <= 48 for step in steps))
                self.assertTrue(all((step > 0) == (delta > 0) for step in steps))

    def test_resize_rejects_non_integer_deltas(self):
        for value in (True, False, 1.5, "48", None):
            with self.subTest(value=value):
                with self.assertRaises(TypeError):
                    list(task.resize_steps(value))

    def test_invalid_framebuffer_is_rejected(self):
        for width, height in ((0, 768), (-1, 600), (800, 44), (800, 0)):
            with self.subTest(resolution=(width, height)):
                with self.assertRaises(ValueError):
                    task.expected_taskmanager_bounds(width, height)

    def test_compact_region_detects_only_changes_inside_window(self):
        width, height = 80, 64
        before = bytearray(width * height * 3)
        outside = bytearray(before)
        inside = bytearray(before)
        bounds = (16, 12, 32, 24)
        # Sampled pixels outside the compact window must not count.
        for x, y in ((0, 0), (60, 48), (12, 12)):
            outside[(y * width + x) * 3] = 255
        changed, samples = task.difference_in_region(
            (width, height, bytes(before)),
            (width, height, bytes(outside)), bounds)
        self.assertEqual(changed, 0)
        self.assertEqual(samples, 48)
        inside[(12 * width + 16) * 3] = 255
        inside[(16 * width + 20) * 3 + 1] = 255
        changed, samples = task.difference_in_region(
            (width, height, bytes(before)),
            (width, height, bytes(inside)), bounds)
        self.assertEqual((changed, samples), (2, 48))

    def test_compact_region_rejects_bad_framebuffers_and_bounds(self):
        frame = (32, 32, bytes(32 * 32 * 3))
        invalid = [
            (frame, (33, 32, bytes(33 * 32 * 3)), (0, 0, 8, 8)),
            (frame, (32, 32, b"short"), (0, 0, 8, 8)),
            (frame, frame, (0, 0, 0, 8)),
            (frame, frame, (32, 0, 8, 8)),
            (frame, frame, (-20, 0, 8, 8)),
        ]
        for first, second, bounds in invalid:
            with self.subTest(bounds=bounds):
                with self.assertRaises(ValueError):
                    task.difference_in_region(first, second, bounds)

    def test_compact_region_clips_partially_visible_window(self):
        frame = (32, 32, bytes(32 * 32 * 3))
        changed, samples = task.difference_in_region(
            frame, frame, (28, 28, 20, 20))
        self.assertEqual((changed, samples), (0, 1))


if __name__ == "__main__":
    unittest.main()
