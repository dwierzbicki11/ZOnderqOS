#!/usr/bin/env python3
"""QMP key mapping regression checks; does not boot a guest."""
import importlib.util
from pathlib import Path
import unittest
import tempfile
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("audio_qemu", ROOT / "tools/smoke-audio-qemu.py")
audio = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audio)


class QmpKeyTests(unittest.TestCase):
    def test_audio_completion_waits_for_the_correct_command(self):
        with tempfile.TemporaryDirectory() as directory:
            serial = Path(directory) / "serial.log"
            serial.write_text("[AUDIO] PCM ACCEPTED_AND_DRAINED\\n"
                              "[AUDIO] MIXQUEUE bytes=100\\n")
            with self.assertRaisesRegex(RuntimeError, "did not finish"):
                audio.wait_for_guest_audio_completion(
                    serial, "[AUDIO] MIXQUEUE", timeout_seconds=0)
            serial.write_text("[AUDIO] MIXQUEUE bytes=100\\n"
                              "[AUDIO] PCM ACCEPTED_AND_DRAINED\\n")
            audio.wait_for_guest_audio_completion(
                serial, "[AUDIO] MIXQUEUE", timeout_seconds=0)
            with self.assertRaisesRegex(RuntimeError, "did not finish"):
                audio.wait_for_guest_audio_completion(
                    serial, "[AUDIO] OTHER", timeout_seconds=0)

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
