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
