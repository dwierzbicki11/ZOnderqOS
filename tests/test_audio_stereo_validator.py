#!/usr/bin/env python3
"""Synthetic negative/positive stereo-validator gates; NOT QEMU playback."""
import importlib.util
from pathlib import Path
import struct
import tempfile
import unittest
import wave

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("stereo_capture", ROOT / "tools/verify-audio-stereo-capture.py")
capture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(capture)


class StereoCaptureTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.wav = Path(self.temp.name) / "stereo.wav"

    def tearDown(self):
        self.temp.cleanup()

    def fixture(self, left_hz=440, right_hz=880, right_amp=4096, duration_ms=250):
        rate = 48000
        pcm = bytearray()
        for i in range(rate * (500 + duration_ms) // 1000):
            if i < rate // 2:
                l = r = 0
            else:
                at = i - rate // 2
                l = 4096 if ((at * left_hz * 2 // rate) & 1) == 0 else -4096
                r = right_amp if ((at * right_hz * 2 // rate) & 1) == 0 else -right_amp
            pcm.extend(struct.pack("<hh", l, r))
        with wave.open(str(self.wav), "wb") as output:
            output.setnchannels(2)
            output.setsampwidth(2)
            output.setframerate(rate)
            output.writeframes(pcm)

    def test_accepts_distinct_stereo_pitches(self):
        self.fixture()
        self.assertIn("PASS captured independent PCM", capture.inspect(self.wav))

    def test_rejects_swapped_channels(self):
        self.fixture(left_hz=880, right_hz=440)
        with self.assertRaisesRegex(ValueError, "left channel"):
            capture.inspect(self.wav)

    def test_rejects_identical_channels(self):
        self.fixture(right_hz=440)
        with self.assertRaisesRegex(ValueError, "right channel"):
            capture.inspect(self.wav)

    def test_rejects_silent_right(self):
        self.fixture(right_amp=0)
        with self.assertRaisesRegex(ValueError, "silent stereo channel"):
            capture.inspect(self.wav)

    def test_rejects_short_tone(self):
        self.fixture(duration_ms=100)
        with self.assertRaisesRegex(ValueError, "no sustained 200 ms"):
            capture.inspect(self.wav)


if __name__ == "__main__":
    unittest.main(verbosity=2)
