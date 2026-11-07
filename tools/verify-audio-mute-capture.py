#!/usr/bin/env python3
"""Verify that an opt-in QEMU HDA playback at software volume 0% is silent.

This requires a real WAV backend capture with enough PCM frames; an absent
file, header-only WAV or unexecuted guest command is never a mute PASS.
"""
import argparse
import array
from pathlib import Path
import sys
import wave


def inspect(path: Path):
    with wave.open(str(path), "rb") as stream:
        rate = stream.getframerate()
        channels = stream.getnchannels()
        width = stream.getsampwidth()
        frames = stream.getnframes()
        if channels not in (1, 2) or width != 2 or rate not in (44100, 48000):
            raise ValueError(f"unsupported mute capture format: {rate}Hz {channels}ch {width * 8}bit")
        if frames < rate // 10 or frames > rate * 90:
            raise ValueError(f"mute capture duration invalid: {frames} frames at {rate}Hz")
        raw = stream.readframes(frames)
    if len(raw) != frames * channels * 2:
        raise ValueError("truncated mute capture")
    samples = array.array("h")
    samples.frombytes(raw)
    if sys.byteorder != "little":
        samples.byteswap()
    if not samples:
        raise ValueError("empty mute capture")
    peak = max(abs(sample) for sample in samples)
    if peak > 32:
        raise ValueError(f"mute leaked PCM: peak={peak} > 32")
    return f"[AUDIO-MUTE] PASS QEMU HDA muted PCM: {rate}Hz {channels}ch, peak={peak}, frames={frames}"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("wav", type=Path)
    args = parser.parse_args()
    try:
        print(inspect(args.wav))
        return 0
    except (OSError, EOFError, ValueError, wave.Error) as exc:
        print(f"[AUDIO-MUTE] FAIL {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
