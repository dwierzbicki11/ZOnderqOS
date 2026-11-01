#!/usr/bin/env python3
"""Synthetic fixtures for the real-QEMU WAV onset-delay validator."""
import importlib.util
from pathlib import Path
import struct
import tempfile
import unittest
import wave

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location(
    "audio_delay_capture", ROOT / "tools/verify-audio-delay-capture.py")
validator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validator)


def fixture(path, rate=48000, right_delay_ms=50, left_delay_ms=0,
            left_enabled=True, right_enabled=True, channels=2):
    frames = rate // 2
    samples = bytearray(frames * channels * 2)
    left_begin = rate // 10 + left_delay_ms * rate // 1000
    right_begin = rate // 10 + right_delay_ms * rate // 1000
    duration = rate // 4
    for i in range(frames):
        left = 4096 if left_enabled and left_begin <= i < left_begin + duration else 0
        right = -4096 if right_enabled and right_begin <= i < right_begin + duration else 0
        if channels == 2:
            struct.pack_into("<hh", samples, i * 4, left, right)
        else:
            struct.pack_into("<h", samples, i * 2, left)
    with wave.open(str(path), "wb") as wav:
        wav.setnchannels(channels)
        wav.setsampwidth(2)
        wav.setframerate(rate)
        wav.writeframes(samples)


class DelayValidatorTests(unittest.TestCase):
    def test_accepts_both_supported_capture_rates(self):
        with tempfile.TemporaryDirectory() as tmp:
            for rate in (44100, 48000):
                path = Path(tmp) / f"valid-{rate}.wav"
                fixture(path, rate=rate)
                self.assertIn("[AUDIO-DELAY] PASS", validator.inspect(path))

    def test_rejects_wrong_timing_and_channel_order(self):
        with tempfile.TemporaryDirectory() as tmp:
            for right_ms, left_ms in ((0, 0), (100, 0), (0, 50)):
                path = Path(tmp) / f"invalid-{right_ms}-{left_ms}.wav"
                fixture(path, right_delay_ms=right_ms, left_delay_ms=left_ms)
                with self.assertRaisesRegex(ValueError, "onset offset"):
                    validator.inspect(path)

    def test_rejects_missing_channel(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "silent-right.wav"
            fixture(path, right_enabled=False)
            with self.assertRaisesRegex(ValueError, "no sustained"):
                validator.inspect(path)

    def test_rejects_mono_and_corrupt_wav(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "mono.wav"
            fixture(path, channels=1)
            with self.assertRaisesRegex(ValueError, "stereo PCM16"):
                validator.inspect(path)
            path.write_bytes(b"not a wave file")
            with self.assertRaises((ValueError, EOFError, wave.Error)):
                validator.inspect(path)


if __name__ == "__main__":
    unittest.main()
