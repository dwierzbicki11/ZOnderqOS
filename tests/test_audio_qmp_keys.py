#!/usr/bin/env python3
"""QMP key mapping regression checks; does not boot a guest."""
import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("audio_qemu", ROOT / "tools/smoke-audio-qemu.py")
audio = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audio)


class QmpKeyTests(unittest.TestCase):
    def test_mixqueue_has_extra_tcg_capture_settle_time(self):
        for command in ("mixqueue", "mixsequence", "mixqueueasync", "mixsequenceasync",
                        "mixqueueboot", "mixsequenceboot", "mixqueue1ms", "mixsequence1ms"):
            self.assertEqual(audio.playback_settle_seconds(command), 8)
        for command in ("tone", "wavtone", "mixdelay", "wavext20"):
            self.assertEqual(audio.playback_settle_seconds(command), 4)

    def test_optional_icount_never_changes_default_qemu_clock(self):
        self.assertEqual(audio.qemu_clock_options(False), [])
        self.assertEqual(audio.qemu_clock_options(True),
                         ["-icount", "shift=auto,sleep=on"])

    def test_output_only_hda_is_explicit_opt_in(self):
        self.assertEqual(audio.qemu_hda_codec(False), "hda-duplex")
        self.assertEqual(audio.qemu_hda_codec(True), "hda-output")

    def test_volume_command_uses_qcode_spc_and_ret(self):
        qmp = audio.Qmp.__new__(audio.Qmp)
        events = []

        def fake_execute(name, args=None):
            self.assertEqual(name, "input-send-event")
            events.extend(args["events"])

        qmp.execute = fake_execute
        with patch.object(audio.time, "sleep", return_value=None):
            qmp.type_line("volume 25")

        down = [event["data"]["key"]["data"] for event in events if event["data"]["down"]]
        up = [event["data"]["key"]["data"] for event in events if not event["data"]["down"]]
        self.assertEqual(down, list("volume") + ["spc", "2", "5", "ret"])
        self.assertEqual(up, down)
        self.assertEqual(len(events), 20)


if __name__ == "__main__":
    unittest.main(verbosity=2)
