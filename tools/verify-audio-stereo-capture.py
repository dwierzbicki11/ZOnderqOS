#!/usr/bin/env python3
"""Verify independent stereo PCM from a real QEMU HDA WAV capture.

This is a sample-level test, not proof of physical speakers or IRQ delivery.
"""
import argparse
import array
from pathlib import Path
import sys
import wave


def inspect(path: Path, expected_left: int = 440, expected_right: int = 880):
    with wave.open(str(path), "rb") as wav:
        rate = wav.getframerate()
        channels = wav.getnchannels()
        width = wav.getsampwidth()
        frames = wav.getnframes()
        if channels != 2 or width != 2 or rate not in (44100, 48000):
            raise ValueError("independent stereo requires 16-bit two-channel PCM")
        if frames < rate // 5 or frames > rate * 90:
            raise ValueError("invalid capture duration")
        raw = wav.readframes(frames)

    samples = array.array("h")
    samples.frombytes(raw)
    if sys.byteorder != "little":
        samples.byteswap()
    left, right = samples[::2], samples[1::2]
    if len(left) != frames or len(right) != frames:
        raise ValueError("truncated stereo PCM")
    if max(abs(v) for v in left) < 512 or max(abs(v) for v in right) < 512:
        raise ValueError("silent stereo channel")
    if sum(abs(v) >= 256 for v in left) < rate // 5 or sum(abs(v) >= 256 for v in right) < rate // 5:
        raise ValueError("no sustained 200 ms stereo signal")

    window = rate * 8 // 100
    starts = []
    for start in range(0, frames - window + 1, max(1, rate // 200)):
        l = left[start:start + window]
        r = right[start:start + window]
        if (sum(abs(v) >= 256 for v in l) * 100 >= window * 95 and
                sum(abs(v) >= 256 for v in r) * 100 >= window * 95):
            if not starts or start >= starts[0] + window:
                starts.append(start)
            if len(starts) == 2:
                break
    if len(starts) != 2:
        raise ValueError("no two disjoint 80 ms stereo windows")

    pitches = []
    for start in starts:
        measured = []
        for samples_channel, expected, name in (
                (left, expected_left, "left"), (right, expected_right, "right")):
            segment = samples_channel[start:start + window]
            crossings = sum((a < 0) != (b < 0) for a, b in zip(segment, segment[1:]))
            hz = crossings * rate / (2 * (window - 1))
            if not expected * 0.90 <= hz <= expected * 1.10:
                raise ValueError(f"{name} channel has {hz:.1f}Hz, expected about {expected}Hz")
            measured.append(hz)
        if abs(measured[0] - measured[1]) < 200:
            raise ValueError("stereo channels are not independent")
        pitches.append(measured)
    avg_left = sum(v[0] for v in pitches) / len(pitches)
    avg_right = sum(v[1] for v in pitches) / len(pitches)
    return (f"[AUDIO-STEREO] PASS captured independent PCM: {rate}Hz stereo16, "
            f"left={avg_left:.1f}Hz right={avg_right:.1f}Hz, frames={frames}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("wav", type=Path)
    args = parser.parse_args()
    try:
        print(inspect(args.wav))
        return 0
    except (OSError, EOFError, ValueError, wave.Error) as exc:
        print(f"[AUDIO-STEREO] FAIL {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
