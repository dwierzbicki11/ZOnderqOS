#!/usr/bin/env python3
"""Compare two independent QEMU WAV captures for 25% software PCM gain.

This checks actual backend samples, not guest serial claims. It cannot
establish acoustic loudness or physical mixer/codec volume support.
"""
import argparse
import array
from pathlib import Path
import sys
import wave


def peak(path: Path):
    with wave.open(str(path), "rb") as stream:
        if stream.getsampwidth() != 2 or stream.getnchannels() not in (1, 2):
            raise ValueError("expected PCM16 mono/stereo WAV")
        raw = stream.readframes(stream.getnframes())
    samples = array.array("h")
    samples.frombytes(raw)
    if sys.byteorder != "little":
        samples.byteswap()
    if not samples:
        raise ValueError("empty capture")
    return max(abs(sample) for sample in samples)


def inspect(reference: Path, quiet: Path):
    normal_peak = peak(reference)
    quiet_peak = peak(quiet)
    if normal_peak < 512 or quiet_peak < 100:
        raise ValueError(f"insufficient signal normal={normal_peak} quiet={quiet_peak}")
    ratio = quiet_peak / normal_peak
    if not 0.16 <= ratio <= 0.36:
        raise ValueError(f"unexpected software gain ratio {ratio:.3f}; expected about 0.25")
    return f"[AUDIO-VOLUME] PASS QEMU PCM gain ratio={ratio:.3f} peaks={normal_peak}/{quiet_peak}"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("reference", type=Path)
    parser.add_argument("quiet", type=Path)
    args = parser.parse_args()
    try:
        print(inspect(args.reference, args.quiet))
        return 0
    except (OSError, EOFError, ValueError, wave.Error) as exc:
        print(f"[AUDIO-VOLUME] FAIL {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
