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

    def test_right_only_replay_is_not_hidden_by_clean_left_channel(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'right_replay.wav'
            left = [2908 if math.sin(2 * math.pi * 440 * i / RATE) >= 0
                    else -2908 for i in range(RATE // 2)]
            right = [2908 if math.sin(2 * math.pi * 880 * i / RATE) >= 0
                     else -2908 for i in range(RATE // 2)]
            right[RING:RING + WINDOW] = right[:WINDOW]
            with wave.open(str(path), 'wb') as output:
                output.setnchannels(2)
                output.setsampwidth(2)
                output.setframerate(RATE)
                output.writeframes(b''.join(
                    struct.pack('<hh', a, b) for a, b in zip(left, right)))
            channels = replay.analyze_channels(path)
            self.assertLess(channels[0][0], 0.5)
            self.assertGreater(channels[1][0], 0.99)

    def test_silent_left_does_not_hide_active_right_replay(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'silent_left.wav'
            right = [2908 if math.sin(2 * math.pi * 880 * i / RATE) >= 0
                     else -2908 for i in range(RATE // 2)]
            right[RING:RING + WINDOW] = right[:WINDOW]
            with wave.open(str(path), 'wb') as output:
                output.setnchannels(2)
                output.setsampwidth(2)
                output.setframerate(RATE)
                output.writeframes(b''.join(
                    struct.pack('<hh', 0, b) for b in right))
            left_result, right_result = replay.analyze_channels(path)
            self.assertIsNone(left_result)
            self.assertGreater(right_result[0], 0.99)
            with self.assertRaisesRegex(ValueError, 'left windows'):
                replay.analyze(path)

    def test_all_silence_cannot_be_mistaken_for_ring_replay(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'silent.wav'
            fixture(path, silence=True)
            with self.assertRaisesRegex(ValueError, 'no two sufficiently active'):
                replay.analyze(path)

    def test_resampled_nonperiodic_ring_replay_correlation(self):
        # A 2% gain perturbation destroys exact sample equality but keeps
        # the resampled/repeated waveform highly correlated at the DMA lag.
        # The original three-value analyze() API remains compatible.
        tone = [(2908 + i % 97) *
                (1 if math.sin(2 * math.pi * 440 * i / RATE) >= 0 else -1)
                for i in range(RATE // 2)]
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'nonperiodic.wav'
            for repeated in (False, True):
                samples = tone.copy()
                if repeated:
                    samples[RING:RING + WINDOW] = [
                        int(value * .98) for value in tone[:WINDOW]]
                with wave.open(str(path), 'wb') as output:
                    output.setnchannels(2)
                    output.setsampwidth(2)
                    output.setframerate(RATE)
                    output.writeframes(b''.join(
                        struct.pack('<hh', value, value) for value in samples))
                exact = replay.analyze(path)
                detailed = replay.analyze_channels(path, include_correlation=True)[0]
                self.assertEqual(len(exact), 3)
                self.assertEqual(len(detailed), 5)
                if repeated:
                    self.assertLess(exact[0], .97)
                    self.assertGreater(detailed[3], .995)
                else:
                    self.assertLess(detailed[3], .995)

    def test_rejects_mono_and_invalid_wav(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'broken.wav'
            path.write_bytes(b'not a wav')
            with self.assertRaises((ValueError, EOFError, wave.Error)):
                replay.analyze(path)


if __name__ == '__main__':
    unittest.main()
