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

    def make_wave(self, frequency=440, amplitude=4096, ms=250, silence_ms=500, width=2, right_amplitude=None, right_click_only=False, invert_right=False):
        rate = 48000
        data = bytearray()
        for i in range(rate * (silence_ms + ms) // 1000):
            playing = i >= rate * silence_ms // 1000
            sample = (amplitude if ((i * frequency * 2 // rate) & 1) == 0 else -amplitude) if playing else 0
            if width == 2:
                right = sample if right_amplitude is None else (right_amplitude if sample > 0 else -right_amplitude) if playing else 0
                if right_click_only and i != rate * silence_ms // 1000:
                    right = 0
                if invert_right:
                    right = -right
                data.extend(struct.pack("<hh", sample, right))
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

    def test_rejects_silent_right_channel(self):
        self.make_wave(right_amplitude=0)
        with self.assertRaisesRegex(ValueError, "stereo channel imbalance"):
            audio.inspect(self.path)

    def test_rejects_quiet_right_channel(self):
        self.make_wave(right_amplitude=256)
        with self.assertRaisesRegex(ValueError, "stereo channel imbalance"):
            audio.inspect(self.path)

    def test_rejects_right_channel_click_without_tone(self):
        self.make_wave(right_click_only=True)
        with self.assertRaisesRegex(ValueError, "stereo right channel has no sustained"):
            audio.inspect(self.path)

    def test_distinct_mono_probe_pitch(self):
        self.make_wave(frequency=660)
        self.assertIn("PASS captured PCM", audio.inspect(self.path, expected_hz=660))
        with self.assertRaisesRegex(ValueError, "expected about 440Hz"):
            audio.inspect(self.path)

    def test_distinct_44100_resampler_probe_pitch(self):
        self.make_wave(frequency=880)
        self.assertIn("PASS captured PCM", audio.inspect(self.path, expected_hz=880))
        with self.assertRaisesRegex(ValueError, "expected about 660Hz"):
            audio.inspect(self.path, expected_hz=660)

    def test_distinct_8000_voice_rate_probe_pitch(self):
        # Synthetic 48kHz backend fixture, NOT a real 8kHz guest capture.
        self.make_wave(frequency=400)
        self.assertIn("PASS captured PCM", audio.inspect(self.path, expected_hz=400))
        with self.assertRaisesRegex(ValueError, "expected about 660Hz"):
            audio.inspect(self.path, expected_hz=660)

    def test_distinct_11025_voice_rate_probe_pitch(self):
        # Synthetic 48kHz backend fixture, NOT a real 11.025kHz guest capture.
        self.make_wave(frequency=550)
        self.assertIn("PASS captured PCM", audio.inspect(self.path, expected_hz=550))
        with self.assertRaisesRegex(ValueError, "expected about 400Hz"):
            audio.inspect(self.path, expected_hz=400)

    def test_distinct_16000_voice_rate_probe_pitch(self):
        # Synthetic 48kHz backend fixture, not a real 16kHz guest capture.
        self.make_wave(frequency=1500)
        self.assertIn("PASS captured PCM", audio.inspect(self.path, expected_hz=1500))
        with self.assertRaisesRegex(ValueError, "expected about 1320Hz"):
            audio.inspect(self.path, expected_hz=1320)

    def test_distinct_24000_stereo_probe_pitch(self):
        self.make_wave(frequency=740)
        self.assertIn("PASS captured PCM", audio.inspect(self.path, expected_hz=740))
        with self.assertRaisesRegex(ValueError, "expected about 880Hz"):
            audio.inspect(self.path, expected_hz=880)

    def test_distinct_22050_resampler_probe_pitch(self):
        # Host-side validator fixture, not a QEMU or hardware playback claim.
        self.make_wave(frequency=1100)
        self.assertIn("PASS captured PCM", audio.inspect(self.path, expected_hz=1100))
        with self.assertRaisesRegex(ValueError, "expected about 880Hz"):
            audio.inspect(self.path, expected_hz=880)

    def test_distinct_pcm8_probe_pitch(self):
        self.make_wave(frequency=1320)
        self.assertIn("PASS captured PCM", audio.inspect(self.path, expected_hz=1320))
        with self.assertRaisesRegex(ValueError, "expected about 880Hz"):
            audio.inspect(self.path, expected_hz=880)

    def test_distinct_pcm24_probe_pitch(self):
        self.make_wave(frequency=1760)
        self.assertIn("PASS captured PCM", audio.inspect(self.path, expected_hz=1760))
        with self.assertRaisesRegex(ValueError, "expected about 1320Hz"):
            audio.inspect(self.path, expected_hz=1320)

    def test_distinct_pcm32_probe_pitch(self):
        self.make_wave(frequency=2200)
        self.assertIn("PASS captured PCM", audio.inspect(self.path, expected_hz=2200))
        with self.assertRaisesRegex(ValueError, "expected about 1760Hz"):
            audio.inspect(self.path, expected_hz=1760)

    def test_distinct_ieee_float32_probe_pitch(self):
        # Synthetic backend PCM fixture; only the independent QEMU capture
        # can prove the guest float decoder actually drove HDA output.
        self.make_wave(frequency=2860)
        self.assertIn("PASS captured PCM", audio.inspect(self.path, expected_hz=2860))
        with self.assertRaisesRegex(ValueError, "expected about 2200Hz"):
            audio.inspect(self.path, expected_hz=2200)

    def test_float_hda_tail_requires_actual_captured_silence(self):
        # Synthetic PCM fixture: 250ms tone followed by 80ms zero tail.
        # A single nonzero final frame must fail the strict tail check.
        self.make_wave(ms=250, silence_ms=0)
        with wave.open(str(self.path), "rb") as wav:
            raw = wav.readframes(wav.getnframes())
        raw += bytes(48000 * 80 // 1000 * 4)
        with wave.open(str(self.path), "wb") as wav:
            wav.setnchannels(2)
            wav.setsampwidth(2)
            wav.setframerate(48000)
            wav.writeframes(raw)
        self.assertIn("PASS captured 20ms zero PCM tail",
                      audio.inspect_silent_tail(self.path))
        corrupt = bytearray(raw)
        corrupt[-4:] = struct.pack("<hh", 4096, -4096)
        with wave.open(str(self.path), "wb") as wav:
            wav.setnchannels(2)
            wav.setsampwidth(2)
            wav.setframerate(48000)
            wav.writeframes(corrupt)
        with self.assertRaisesRegex(ValueError, "non-silent float WAV tail"):
            audio.inspect_silent_tail(self.path)

    def test_float_hda_tail_rejects_short_capture(self):
        self.make_wave(ms=250, silence_ms=0)
        with self.assertRaisesRegex(ValueError, "before 300ms"):
            audio.inspect_silent_tail(self.path)

    def test_distinct_extensible_ieee_float_probe_pitch(self):
        # Synthetic capture only. Real QEMU must independently confirm
        # the new 44.1kHz extensible float decoder drove the HDA backend.
        self.make_wave(frequency=3960)
        self.assertIn("PASS captured PCM", audio.inspect(self.path, expected_hz=3960))
        with self.assertRaisesRegex(ValueError, "expected about 2860Hz"):
            audio.inspect(self.path, expected_hz=2860)

    def test_distinct_extensible_probe_pitch(self):
        self.make_wave(frequency=2640)
        self.assertIn("PASS captured PCM", audio.inspect(self.path, expected_hz=2640))
        with self.assertRaisesRegex(ValueError, "expected about 2200Hz"):
            audio.inspect(self.path, expected_hz=2200)

    def test_distinct_extensible_20bit_probe_pitch(self):
        self.make_wave(frequency=3080)
        self.assertIn("PASS captured PCM", audio.inspect(self.path, expected_hz=3080))
        with self.assertRaisesRegex(ValueError, "expected about 2640Hz"):
            audio.inspect(self.path, expected_hz=2640)

    def test_distinct_extensible_24in32_probe_pitch(self):
        # Synthetic validator fixture only; QEMU must capture its own PCM.
        self.make_wave(frequency=3700)
        self.assertIn("PASS captured PCM", audio.inspect(self.path, expected_hz=3700))
        with self.assertRaisesRegex(ValueError, "expected about 3080Hz"):
            audio.inspect(self.path, expected_hz=3080)

    def test_distinct_32000_resampler_probe_pitch(self):
        # Synthetic 3520 Hz capture: this only verifies the validator, not HDA.
        self.make_wave(frequency=3520)
        self.assertIn("PASS captured PCM", audio.inspect(self.path, expected_hz=3520))
        with self.assertRaisesRegex(ValueError, "expected about 3080Hz"):
            audio.inspect(self.path, expected_hz=3080)

    def test_accepts_hda_capture_with_two_short_underruns(self):
        # Matches the measured QEMU WAV capture: two 21 ms silent gaps,
        # but >200 ms of real 440 Hz samples on both channels.
        self.make_wave(ms=390, silence_ms=0)
        with wave.open(str(self.path), "rb") as wav:
            data = bytearray(wav.readframes(wav.getnframes()))
        for start_ms, end_ms in ((64, 85), (205, 226)):
            for i in range(48000 * start_ms // 1000, 48000 * end_ms // 1000):
                data[i * 4:i * 4 + 4] = bytes(4)
        with wave.open(str(self.path), "wb") as wav:
            wav.setnchannels(2)
            wav.setsampwidth(2)
            wav.setframerate(48000)
            wav.writeframes(data)
        self.assertIn("PASS captured PCM", audio.inspect(self.path))

    def test_rejects_two_short_bursts_below_total_activity_floor(self):
        self.make_wave(ms=390, silence_ms=0)
        with wave.open(str(self.path), "rb") as wav:
            data = bytearray(wav.readframes(wav.getnframes()))
        # Only two 80 ms bursts remain, not the required 200 ms of PCM.
        for i in range(48000 * 390 // 1000):
            if not (i < 48000 * 80 // 1000 or i >= 48000 * 310 // 1000):
                data[i * 4:i * 4 + 4] = bytes(4)
        with wave.open(str(self.path), "wb") as wav:
            wav.setnchannels(2)
            wav.setsampwidth(2)
            wav.setframerate(48000)
            wav.writeframes(data)
        with self.assertRaisesRegex(ValueError, "no sustained 200 ms"):
            audio.inspect(self.path)

    def test_rejects_inverted_right_channel(self):
        self.make_wave(invert_right=True)
        with self.assertRaisesRegex(ValueError, "not phase-aligned"):
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
