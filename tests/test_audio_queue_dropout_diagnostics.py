#!/usr/bin/env python3
"""Synthetic checks for the diagnostic report; never replace the strict HDA gate."""
import importlib.util
from pathlib import Path
import struct
import tempfile
import unittest
import wave

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("queue_diag", ROOT / "tools/analyze-audio-queue-dropouts.py")
diag = importlib.util.module_from_spec(spec)
spec.loader.exec_module(diag)


def fixture(path, dropout=False):
    rate = 48000
    frames = rate * 8 // 10
    origin = rate // 10
    pcm = bytearray(frames * 4)
    for frame in range(frames):
        ms = (frame - origin) * 1000 // rate
        left = 0
        right = 0
        if 0 <= ms < 250:
            left = 4096 if ((frame - origin) * 440 * 2 // rate) & 1 == 0 else -4096
        if 50 <= ms < 300:
            right = 4096 if ((frame - origin - rate // 20) * 880 * 2 // rate) & 1 == 0 else -4096
        if 300 <= ms < 400 and not (dropout and 330 <= ms < 370):
            left = 4096 if ((frame - origin - 3 * rate // 10) * 660 * 2 // rate) & 1 == 0 else -4096
        struct.pack_into("<hh", pcm, frame * 4, left, right)
    with wave.open(str(path), "wb") as output:
        output.setnchannels(2)
        output.setsampwidth(2)
        output.setframerate(rate)
        output.writeframes(pcm)


class DiagnosticsTests(unittest.TestCase):
    def test_complete_capture_reports_no_false_underrun(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "ok.wav"
            fixture(path)
            report = diag.format_report(path)
            self.assertIn("right_offset=50.0ms", report)
            self.assertNotIn("WARN", report)
            self.assertGreater(diag.inspect(path)[3]["left-last"][0], 0.99)

    def test_missing_40ms_is_reported_without_passing_quality(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "drop.wav"
            fixture(path, dropout=True)
            report = diag.format_report(path)
            self.assertIn("WARN last effect incomplete", report)
            self.assertLess(diag.inspect(path)[3]["left-last"][0], 0.3)
            self.assertGreater(diag.inspect(path)[3]["left-last"][1], 30)

    def test_rejects_bad_wav(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "bad.wav"
            path.write_bytes(b"not wave")
            with self.assertRaises((ValueError, EOFError, wave.Error)):
                diag.inspect(path)


if __name__ == "__main__":
    unittest.main()
