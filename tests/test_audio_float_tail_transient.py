"""Regression for float-tone QEMU capture: a quiet tail must not mask bad PCM."""
import importlib.util
import math
from pathlib import Path
import struct
import tempfile
import unittest
import wave

ROOT = Path(__file__).resolve().parents[1]
VALIDATOR = ROOT / "tools" / "verify-audio-wav-capture.py"


def inspect(path):
    spec = importlib.util.spec_from_file_location("audio_wav_validator", VALIDATOR)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.inspect_silent_tail(path, expected_hz=2860)


def write_capture(path, fault=None):
    rate = 48000
    pcm = bytearray()
    for frame in range(rate * 6 // 10):
        time = frame / rate
        if time >= 0.4:
            left = right = 0
        else:
            frequency = 2860
            if fault == "mid_pitch" and 0.15 <= time < 0.25:
                frequency = 3960
            left = 2800 if math.sin(2 * math.pi * frequency * time) >= 0 else -2800
            right = left
            if 0.15 <= time < 0.25:
                if fault == "right_silent":
                    right = 0
                elif fault == "right_inverted":
                    right = -left
        pcm.extend(struct.pack("<hh", left, right))
    with wave.open(str(path), "wb") as wav:
        wav.setnchannels(2)
        wav.setsampwidth(2)
        wav.setframerate(rate)
        wav.writeframes(pcm)


class FloatTailTransientTests(unittest.TestCase):
    def check(self, fault=None):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "capture.wav"
            write_capture(path, fault)
            return inspect(path)

    def test_valid_tone_followed_by_silence(self):
        self.assertIn("PASS", self.check())

    def test_middle_pitch_fault_rejected(self):
        with self.assertRaisesRegex(ValueError, "pitch"):
            self.check("mid_pitch")

    def test_middle_right_dropout_rejected(self):
        with self.assertRaisesRegex(ValueError, "stereo|sustained"):
            self.check("right_silent")

    def test_middle_right_phase_inversion_rejected(self):
        with self.assertRaisesRegex(ValueError, "phase"):
            self.check("right_inverted")


if __name__ == "__main__":
    unittest.main()
