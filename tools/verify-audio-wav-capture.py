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


def inspect(path: Path):
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

    # Find an active 180 ms window. The first active sample may be a click;
    # insist that most of the window carries the expected square wave.
    window_frames = rate * 18 // 100
    start = None
    for i in range(0, len(left) - window_frames + 1, max(1, rate // 200)):
        chunk = left[i:i + window_frames]
        active = sum(abs(x) >= 256 for x in chunk)
        if active * 100 >= len(chunk) * 80:
            start = i
            break
    if start is None:
        raise ValueError("no sustained 180 ms PCM tone (may be noise/clicks)")

    chunk = left[start:start + window_frames]
    crossings = sum((a < 0) != (b < 0) for a, b in zip(chunk, chunk[1:]))
    hz = crossings * rate / (2 * (len(chunk) - 1))
    if not 400 <= hz <= 480:
        raise ValueError(f"captured signal has {hz:.1f}Hz, expected about 440Hz")

    return f"[AUDIO-WAV] PASS captured PCM: {rate}Hz {channels}ch, peak={peak}, tone={hz:.1f}Hz, frames={frames}"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("wav", type=Path, help="QEMU WAV backend output file")
    args = parser.parse_args()
    try:
        print(inspect(args.wav))
    except (OSError, EOFError, ValueError, wave.Error) as exc:
        print(f"[AUDIO-WAV] FAIL {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
