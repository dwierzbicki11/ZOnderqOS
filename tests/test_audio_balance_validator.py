#!/usr/bin/env python3
"""Positive and negative synthetic fixtures for balance WAV validator (not QEMU)."""
import importlib.util
from pathlib import Path
import struct
import tempfile
import unittest
import wave

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("balance", ROOT / "tools/verify-audio-balance-capture.py")
balance = importlib.util.module_from_spec(spec)
spec.loader.exec_module(balance)


class BalanceCaptureValidatorTests(unittest.TestCase):
    def capture(self, left_hz=0, right_hz=880, duration_ms=300):
        rate = 48000
        frames = rate * duration_ms // 1000
        samples = bytearray()
        for i in range(frames):
            left = (4096 if (i * left_hz * 2 // rate) % 2 == 0 else -4096) if left_hz else 0
            right = (4096 if (i * right_hz * 2 // rate) % 2 == 0 else -4096) if right_hz else 0
            samples.extend(struct.pack("<hh", left, right))
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "capture.wav"
            with wave.open(str(path), "wb") as stream:
                stream.setnchannels(2)
                stream.setsampwidth(2)
                stream.setframerate(rate)
                stream.writeframes(samples)
            return balance.inspect(path)

    def test_right_only_880_hz_passes(self):
        self.assertIn("PASS", self.capture())

    def test_swapped_channels_rejected(self):
        with self.assertRaises(ValueError):
            self.capture(left_hz=880, right_hz=0)

    def test_both_channels_active_rejected(self):
        with self.assertRaises(ValueError):
            self.capture(left_hz=440, right_hz=880)

    def test_silence_rejected(self):
        with self.assertRaises(ValueError):
            self.capture(right_hz=0)

    def test_wrong_pitch_rejected(self):
        with self.assertRaises(ValueError):
            self.capture(right_hz=440)

    def test_short_signal_rejected(self):
        with self.assertRaises(ValueError):
            self.capture(duration_ms=100)


if __name__ == "__main__":
    unittest.main(verbosity=2)
