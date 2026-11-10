"""Regression: QEMU WAV capture validation must reject mid-tone pitch faults."""
import importlib.util
import math
from pathlib import Path
import struct
import tempfile
import unittest
import wave

ROOT = Path(__file__).resolve().parents[1]
VALIDATOR = ROOT / "tools" / "verify-audio-wav-capture.py"


def load_validator():
    spec = importlib.util.spec_from_file_location("audio_capture_validator", VALIDATOR)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def make_capture(path, fault=False):
    rate = 48000
    pcm = bytearray()
    for frame in range(rate * 4 // 10):
        time = frame / rate
        # 440 Hz normally; inject 880 Hz for a sustained middle segment.
        hz = 880 if fault and 0.15 <= time < 0.25 else 440
        sample = 2800 if math.sin(2 * math.pi * hz * time) >= 0 else -2800
        pcm.extend(struct.pack('<hh', sample, sample))
    with wave.open(str(path), 'wb') as output:
        output.setnchannels(2)
        output.setsampwidth(2)
        output.setframerate(rate)
        output.writeframes(pcm)


class TransientCaptureTests(unittest.TestCase):
    def test_valid_stereo_tone(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'valid.wav'
            make_capture(path)
            self.assertIn('PASS', load_validator().inspect(path, expected_hz=440))

    def test_mid_tone_pitch_corruption_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'corrupt.wav'
            make_capture(path, fault=True)
            with self.assertRaisesRegex(ValueError, 'Hz|pitch|tone'):
                load_validator().inspect(path, expected_hz=440)


if __name__ == '__main__':
    unittest.main()
