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
    def test_real_qmp_bottom_edge_resize_has_two_visual_gates(self):
        source = (ROOT / "tools/qmp-gui-filemanager.py").read_text()
        self.assertIn('".filemanager-resize-before.ppm"', source)
        self.assertIn('".filemanager-resize-smaller.ppm"', source)
        self.assertIn('".filemanager-resize-restored.ppm"', source)
        self.assertIn("drag_mouse_relative(channel, 0, -64)", source)
        self.assertIn("drag_mouse_relative(channel, 0, 64)", source)
        self.assertIn("if fraction < 0.015:", source)
        self.assertIn("if fraction > 0.10:", source)
        self.assertIn("PS/2 bottom-edge resize did not visibly shrink", source)
        self.assertIn("reverse PS/2 resize did not restore", source)
        # A bottom-edge shrink is sampled inside the central framebuffer
        # region, unlike a right-edge-only change beyond the 5/6 crop.
        width, height = 1024, 768
        baseline = bytes(width * height * 3)
        shrunk = bytearray(baseline)
        for y in range(534, 598):
            for x in range(170, 850):
                offset = (y * width + x) * 3
                shrunk[offset:offset + 3] = b"\\xff\\xff\\xff"
        changed, samples = PROBE.difference(
            (width, height, baseline), (width, height, bytes(shrunk)))
        self.assertGreater(changed / samples, 0.015)
        restored, _ = PROBE.difference(
            (width, height, baseline), (width, height, baseline))
        self.assertEqual(restored, 0)

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

    def test_restored_desktop_diff_is_small(self):
        width, height = 80, 64
        desktop = bytes(width * height * 3)
        # The application fills the center; closing it must restore desktop.
        app = bytes([255]) * (width * height * 3)
        changed, samples = PROBE.difference(
            (width, height, app), (width, height, desktop))
        self.assertGreater(changed / samples, 0.12)
        changed, samples = PROBE.difference(
            (width, height, desktop), (width, height, desktop))
        self.assertLessEqual(changed / samples, 0.10)

    def test_alt_f4_close_probe_is_wired(self):
        source = (ROOT / "tools/qmp-gui-filemanager.py").read_text()
        self.assertIn('send_keys(channel, "alt", "f4")', source)
        self.assertIn('".filemanager-closed.ppm"', source)
        self.assertIn('("app-closed", after, closed)', source)
        self.assertIn('if fraction > 0.10:', source)

    def test_changed_framebuffer_dimensions_are_rejected(self):
        with self.assertRaises(ValueError):
            PROBE.difference((80, 64, bytes(80 * 64 * 3)),
                             (81, 64, bytes(81 * 64 * 3)))


    def test_real_qmp_filter_escape_probe_has_restore_gate(self):
        source = (ROOT / "tools/qmp-gui-filemanager.py").read_text()
        self.assertIn('for key in "zzzzzzzz":', source)
        self.assertIn('send_keys(channel, "esc")', source)
        self.assertIn('".filemanager-filtered.ppm"', source)
        self.assertIn('".filemanager-filter-cleared.ppm"', source)
        self.assertIn('difference(mouse_after, filter_cleared)', source)
        self.assertIn('if fraction > 0.10:', source)
        self.assertLess(source.index('send_keys(channel, "esc")'),
                        source.index('send_keys(channel, "alt", "f4")'))

    def test_filter_escape_restore_diff_accepts_small_status_change(self):
        width, height = 80, 64
        original = bytes(width * height * 3)
        restored = bytearray(original)
        # A small status/clock repaint must not fail a viewport restore.
        for y in range(13, 15):
            for x in range(13, 19):
                off = (y * width + x) * 3
                restored[off:off + 3] = bytes((255, 255, 255))
        changed, samples = PROBE.difference(
            (width, height, original), (width, height, bytes(restored)))
        self.assertGreater(changed, 0)
        self.assertLessEqual(changed / samples, 0.10)


    def test_filter_requires_measurable_real_framebuffer_change(self):
        source = (ROOT / "tools/qmp-gui-filemanager.py").read_text()
        self.assertIn("if fraction < 0.003:", source)
        self.assertIn("File Manager search did not visibly update", source)
        self.assertLess(source.index("if fraction < 0.003:"),
                        source.index("difference(mouse_after, filter_cleared)"))

        width, height = 80, 64
        before = bytes(width * height * 3)
        unchanged, samples = PROBE.difference(
            (width, height, before), (width, height, before))
        self.assertLess(unchanged / samples, 0.003)

        # A real content change in the sampled viewport must clear the gate.
        changed, samples = PROBE.difference(
            (width, height, before),
            (width, height, bytes([255]) * len(before)))
        self.assertGreaterEqual(changed / samples, 0.003)


    def test_real_mouse_move_probe_and_event_shape(self):
        source = (ROOT / "tools/qmp-gui-filemanager.py").read_text()
        self.assertIn('move_mouse_relative(channel, 60, 0)', source)
        self.assertIn('".filemanager-mouse-before.ppm"', source)
        self.assertIn('".filemanager-mouse-after.ppm"', source)
        self.assertIn('if mouse_changed < 8:', source)
        self.assertIn("difference(mouse_after, filtered)", source)
        self.assertIn("difference(mouse_after, filter_cleared)", source)

        class FakeChannel:
            def __init__(self):
                self.payloads = []

            def write(self, payload):
                import json
                self.payloads.append(json.loads(payload))

            def readline(self):
                return b'{"return": {}}\n'

        channel = FakeChannel()
        original_sleep = PROBE.time.sleep
        try:
            PROBE.time.sleep = lambda seconds: None
            PROBE.move_mouse_relative(channel, 60, 0)
            PROBE.move_mouse_relative(channel, 0, 0)
        finally:
            PROBE.time.sleep = original_sleep
        self.assertEqual(len(channel.payloads), 1)
        self.assertEqual(channel.payloads[0], {
            "execute": "input-send-event",
            "arguments": {"events": [
                {"type": "rel", "data": {"axis": "x", "value": 60}}
            ]}
        })


    def test_qmp_mouse_click_sends_press_then_release(self):
        import json

        class FakeChannel:
            def __init__(self):
                self.payloads = []

            def write(self, payload):
                self.payloads.append(json.loads(payload))

            def readline(self):
                return b'{"return": {}}\\n'.replace(b"\\n", b"\n")

        channel = FakeChannel()
        original_sleep = PROBE.time.sleep
        try:
            PROBE.time.sleep = lambda _: None
            PROBE.click_mouse(channel)
        finally:
            PROBE.time.sleep = original_sleep

        self.assertEqual(channel.payloads, [
            {"execute": "input-send-event", "arguments": {"events": [
                {"type": "btn", "data": {"button": "left", "down": True}}]}},
            {"execute": "input-send-event", "arguments": {"events": [
                {"type": "btn", "data": {"button": "left", "down": False}}]}},
        ])

    def test_qmp_mouse_click_releases_on_press_error_and_rejects_invalid_button(self):
        import json

        class FakeChannel:
            def __init__(self):
                self.payloads = []

            def write(self, payload):
                self.payloads.append(json.loads(payload))

            def readline(self):
                if len(self.payloads) == 1:
                    return b'{"error": {"desc": "synthetic press failure"}}\\n'.replace(b"\\n", b"\n")
                return b'{"return": {}}\\n'.replace(b"\\n", b"\n")

        channel = FakeChannel()
        original_sleep = PROBE.time.sleep
        try:
            PROBE.time.sleep = lambda _: None
            with self.assertRaises(RuntimeError):
                PROBE.click_mouse(channel)
            with self.assertRaises(ValueError):
                PROBE.mouse_button(channel, True, "wheel-up")
            with self.assertRaises(TypeError):
                PROBE.mouse_button(channel, 1)
        finally:
            PROBE.time.sleep = original_sleep

        self.assertEqual(len(channel.payloads), 2)
        self.assertTrue(channel.payloads[0]["arguments"]["events"][0]["data"]["down"])
        self.assertFalse(channel.payloads[1]["arguments"]["events"][0]["data"]["down"])



    def test_relative_mouse_homing_reaches_notification_tile_without_overflow(self):
        import json

        class FakeChannel:
            def __init__(self):
                self.payloads = []

            def write(self, payload):
                self.payloads.append(json.loads(payload))

            def readline(self):
                return b'{"return": {}}\\n'.replace(b"\\n", b"\n")

        channel = FakeChannel()
        original_sleep = PROBE.time.sleep
        try:
            PROBE.time.sleep = lambda _: None
            PROBE.move_mouse_to_from_corner(channel, 772, 746, 1024, 768)
            with self.assertRaises(ValueError):
                PROBE.move_mouse_to_from_corner(channel, -1, 10, 1024, 768)
            with self.assertRaises(ValueError):
                PROBE.move_mouse_to_from_corner(channel, 10, 768, 1024, 768)
        finally:
            PROBE.time.sleep = original_sleep

        # A relative PS/2 packet must never request a large delta.
        for start_x, start_y in ((0, 0), (512, 384), (1023, 767)):
            x, y = start_x, start_y
            for payload in channel.payloads:
                for event in payload["arguments"]["events"]:
                    delta = event["data"]["value"]
                    self.assertLessEqual(abs(delta), 64)
                    if event["data"]["axis"] == "x":
                        x = max(0, min(1023, x + delta))
                    else:
                        y = max(0, min(767, y + delta))
            self.assertEqual((x, y), (772, 746))

    def test_real_qmp_notification_click_has_stationary_framebuffer_gate(self):
        source = (ROOT / "tools/qmp-gui-filemanager.py").read_text()
        self.assertIn('move_mouse_to_from_corner(channel, notification_x, notification_y,', source)
        self.assertIn('".notification-click-before.ppm"', source)
        self.assertIn('".notification-click-open.ppm"', source)
        self.assertIn('".notification-click-closed.ppm"', source)
        self.assertIn('difference(notification_before, notification_open)', source)
        self.assertIn('difference(notification_before, notification_closed)', source)
        self.assertIn('if fraction < 0.05:', source)
        self.assertIn('if fraction > 0.10:', source)
        self.assertLess(source.index('screenshot(channel, prefix + ".notification-click-before.ppm")'),
                        source.index('click_mouse(channel)'))
        self.assertLess(source.index('screenshot(channel, prefix + ".notification-click-closed.ppm")'),
                        source.index('send_keys(channel, "alt", "f4")'))

    def test_qmp_drag_holds_button_during_relative_motion(self):
        import json

        class FakeChannel:
            def __init__(self):
                self.payloads = []

            def write(self, payload):
                self.payloads.append(json.loads(payload))

            def readline(self):
                return b'{"return": {}}\\n'.replace(b"\\n", b"\n")

        channel = FakeChannel()
        old_sleep = PROBE.time.sleep
        try:
            PROBE.time.sleep = lambda _: None
            PROBE.drag_mouse_relative(channel, 48, 28)
        finally:
            PROBE.time.sleep = old_sleep
        events = [p["arguments"]["events"] for p in channel.payloads]
        self.assertEqual(events[0], [{"type": "btn", "data": {"button": "left", "down": True}}])
        self.assertEqual(events[1], [
            {"type": "rel", "data": {"axis": "x", "value": 48}},
            {"type": "rel", "data": {"axis": "y", "value": 28}},
        ])
        self.assertEqual(events[2], [{"type": "btn", "data": {"button": "left", "down": False}}])

    def test_real_qmp_window_drag_has_forward_and_reverse_framebuffer_gates(self):
        source = (ROOT / "tools/qmp-gui-filemanager.py").read_text()
        self.assertIn('drag_mouse_relative(channel, 48, 28)', source)
        self.assertIn('drag_mouse_relative(channel, -48, -28)', source)
        self.assertIn('".filemanager-drag-before.ppm"', source)
        self.assertIn('".filemanager-drag-moved.ppm"', source)
        self.assertIn('".filemanager-drag-restored.ppm"', source)
        self.assertIn('difference(drag_before, drag_moved)', source)
        self.assertIn('difference(drag_before, drag_restored)', source)
        self.assertIn('if fraction < 0.03:', source)
        self.assertIn('if fraction > 0.10:', source)
        self.assertLess(source.index('drag_mouse_relative(channel, 48, 28)'),
                        source.index('drag_mouse_relative(channel, -48, -28)'))
        self.assertLess(source.index('".filemanager-drag-restored.ppm"'),
                        source.index('send_keys(channel, "alt", "f4")'))



if __name__ == "__main__":
    unittest.main(verbosity=2)
