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
    def capture(self, left_hz=0, right_hz=880, duration_ms=300, active_channel="right"):
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
            return balance.inspect(path, active_channel=active_channel)

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

    def test_left_only_440_hz_passes(self):
        self.assertIn("left-only", self.capture(left_hz=440, right_hz=0, active_channel="left"))

    def test_left_only_swapped_channels_rejected(self):
        with self.assertRaises(ValueError):
            self.capture(left_hz=0, right_hz=440, active_channel="left")

    def test_left_only_both_channels_active_rejected(self):
        with self.assertRaises(ValueError):
            self.capture(left_hz=440, right_hz=880, active_channel="left")

    def test_left_only_silence_rejected(self):
        with self.assertRaises(ValueError):
            self.capture(left_hz=0, right_hz=0, active_channel="left")

    def test_left_only_wrong_pitch_rejected(self):
        with self.assertRaises(ValueError):
            self.capture(left_hz=880, right_hz=0, active_channel="left")

    def test_left_only_short_signal_rejected(self):
        with self.assertRaises(ValueError):
            self.capture(left_hz=440, right_hz=0, duration_ms=100, active_channel="left")

    def test_invalid_active_channel_rejected(self):
        with self.assertRaises(ValueError):
            self.capture(active_channel="center")


    def capture_partial(self, balance_percent=50, left_hz=440, right_hz=880,
                        left_amplitude=2048, right_amplitude=4096, duration_ms=300):
        rate = 48000
        frames = rate * duration_ms // 1000
        samples = bytearray()
        for i in range(frames):
            left = (left_amplitude if (i * left_hz * 2 // rate) % 2 == 0
                    else -left_amplitude) if left_hz else 0
            right = (right_amplitude if (i * right_hz * 2 // rate) % 2 == 0
                     else -right_amplitude) if right_hz else 0
            samples.extend(struct.pack("<hh", left, right))
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "partial.wav"
            with wave.open(str(path), "wb") as stream:
                stream.setnchannels(2)
                stream.setsampwidth(2)
                stream.setframerate(rate)
                stream.writeframes(samples)
            return balance.inspect_partial(path, balance_percent)

    def test_partial_right_bias_passes(self):
        self.assertIn("balance=+50", self.capture_partial())

    def test_partial_left_bias_passes(self):
        self.assertIn("balance=-50", self.capture_partial(
            balance_percent=-50, left_amplitude=4096, right_amplitude=2048))

    def test_partial_equal_channels_rejected(self):
        with self.assertRaises(ValueError):
            self.capture_partial(left_amplitude=4096, right_amplitude=4096)

    def test_partial_wrong_channel_attenuated_rejected(self):
        with self.assertRaises(ValueError):
            self.capture_partial(left_amplitude=4096, right_amplitude=2048)

    def test_partial_wrong_pitch_rejected(self):
        with self.assertRaises(ValueError):
            self.capture_partial(left_hz=880, right_hz=440)

    def test_partial_silent_channel_rejected(self):
        with self.assertRaises(ValueError):
            self.capture_partial(left_hz=0)

    def test_partial_short_signal_rejected(self):
        with self.assertRaises(ValueError):
            self.capture_partial(duration_ms=100)

    def test_partial_zero_balance_rejected(self):
        with self.assertRaises(ValueError):
            self.capture_partial(balance_percent=0)

    def test_partial_extreme_balance_rejected(self):
        with self.assertRaises(ValueError):
            self.capture_partial(balance_percent=100)

    def test_partial_tiny_channel_rejected(self):
        with self.assertRaises(ValueError):
            self.capture_partial(balance_percent=90)



if __name__ == "__main__":
    unittest.main(verbosity=2)
