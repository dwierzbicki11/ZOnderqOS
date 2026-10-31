#!/usr/bin/env python3
"""Host-side geometry and fail-closed checks; does NOT boot ZonderqOS."""
import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location(
    "qmp_gui_taskmanager", ROOT / "tools/qmp-gui-taskmanager.py")
PROBE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PROBE)


class TaskManagerQmpContract(unittest.TestCase):
    def test_expected_window_bounds_match_window_constrain_to_desktop(self):
        self.assertEqual(PROBE.expected_taskmanager_bounds(800, 600),
                         (0, 0, 800, 556))
        self.assertEqual(PROBE.expected_taskmanager_bounds(1024, 768),
                         (12, 22, 1000, 690))
        self.assertEqual(PROBE.expected_taskmanager_bounds(1366, 768),
                         (165, 22, 1040, 690))
        self.assertEqual(PROBE.expected_taskmanager_bounds(1920, 1080),
                         (165, 110, 1040, 690))
        with self.assertRaises(ValueError):
            PROBE.expected_taskmanager_bounds(800, 44)

    def test_navigation_targets_are_within_client_area(self):
        for width, height in ((320, 240), (800, 600), (1024, 768),
                              (1366, 768), (1920, 1080)):
            bounds = PROBE.expected_taskmanager_bounds(width, height)
            for page in (0, 1):
                x, y = PROBE.nav_target(bounds, page)
                self.assertGreaterEqual(x, bounds[0] + 10)
                self.assertLess(x, bounds[0] + bounds[2] - 10)
                self.assertGreaterEqual(y, bounds[1] + 40)
                self.assertLess(y, bounds[1] + bounds[3] - 10)
        with self.assertRaises(ValueError):
            PROBE.nav_target((0, 0, 800, 556), 2)

    def test_source_uses_read_only_task_manager_actions(self):
        source = (ROOT / "tools/qmp-gui-taskmanager.py").read_text()
        self.assertIn('for key in "task":', source)
        self.assertIn('qmp.send_keys(channel, "ret")', source)
        self.assertIn('qmp.click_mouse(channel)', source)
        self.assertIn('qmp.send_keys(channel, "esc")', source)
        self.assertNotIn('"del"', source)
        self.assertNotIn('EndSelectedTask(', source)
        self.assertIn('qmp.verify_expected_framebuffer(frame_width, frame_height)', source)
        self.assertIn('minimum=0.025', source)
        self.assertIn('maximum=0.10', source)

    def test_visual_gate_rejects_unchanged_frames(self):
        pixels = bytes(80 * 64 * 3)
        frame = (80, 64, pixels)
        with self.assertRaisesRegex(RuntimeError, "insufficient"):
            PROBE.measure("app-open", frame, frame, minimum=0.12)

    def test_visual_gate_accepts_large_change_and_rejects_bad_restore(self):
        before = (80, 64, bytes(80 * 64 * 3))
        after = (80, 64, bytes([255]) * (80 * 64 * 3))
        self.assertEqual(PROBE.measure("app-open", before, after, minimum=0.12), 1)
        with self.assertRaisesRegex(RuntimeError, "did not return"):
            PROBE.measure("desktop-restored", before, after, maximum=0.10)

    def test_real_guest_identity_is_checked_by_persistent_log(self):
        smoke = (ROOT / "tools/smoke-services-qemu.sh").read_text()
        self.assertIn("SERVICE_QEMU_PROBE_TASK_MANAGER", smoke)
        self.assertIn("[PALETTE] Launched application: Manager zadan", smoke)
        self.assertIn("second_taskmanager_launches > first_taskmanager_launches", smoke)



    def test_bottom_edge_resize_steps_are_bounded_and_reversible(self):
        for delta in (-470, -336, -64, -1, 0, 1, 64, 336, 470):
            steps = list(PROBE.resize_steps(delta))
            self.assertEqual(sum(steps), delta)
            self.assertTrue(all(0 < abs(step) <= 48 for step in steps))
            self.assertEqual(sum(PROBE.resize_steps(-delta)), -delta)
        with self.assertRaises(TypeError):
            list(PROBE.resize_steps(1.5))
        with self.assertRaises(TypeError):
            list(PROBE.resize_steps(True))

    def test_taskmanager_probe_keeps_resize_evidence_separate_from_layout_proof(self):
        source = (ROOT / "tools/qmp-gui-taskmanager.py").read_text()
        self.assertIn("shrink = min(470, max(0, bounds[3] - 220))", source)
        self.assertIn("resize_bottom_edge(channel, -shrink)", source)
        self.assertIn("resize_bottom_edge(channel, shrink)", source)
        self.assertIn('minimum=0.015', source)
        self.assertIn('maximum=0.10', source)
        self.assertIn("compact layout semantics require visual review", source)
        self.assertIn("finally:\n        qmp.mouse_button(channel, False)", source)
        for height in (220, 300, 556, 690):
            shrink = min(470, max(0, height - 220))
            self.assertGreaterEqual(height - shrink, 220)
            self.assertLessEqual(height - shrink, height)

if __name__ == "__main__":
    unittest.main(verbosity=2)
