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

    def test_real_qmp_keyboard_page_probe_requires_both_transitions(self):
        source = (ROOT / "tools/qmp-gui-taskmanager.py").read_text()
        self.assertIn('qmp.send_keys(channel, "right")', source)
        self.assertIn('qmp.send_keys(channel, "left")', source)
        self.assertIn('prefix + ".taskmanager-performance-keyboard.ppm"', source)
        self.assertIn('prefix + ".taskmanager-processes-keyboard.ppm"', source)
        self.assertIn('measure("keyboard-right-performance", restored, keyboard_performance, minimum=0.025)', source)
        self.assertIn('measure("keyboard-left-transition", keyboard_performance, keyboard_restored, minimum=0.025)', source)
        self.assertIn('measure("keyboard-processes-restored", restored, keyboard_restored, maximum=0.15)', source)
        self.assertLess(source.index('qmp.send_keys(channel, "right")'),
                        source.index('qmp.send_keys(channel, "left")'))
        self.assertNotIn('qmp.send_keys(channel, "del")', source)

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

    def test_compact_region_gate_is_resolution_independent_and_fail_closed(self):
        # Synthetic host frames exercise the *actual* pixel comparison, not
        # guest input. A compact 1040x220 window is identical on HD and FHD.
        for screen_width, screen_height in ((1366, 768), (1920, 1080)):
            bounds = PROBE.expected_taskmanager_bounds(screen_width, screen_height)
            compact = (bounds[0], bounds[1], bounds[2], 220)
            size = screen_width * screen_height * 3
            before = bytearray(size)
            after = bytearray(size)
            # Simulate a small real page change (96x96 pixels), much less
            # than 2.5% of the whole FHD desktop but >2.5% of the window.
            for y in range(compact[1] + 70, compact[1] + 166):
                for x in range(compact[0] + 100, compact[0] + 196):
                    after[(y * screen_width + x) * 3] = 255
            baseline = (screen_width, screen_height, bytes(before))
            changed = (screen_width, screen_height, bytes(after))
            self.assertGreaterEqual(
                PROBE.measure("compact", baseline, changed,
                              minimum=0.025, region=compact), 0.025)
            with self.assertRaisesRegex(RuntimeError, "insufficient"):
                PROBE.measure("compact-unchanged", baseline, baseline,
                              minimum=0.025, region=compact)
            # An unrelated toast outside the compact window cannot pass.
            after[:] = before
            after[(screen_width * (screen_height - 50) + 30) * 3] = 255
            with self.assertRaisesRegex(RuntimeError, "insufficient"):
                PROBE.measure("outside", baseline,
                              (screen_width, screen_height, bytes(after)),
                              minimum=0.025, region=compact)

    def test_compact_region_rejects_bad_frames_and_bounds(self):
        frame = (32, 32, bytes(32 * 32 * 3))
        with self.assertRaises(ValueError):
            PROBE.difference_in_region(frame, (33, 32, bytes(33 * 32 * 3)),
                                       (0, 0, 20, 20))
        with self.assertRaises(ValueError):
            PROBE.difference_in_region(frame, (32, 32, b"truncated"),
                                       (0, 0, 20, 20))
        for bounds in ((0, 0, 0, 10), (0, 0, 10, -1), (40, 40, 10, 10)):
            with self.assertRaises(ValueError):
                PROBE.difference_in_region(frame, frame, bounds)

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

    def test_short_resize_probes_real_compact_tabs_and_reacquires_border(self):
        source = (ROOT / "tools/qmp-gui-taskmanager.py").read_text()
        self.assertIn("compact_bounds = (bounds[0], bounds[1], bounds[2], bounds[3] - shrink)", source)
        self.assertIn('prefix + ".taskmanager-short-performance.ppm"', source)
        self.assertIn('prefix + ".taskmanager-short-processes.ppm"', source)
        self.assertIn('measure("compact-performance-tab", short, short_performance,', source)
        self.assertIn('measure("compact-processes-tab", short_performance, short_processes,', source)
        self.assertEqual(source.count('minimum=0.025, region=compact_bounds'), 2)
        self.assertIn('measure("compact-processes-restored", short, short_processes, maximum=0.15)', source)
        self.assertIn("resize_x, resize_y - shrink,", source)
        self.assertLess(source.index('prefix + ".taskmanager-short-performance.ppm"'),
                        source.index('prefix + ".taskmanager-short-processes.ppm"'))
        self.assertLess(source.index('prefix + ".taskmanager-short-processes.ppm"'),
                        source.index("resize_x, resize_y - shrink,"))
        for width, height in ((640, 480), (800, 600), (1366, 768), (1920, 1080)):
            bounds = PROBE.expected_taskmanager_bounds(width, height)
            shrink = min(470, max(0, bounds[3] - 220))
            compact = (bounds[0], bounds[1], bounds[2], bounds[3] - shrink)
            self.assertEqual(compact[3], 220)
            self.assertLess(compact[3] - 50, 300)
            for page in (0, 1):
                x, y = PROBE.nav_target(compact, page)
                self.assertGreaterEqual(x, compact[0] + 10)
                self.assertLess(x, compact[0] + 10 + 170)
                self.assertLess(y, compact[1] + compact[3] - 10)

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
