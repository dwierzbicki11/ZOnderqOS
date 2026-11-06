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


if __name__ == "__main__":
    unittest.main()
