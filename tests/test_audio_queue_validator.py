#!/usr/bin/env python3
"""Synthetic positive/negative fixtures for the real HDA mixqueue validator."""
import importlib.util
from pathlib import Path
import struct
import tempfile
import unittest
import wave

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location(
    "audio_queue_capture", ROOT / "tools/verify-audio-queue-capture.py")
validator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validator)


def fixture(path, rate=48000, right_delay=50, last_hz=660,
            last_enabled=True, gap=True, right_overrun=False, channels=2):
    frames = rate * 8 // 10
    origin = rate // 10
    first_end = origin + (250 if gap else 300) * rate // 1000
    right_start = origin + right_delay * rate // 1000
    right_end = origin + (400 if right_overrun else right_delay + 250) * rate // 1000
    last_start = origin + 300 * rate // 1000
    last_end = origin + 400 * rate // 1000
    data = bytearray(frames * channels * 2)

    def square(frame, start, frequency):
        return 4096 if (((frame - start) * 2 * frequency // rate) & 1) == 0 else -4096

    for frame in range(frames):
        left = 0
        right = 0
        if origin <= frame < first_end:
            left = square(frame, origin, 440)
        if last_enabled and last_start <= frame < last_end:
            left = square(frame, last_start, last_hz)
        if right_start <= frame < right_end:
            right = square(frame, right_start, 880)
        if channels == 2:
            struct.pack_into("<hh", data, frame * 4, left, right)
        else:
            struct.pack_into("<h", data, frame * 2, left)
    with wave.open(str(path), "wb") as output:
        output.setnchannels(channels)
        output.setsampwidth(2)
        output.setframerate(rate)
        output.writeframes(data)


class AudioQueueValidatorTests(unittest.TestCase):
    def test_valid_three_clip_timeline_at_both_backend_rates(self):
        with tempfile.TemporaryDirectory() as tmp:
            for rate in (44100, 48000):
                path = Path(tmp) / f"good-{rate}.wav"
                fixture(path, rate=rate)
                self.assertIn("[AUDIO-QUEUE] PASS", validator.inspect(path))

    def test_rejects_wrong_onset(self):
        with tempfile.TemporaryDirectory() as tmp:
            for delay in (0, 90):
                path = Path(tmp) / f"delay-{delay}.wav"
                fixture(path, right_delay=delay)
                with self.assertRaisesRegex(ValueError, "right-channel onset"):
                    validator.inspect(path)

    def test_rejects_missing_or_wrong_final_effect(self):
        with tempfile.TemporaryDirectory() as tmp:
            for enabled, pitch in ((False, 660), (True, 440)):
                path = Path(tmp) / f"final-{enabled}-{pitch}.wav"
                fixture(path, last_enabled=enabled, last_hz=pitch)
                with self.assertRaisesRegex(ValueError, "last left clip"):
                    validator.inspect(path)

    def test_rejects_missing_gap_and_late_right_audio(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "no-gap.wav"
            fixture(path, gap=False)
            with self.assertRaisesRegex(ValueError, "left inter-clip gap"):
                validator.inspect(path)
            path = Path(tmp) / "right-overrun.wav"
            fixture(path, right_overrun=True)
            with self.assertRaisesRegex(ValueError, "right after EOF"):
                validator.inspect(path)

    def test_rejects_mono_and_corrupt_capture(self):
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
