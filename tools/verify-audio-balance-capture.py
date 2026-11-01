#!/usr/bin/env python3
"""Validate left/right-only PCM after balance -100/+100 in real QEMU HDA WAV capture.

This is sample-level evidence, not a serial-log or physical-speaker test.
"""
import argparse
import array
from pathlib import Path
import sys
import wave


def inspect(path: Path, expected_right: int = 880, active_channel: str = "right"):
    """Require real captured PCM on exactly one stereo channel.

    Balance +100 must mute left and retain right 880Hz; balance -100 must
    mute right and retain left 440Hz. Neither a boot nor a serial marker
    counts as successful output.
    """
    if active_channel not in ("left", "right"):
        raise ValueError("active_channel must be left or right")
    expected_hz = 440 if active_channel == "left" else expected_right
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
    active = left if active_channel == "left" else right
    muted = right if active_channel == "left" else left
    if max(abs(v) for v in muted) > 32:
        raise ValueError(f"muted {'right' if active_channel == 'left' else 'left'} channel contains PCM")
    if max(abs(v) for v in active) < 512:
        raise ValueError(f"{active_channel} channel is silent")
    if sum(abs(v) >= 256 for v in active) < rate // 5:
        raise ValueError(f"{active_channel} channel has no sustained 200ms signal")

    window = rate * 8 // 100
    starts = []
    for start in range(0, frames - window + 1, max(1, rate // 200)):
        segment = active[start:start + window]
        if sum(abs(v) >= 256 for v in segment) * 100 >= window * 95:
            if not starts or start >= starts[0] + window:
                starts.append(start)
            if len(starts) == 2:
                break
    if len(starts) != 2:
        raise ValueError(f"no two disjoint 80ms {active_channel}-channel windows")

    frequencies = []
    for start in starts:
        segment = active[start:start + window]
        crossings = sum((a < 0) != (b < 0) for a, b in zip(segment, segment[1:]))
        hz = crossings * rate / (2 * (window - 1))
        if not expected_hz * 0.90 <= hz <= expected_hz * 1.10:
            raise ValueError(f"{active_channel} channel {hz:.1f}Hz, expected about {expected_hz}Hz")
        frequencies.append(hz)
    mean = sum(frequencies) / len(frequencies)
    return f"[AUDIO-BALANCE] PASS captured {active_channel}-only {mean:.1f}Hz, silent {'right' if active_channel == 'left' else 'left'}, frames={frames}"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("wav", type=Path)
    parser.add_argument("--channel", choices=("left", "right"), default="right",
                        help="channel expected to remain audible after balance -100/+100")
    args = parser.parse_args()
    try:
        print(inspect(args.wav, active_channel=args.channel))
        return 0
    except (OSError, EOFError, ValueError, wave.Error) as exc:
        print(f"[AUDIO-BALANCE] FAIL {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
