#!/usr/bin/env python3
"""Synthetic mute-capture fixtures, not proof of actual QEMU audio."""
import importlib.util
from pathlib import Path
import struct
import tempfile
import unittest
import wave

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("audio_mute", ROOT / "tools/verify-audio-mute-capture.py")
mute = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mute)


class MuteCaptureTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.path = Path(self.temp.name) / "capture.wav"

    def tearDown(self):
        self.temp.cleanup()

    def make(self, frames=12000, amplitude=0, width=2):
        with wave.open(str(self.path), "wb") as stream:
            stream.setnchannels(2)
            stream.setsampwidth(width)
            stream.setframerate(44100)
            if width == 2:
                stream.writeframes(struct.pack("<hh", amplitude, -amplitude) * frames)
            else:
                stream.writeframes(bytes([128, 128]) * frames)

    def test_accepts_sustained_silence(self):
        self.make()
        self.assertIn("PASS QEMU HDA muted PCM", mute.inspect(self.path))

    def test_accepts_small_dither(self):
        self.make(amplitude=16)
        self.assertIn("PASS QEMU HDA muted PCM", mute.inspect(self.path))

    def test_rejects_leaked_tone(self):
        self.make(amplitude=100)
        with self.assertRaisesRegex(ValueError, "mute leaked PCM"):
            mute.inspect(self.path)

    def test_rejects_header_only(self):
        self.make(frames=0)
        with self.assertRaisesRegex(ValueError, "duration invalid"):
            mute.inspect(self.path)

    def test_rejects_too_short(self):
        self.make(frames=2000)
        with self.assertRaisesRegex(ValueError, "duration invalid"):
            mute.inspect(self.path)

    def test_rejects_wrong_format(self):
        self.make(width=1)
        with self.assertRaisesRegex(ValueError, "unsupported mute capture format"):
            mute.inspect(self.path)

    def test_rejects_missing_file(self):
        with self.assertRaises(FileNotFoundError):
            mute.inspect(self.path)


if __name__ == "__main__":
    unittest.main(verbosity=2)
