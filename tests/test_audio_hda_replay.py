#!/usr/bin/env python3
"""Synthetic fixtures for HDA ring replay diagnostics (not hardware tests)."""
import importlib.util
import math
from pathlib import Path
import struct
import tempfile
import unittest
import wave

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location(
    'hda_replay', ROOT / 'tools/analyze-audio-hda-replay.py')
replay = importlib.util.module_from_spec(spec)
spec.loader.exec_module(replay)
RATE = 44100
RING = round(RATE * 16384 / (48000 * 4))
WINDOW = RATE * 40 // 1000


def fixture(path, repeat=False, silence=False):
    tone = [0 if silence else
            (2908 if math.sin(2 * math.pi * 440 * i / RATE) >= 0 else -2908)
            for i in range(RATE // 2)]
    if repeat:
        tone[RING:RING + WINDOW] = tone[:WINDOW]
    with wave.open(str(path), 'wb') as output:
        output.setnchannels(2)
        output.setsampwidth(2)
        output.setframerate(RATE)
        output.writeframes(b''.join(struct.pack('<hh', value, value) for value in tone))


class HdaReplayTests(unittest.TestCase):
    def test_natural_square_wave_does_not_match_ring_period(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'clean.wav'
            fixture(path)
            fraction, _, period = replay.analyze(path)
            self.assertLess(fraction, 0.5)
            self.assertAlmostEqual(period, 85.33, delta=0.05)

    def test_replayed_active_ring_is_detected(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'replayed.wav'
            fixture(path, repeat=True)
            fraction, start, _ = replay.analyze(path)
            self.assertGreater(fraction, 0.99)
            self.assertEqual(start, 0)

    def test_all_silence_cannot_be_mistaken_for_ring_replay(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'silent.wav'
            fixture(path, silence=True)
            with self.assertRaisesRegex(ValueError, 'no two sufficiently active'):
                replay.analyze(path)

    def test_rejects_mono_and_invalid_wav(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'broken.wav'
            path.write_bytes(b'not a wav')
            with self.assertRaises((ValueError, EOFError, wave.Error)):
                replay.analyze(path)


if __name__ == '__main__':
    unittest.main()
