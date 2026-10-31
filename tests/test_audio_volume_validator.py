#!/usr/bin/env python3
"""Synthetic checks for volume comparator; NOT evidence of QEMU playback."""
import importlib.util
from pathlib import Path
import struct
import tempfile
import unittest
import wave

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("audio_volume", ROOT / "tools/verify-audio-volume-capture.py")
volume = importlib.util.module_from_spec(spec)
spec.loader.exec_module(volume)


class VolumeCaptureTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.normal = Path(self.tmp.name) / "normal.wav"
        self.quiet = Path(self.tmp.name) / "quiet.wav"

    def tearDown(self):
        self.tmp.cleanup()

    def make(self, path, amplitude):
        with wave.open(str(path), "wb") as output:
            output.setnchannels(2)
            output.setsampwidth(2)
            output.setframerate(44100)
            output.writeframes(struct.pack("<hh", amplitude, -amplitude) * 10000)

    def test_accept_quarter_gain(self):
        self.make(self.normal, 3000)
        self.make(self.quiet, 750)
        self.assertIn("PASS QEMU PCM gain", volume.inspect(self.normal, self.quiet))

    def test_reject_unscaled(self):
        self.make(self.normal, 3000)
        self.make(self.quiet, 3000)
        with self.assertRaisesRegex(ValueError, "unexpected software gain"):
            volume.inspect(self.normal, self.quiet)

    def test_reject_half_gain(self):
        self.make(self.normal, 3000)
        self.make(self.quiet, 1500)
        with self.assertRaisesRegex(ValueError, "unexpected software gain"):
            volume.inspect(self.normal, self.quiet)

    def test_reject_silence(self):
        self.make(self.normal, 3000)
        self.make(self.quiet, 0)
        with self.assertRaisesRegex(ValueError, "insufficient signal"):
            volume.inspect(self.normal, self.quiet)


if __name__ == "__main__":
    unittest.main(verbosity=2)
