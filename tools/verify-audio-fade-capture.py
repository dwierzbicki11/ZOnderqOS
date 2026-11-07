#!/usr/bin/env python3
"""Check actual QEMU HDA PCM16 capture for a 20 ms attack/release envelope."""
import argparse
import array
from pathlib import Path
import sys
import wave


def inspect(path: Path):
    with wave.open(str(path), "rb") as stream:
        rate = stream.getframerate()
        frames = stream.getnframes()
        if stream.getnchannels() != 2 or stream.getsampwidth() != 2 or rate not in (44100, 48000):
            raise ValueError("fade capture requires 44.1/48 kHz stereo PCM16")
        if frames < rate // 5 or frames > rate * 90:
            raise ValueError("invalid fade capture duration")
        raw = stream.readframes(frames)
    samples = array.array("h")
    samples.frombytes(raw)
    if sys.byteorder != "little":
        samples.byteswap()
    if len(samples) != frames * 2:
        raise ValueError("truncated fade capture")

    results = []
    for channel in (0, 1):
        pcm = samples[channel::2]
        first = next((i for i, sample in enumerate(pcm) if abs(sample) >= 64), None)
        if first is None:
            raise ValueError("silent fade capture channel")
        last = next(i for i in range(len(pcm) - 1, -1, -1) if abs(pcm[i]) >= 64)
        if last - first < rate * 18 // 100:
            raise ValueError("fade signal shorter than 180ms")
        early_count = rate * 5 // 1000
        early = max(abs(v) for v in pcm[first:first + early_count])
        late = max(abs(v) for v in pcm[max(first, last - early_count):last + 1])
        middle = max(abs(v) for v in pcm[first + rate * 4 // 100:first + rate * 16 // 100])
        if middle < 512:
            raise ValueError("fade capture has no full-level middle")
        if early * 100 >= middle * 65:
            raise ValueError(f"missing fade-in: early={early} middle={middle}")
        if late * 100 >= middle * 65:
            raise ValueError(f"missing fade-out: late={late} middle={middle}")
        if sum(abs(v) >= 256 for v in pcm) < rate * 16 // 100:
            raise ValueError("fade capture has no sustained PCM")
        results.append(middle)
    if max(results) > min(results) * 2:
        raise ValueError("fade capture stereo channels are imbalanced")
    return f"[AUDIO-FADE] PASS captured 20ms stereo attack/release, middle peaks={results}, frames={frames}"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("wav", type=Path)
    args = parser.parse_args()
    try:
        print(inspect(args.wav))
    except (OSError, EOFError, ValueError, wave.Error) as exc:
        print(f"[AUDIO-FADE] FAIL {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
