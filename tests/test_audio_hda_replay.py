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

    def test_strict_inconclusive_stereo_channel_fails_closed(self):
        # Left-channel silence must not turn a clean right-channel signal
        # into a strict PASS. This is an inconclusive capture, not proof
        # that both DMA output channels were checked.
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'silent_left_clean_right.wav'
            right = [2908 if math.sin(2 * math.pi * 880 * i / RATE) >= 0
                     else -2908 for i in range(RATE // 2)]
            with wave.open(str(path), 'wb') as output:
                output.setnchannels(2)
                output.setsampwidth(2)
                output.setframerate(RATE)
                output.writeframes(b''.join(
                    struct.pack('<hh', 0, sample) for sample in right))
            channels = replay.analyze_channels(path, include_correlation=True)
            self.assertIsNone(channels[0])
            self.assertIsNotNone(channels[1])
            with patch('sys.argv', ['analyze-audio-hda-replay.py',
                                    str(path), '--strict']):
                self.assertEqual(replay.main(), 2)
            with patch('sys.argv', ['analyze-audio-hda-replay.py',
                                    str(path)]):
                self.assertEqual(replay.main(), 0)

    def test_strict_inconclusive_right_channel_fails_closed(self):
        # Symmetric case: a silent right channel cannot be reported as a
        # successful stereo DMA analysis just because left contains signal.
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'silent_right_clean_left.wav'
            left = [2908 if math.sin(2 * math.pi * 880 * i / RATE) >= 0
                    else -2908 for i in range(RATE // 2)]
            with wave.open(str(path), 'wb') as output:
                output.setnchannels(2)
                output.setsampwidth(2)
                output.setframerate(RATE)
                output.writeframes(b''.join(
                    struct.pack('<hh', sample, 0) for sample in left))
            channels = replay.analyze_channels(path, include_correlation=True)
            self.assertIsNotNone(channels[0])
            self.assertIsNone(channels[1])
            with patch('sys.argv', ['analyze-audio-hda-replay.py',
                                    str(path), '--strict']):
                self.assertEqual(replay.main(), 2)

    def test_48khz_ring_lag_and_stereo_replay(self):
        # Native HDA rate: one 16 KiB stereo16 ring is exactly 4096 frames.
        rate = 48000
        ring = 16384 // 4
        window = rate * 40 // 1000
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'native_rate_replay.wav'
            samples = [2908 if math.sin(2 * math.pi * 731 * i / rate) >= 0
                       else -2908 for i in range(rate // 2)]
            samples[ring:ring + window] = samples[:window]
            with wave.open(str(path), 'wb') as output:
                output.setnchannels(2)
                output.setsampwidth(2)
                output.setframerate(rate)
                output.writeframes(b''.join(
                    struct.pack('<hh', value, value) for value in samples))
            left, right = replay.analyze_channels(path)
            self.assertGreater(left[0], .99)
            self.assertGreater(right[0], .99)
            self.assertAlmostEqual(left[2], 1000 * ring / rate, places=5)

    def test_rejects_mono_and_invalid_wav(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'broken.wav'
            path.write_bytes(b'not a wav')
            with self.assertRaises((ValueError, EOFError, wave.Error)):
                replay.analyze(path)


if __name__ == '__main__':
    unittest.main()
