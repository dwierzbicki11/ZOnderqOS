#!/usr/bin/env python3
"""Positive and deliberately corrupted WAV fixtures for the HDA sequence gate."""
import array
from importlib.util import module_from_spec, spec_from_file_location
from pathlib import Path
import sys
import tempfile
import unittest
import wave

HERE = Path(__file__).resolve().parents[1] / "tools"
spec = spec_from_file_location("audio_sequence", HERE / "verify-audio-sequence-capture.py")
sequence = module_from_spec(spec)
spec.loader.exec_module(sequence)


def fixture(path, pitches=(400, 700, 1000, 1300), rate=44100,
            right=True, tail_ms=90, right_gain_percent=100, right_delay_ms=0,
            segment_gains_percent=(100, 100, 100, 100)):
    pcm = array.array("h")
    for _ in range(rate // 4):
        pcm.extend((0, 0))
    for index, pitch in enumerate(pitches):
        frames = rate * 90 // 1000
        for frame in range(frames):
            value = 4096 if ((frame * pitch * 2) // rate) % 2 == 0 else -4096
            value = value * segment_gains_percent[index] // 100
            pcm.extend((value, value * right_gain_percent // 100 if right else 0))
    for _ in range(rate * tail_ms // 1000):
        pcm.extend((0, 0))
    if right_delay_ms:
        # A small right-channel skew is below the 4 ms onset tolerance,
        # so the new sample-coherence gate must detect it independently.
        delay = rate * right_delay_ms // 1000
        for frame in range(len(pcm) // 2 - 1, -1, -1):
            pcm[frame * 2 + 1] = pcm[(frame - delay) * 2 + 1] if frame >= delay else 0
    if sys.byteorder != "little":
        pcm.byteswap()
    with wave.open(str(path), "wb") as wav:
        wav.setnchannels(2)
        wav.setsampwidth(2)
        wav.setframerate(rate)
        wav.writeframes(pcm.tobytes())


class SequenceGate(unittest.TestCase):
    def test_four_distinct_segments(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "valid.wav"
            fixture(path)
            self.assertIn("PASS", sequence.inspect(path))
            fixture(path, rate=48000)
            self.assertIn("PASS", sequence.inspect(path))

    def test_reject_repeated_or_reordered_segments(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "bad.wav"
            for pitches in ((400, 400, 400, 400), (400, 1000, 700, 1300),
                            (400, 700, 1000, 1000)):
                fixture(path, pitches=pitches)
                with self.assertRaisesRegex(ValueError, "expected"):
                    sequence.inspect(path)

    def test_reject_missing_channel_and_tail(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "bad.wav"
            fixture(path, right=False)
            with self.assertRaises(ValueError):
                sequence.inspect(path)
            fixture(path, tail_ms=0)
            with self.assertRaisesRegex(ValueError, "capture too short"):
                sequence.inspect(path)

    def test_reject_interior_pcm_dropout(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "dropout.wav"
            fixture(path)
            with wave.open(str(path), "rb") as wav:
                rate = wav.getframerate()
                data = array.array("h")
                data.frombytes(wav.readframes(wav.getnframes()))
            if sys.byteorder != "little":
                data.byteswap()
            # Remove 15 ms from the second effect, outside its 20..65 ms
            # frequency window. A pitch-only validator would miss this.
            start = (rate // 4 + rate * 90 // 1000 + rate * 68 // 1000) * 2
            end = start + rate * 15 // 1000 * 2
            for i in range(start, end):
                data[i] = 0
            if sys.byteorder != "little":
                data.byteswap()
            with wave.open(str(path), "wb") as wav:
                wav.setnchannels(2)
                wav.setsampwidth(2)
                wav.setframerate(rate)
                wav.writeframes(data.tobytes())
            with self.assertRaisesRegex(ValueError, "dropout"):
                sequence.inspect(path)

    def test_reject_stereo_phase_gain_and_subthreshold_skew(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "bad-stereo.wav"
            # All retain both active channels and the expected four pitches;
            # onset-only and pitch-only gates would otherwise accept them.
            for gain, delay in ((-100, 0), (50, 0), (100, 1)):
                fixture(path, right_gain_percent=gain, right_delay_ms=delay)
                with self.subTest(right_gain_percent=gain, right_delay_ms=delay):
                    with self.assertRaisesRegex(ValueError, "stereo phase/gain mismatch"):
                        sequence.inspect(path)

    def test_reject_per_effect_amplitude_drift(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "quiet-third-effect.wav"
            # Correct pitches, two coherent channels and no dropout; only
            # the third DMA-period-sized effect loses half its amplitude.
            fixture(path, segment_gains_percent=(100, 100, 50, 100))
            with self.assertRaisesRegex(ValueError, "amplitude drift"):
                sequence.inspect(path)

    def test_reject_non_pcm16(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "bad.wav"
            with wave.open(str(path), "wb") as wav:
                wav.setnchannels(1)
                wav.setsampwidth(1)
                wav.setframerate(44100)
                wav.writeframes(bytes(44100))
            with self.assertRaisesRegex(ValueError, "stereo PCM16"):
                sequence.inspect(path)


if __name__ == "__main__":
    unittest.main()
