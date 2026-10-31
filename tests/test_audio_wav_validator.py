#!/usr/bin/env python3
"""Synthetic fixtures for the WAV parser. NOT a QEMU audio pass."""
import importlib.util
from pathlib import Path
import struct
import tempfile
import unittest
import wave

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("audio_capture", ROOT / "tools/verify-audio-wav-capture.py")
audio = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audio)


class WavValidatorTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.path = Path(self.temp.name) / "capture.wav"

    def tearDown(self):
        self.temp.cleanup()

    def make_wave(self, frequency=440, amplitude=4096, ms=250, silence_ms=500, width=2):
        rate = 48000
        data = bytearray()
        for i in range(rate * (silence_ms + ms) // 1000):
            playing = i >= rate * silence_ms // 1000
            sample = (amplitude if ((i * frequency // rate) & 1) == 0 else -amplitude) if playing else 0
            if width == 2:
                data.extend(struct.pack("<hh", sample, sample))
            else:
                data.extend(bytes([128, 128]))
        with wave.open(str(self.path), "wb") as wav:
            wav.setnchannels(2)
            wav.setsampwidth(width)
            wav.setframerate(rate)
            wav.writeframes(data)

    def test_accepts_real_samples_from_synthetic_tone(self):
        self.make_wave()
        report = audio.inspect(self.path)
        self.assertIn("PASS captured PCM", report)
        self.assertIn("tone=", report)

    def test_rejects_silence(self):
        self.make_wave(amplitude=0)
        with self.assertRaisesRegex(ValueError, "silent"):
            audio.inspect(self.path)

    def test_rejects_wrong_pitch(self):
        self.make_wave(frequency=880)
        with self.assertRaisesRegex(ValueError, "expected about 440Hz"):
            audio.inspect(self.path)

    def test_rejects_short_capture(self):
        self.make_wave(ms=100, silence_ms=0)
        with self.assertRaisesRegex(ValueError, "too short"):
            audio.inspect(self.path)

    def test_rejects_wrong_bit_depth(self):
        self.make_wave(width=1)
        with self.assertRaisesRegex(ValueError, "unsupported capture format"):
            audio.inspect(self.path)


if __name__ == "__main__":
    unittest.main(verbosity=2)
