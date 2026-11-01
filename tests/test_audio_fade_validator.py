#!/usr/bin/env python3
"""Synthetic positive/negative WAV fixtures; separate from real QEMU evidence."""
import importlib.util
from pathlib import Path
import struct
import tempfile
import unittest
import wave

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("audio_fade", ROOT / "tools/verify-audio-fade-capture.py")
audio_fade = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audio_fade)


class FadeCaptureValidatorTests(unittest.TestCase):
    def capture(self, fade_ms=20, fade_left=True, fade_right=True,
                duration_ms=250, gap=False, rate=48000):
        frames = rate * duration_ms // 1000
        samples = bytearray()
        for frame in range(frames):
            tone = 4096 if (frame * 440 * 2 // rate) % 2 == 0 else -4096
            fade_frames = rate * fade_ms // 1000
            envelope = min(fade_frames, frame, frames - 1 - frame)
            left = tone * envelope // fade_frames if fade_left and fade_frames else tone
            right = tone * envelope // fade_frames if fade_right and fade_frames else tone
            if gap and rate * 12 // 100 <= frame < rate * 14 // 100:
                left = right = 0
            samples.extend(struct.pack("<hh", left, right))
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "capture.wav"
            with wave.open(str(path), "wb") as stream:
                stream.setnchannels(2)
                stream.setsampwidth(2)
                stream.setframerate(rate)
                stream.writeframes(samples)
            return audio_fade.inspect(path)

    def test_20ms_stereo_fade_passes(self):
        self.assertIn("PASS", self.capture())

    def test_44100_backend_rate_passes(self):
        self.assertIn("PASS", self.capture(rate=44100))

    def test_short_backend_dropout_does_not_mask_real_fade(self):
        self.assertIn("PASS", self.capture(gap=True))

    def test_no_fade_rejected(self):
        with self.assertRaisesRegex(ValueError, "missing fade-in"):
            self.capture(fade_ms=0)

    def test_left_channel_missing_fade_rejected(self):
        with self.assertRaisesRegex(ValueError, "missing fade-in"):
            self.capture(fade_left=False)

    def test_right_channel_missing_fade_rejected(self):
        with self.assertRaisesRegex(ValueError, "missing fade-in"):
            self.capture(fade_right=False)

    def test_short_capture_rejected(self):
        with self.assertRaises(ValueError):
            self.capture(duration_ms=100)


if __name__ == "__main__":
    unittest.main(verbosity=2)
