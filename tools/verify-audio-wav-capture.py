#!/usr/bin/env python3
"""Validate captured QEMU WAV samples, not serial-log claims.

Expected source: opt-in ZonderqOS 'tone' (440 Hz square, 250 ms, PCM16 stereo).
A PASS requires non-silent PCM with approximately the requested pitch.
This does not prove physical speakers or headphone output.
"""
import argparse
import array
from pathlib import Path
import sys
import wave


def inspect(path: Path, expected_hz: int = 440):
    if expected_hz not in (440, 660):
        raise ValueError("unsupported expected test frequency")
    with wave.open(str(path), "rb") as wav:
        channels = wav.getnchannels()
        rate = wav.getframerate()
        width = wav.getsampwidth()
        frames = wav.getnframes()
        if channels not in (1, 2) or width != 2 or rate not in (44100, 48000):
            raise ValueError(f"unsupported capture format: {rate}Hz {channels}ch {width * 8}bit")
        if frames < rate // 5:
            raise ValueError("capture too short for a 250 ms tone")
        if frames > rate * 90:
            raise ValueError("capture longer than 90 s; isolate one tone per run")
        raw = wav.readframes(frames)

    samples = array.array("h")
    samples.frombytes(raw)
    if sys.byteorder != "little":
        samples.byteswap()
    left = samples[::channels]
    peak = max(abs(x) for x in left)
    if peak < 512:
        raise ValueError(f"silent/too quiet capture (peak={peak})")
    # A stereo output must carry both channels. Looking only at the left
    # channel would incorrectly certify a broken right-channel HDA path.
    if channels == 2:
        right = samples[1::2]
        right_peak = max(abs(x) for x in right)
        if right_peak < 256 or right_peak * 2 < peak or right_peak > peak * 2:
            raise ValueError(f"stereo channel imbalance: left={peak} right={right_peak}")

    # Find an active 180 ms window. The first active sample may be a click;
    # insist that most of the window carries the expected square wave.
    window_frames = rate * 18 // 100
    start = None
    for i in range(0, len(left) - window_frames + 1, max(1, rate // 200)):
        chunk = left[i:i + window_frames]
        active = sum(abs(x) >= 256 for x in chunk)
        if active * 100 >= len(chunk) * 95:
            start = i
            break
    if start is None:
        raise ValueError("no sustained 180 ms PCM tone (may be noise/clicks)")

    chunk = left[start:start + window_frames]
    crossings = sum((a < 0) != (b < 0) for a, b in zip(chunk, chunk[1:]))
    hz = crossings * rate / (2 * (len(chunk) - 1))
    if not expected_hz * 0.90 <= hz <= expected_hz * 1.10:
        raise ValueError(f"captured signal has {hz:.1f}Hz, expected about {expected_hz}Hz")

    if channels == 2:
        right_chunk = right[start:start + window_frames]
        right_active = sum(abs(x) >= 256 for x in right_chunk)
        if right_active * 100 < len(right_chunk) * 95:
            raise ValueError("stereo right channel has no sustained 180 ms tone")
        right_crossings = sum((a < 0) != (b < 0) for a, b in zip(right_chunk, right_chunk[1:]))
        right_hz = right_crossings * rate / (2 * (len(right_chunk) - 1))
        if not expected_hz * 0.90 <= right_hz <= expected_hz * 1.10:
            raise ValueError(f"stereo right channel wrong pitch: {right_hz:.1f}Hz (expected {expected_hz}Hz)")
        # Every diagnostic source sends identical samples to both channels.
        # Opposite polarity or a phase-shifted second signal is not valid stereo.
        aligned = sum((a < 0) == (b < 0) for a, b in zip(chunk, right_chunk))
        if aligned * 100 < len(chunk) * 95:
            raise ValueError("stereo channels are not phase-aligned")

    return f"[AUDIO-WAV] PASS captured PCM: {rate}Hz {channels}ch, peak={peak}, tone={hz:.1f}Hz, frames={frames}"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("wav", type=Path, help="QEMU WAV backend output file")
    parser.add_argument("--expected-hz", type=int, choices=(440, 660), default=440)
    args = parser.parse_args()
    try:
        print(inspect(args.wav, expected_hz=args.expected_hz))
    except (OSError, EOFError, ValueError, wave.Error) as exc:
        print(f"[AUDIO-WAV] FAIL {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
