#!/usr/bin/env python3
"""Validate right-only PCM after balance +100 in real QEMU HDA WAV capture.

This is sample-level evidence, not a serial-log or physical-speaker test.
"""
import argparse
import array
from pathlib import Path
import sys
import wave


def inspect(path: Path, expected_right: int = 880):
    with wave.open(str(path), "rb") as stream:
        rate = stream.getframerate()
        frames = stream.getnframes()
        if stream.getnchannels() != 2 or stream.getsampwidth() != 2 or rate not in (44100, 48000):
            raise ValueError("balance capture requires stereo PCM16")
        if frames < rate // 5 or frames > rate * 90:
            raise ValueError("invalid balance capture duration")
        raw = stream.readframes(frames)

    samples = array.array("h")
    samples.frombytes(raw)
    if sys.byteorder != "little":
        samples.byteswap()
    if len(samples) != frames * 2:
        raise ValueError("truncated balance capture")
    left = samples[::2]
    right = samples[1::2]
    if max(abs(v) for v in left) > 32:
        raise ValueError("muted left channel contains PCM")
    if max(abs(v) for v in right) < 512:
        raise ValueError("right channel is silent")
    if sum(abs(v) >= 256 for v in right) < rate // 5:
        raise ValueError("right channel has no sustained 200ms signal")

    window = rate * 8 // 100
    starts = []
    for start in range(0, frames - window + 1, max(1, rate // 200)):
        segment = right[start:start + window]
        if sum(abs(v) >= 256 for v in segment) * 100 >= window * 95:
            if not starts or start >= starts[0] + window:
                starts.append(start)
            if len(starts) == 2:
                break
    if len(starts) != 2:
        raise ValueError("no two disjoint 80ms right-channel windows")

    frequencies = []
    for start in starts:
        segment = right[start:start + window]
        crossings = sum((a < 0) != (b < 0) for a, b in zip(segment, segment[1:]))
        hz = crossings * rate / (2 * (window - 1))
        if not expected_right * 0.90 <= hz <= expected_right * 1.10:
            raise ValueError(f"right channel {hz:.1f}Hz, expected about {expected_right}Hz")
        frequencies.append(hz)
    mean = sum(frequencies) / len(frequencies)
    return f"[AUDIO-BALANCE] PASS captured right-only {mean:.1f}Hz, silent left, frames={frames}"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("wav", type=Path)
    args = parser.parse_args()
    try:
        print(inspect(args.wav))
        return 0
    except (OSError, EOFError, ValueError, wave.Error) as exc:
        print(f"[AUDIO-BALANCE] FAIL {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
