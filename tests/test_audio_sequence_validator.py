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
            right=True, tail_ms=90):
    pcm = array.array("h")
    for _ in range(rate // 4):
        pcm.extend((0, 0))
    for pitch in pitches:
        frames = rate * 90 // 1000
        for frame in range(frames):
            value = 4096 if ((frame * pitch * 2) // rate) % 2 == 0 else -4096
            pcm.extend((value, value if right else 0))
    for _ in range(rate * tail_ms // 1000):
        pcm.extend((0, 0))
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
