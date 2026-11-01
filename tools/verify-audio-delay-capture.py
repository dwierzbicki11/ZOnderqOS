#!/usr/bin/env python3
"""Check relative PCM channel onset in a real QEMU HDA WAV capture.

The opt-in mixdelay diagnostic schedules right-channel audio 2400 frames
(50 ms at 48 kHz) after left-channel audio. A serial PASS or merely
non-silent capture cannot establish that timing.
"""
import argparse
import array
from pathlib import Path
import sys
import wave


def first_sustained(samples, rate):
    """First 5 ms window containing at least 80% nontrivial PCM samples."""
    window = max(32, rate // 200)
    if len(samples) < window:
        raise ValueError("capture too short for onset analysis")
    active = sum(abs(value) >= 256 for value in samples[:window])
    if active * 5 >= window * 4:
        return 0
    for index in range(window, len(samples)):
        active += abs(samples[index]) >= 256
        active -= abs(samples[index - window]) >= 256
        if active * 5 >= window * 4:
            return index - window + 1
    raise ValueError("no sustained audio onset")


def inspect(path: Path, expected_delay_ms=50):
    with wave.open(str(path), "rb") as wav:
        rate = wav.getframerate()
        channels = wav.getnchannels()
        width = wav.getsampwidth()
        frames = wav.getnframes()
        if rate not in (44100, 48000) or channels != 2 or width != 2:
            raise ValueError("requires 44.1/48kHz stereo PCM16")
        if frames < rate // 3 or frames > rate * 90:
            raise ValueError("invalid capture duration")
        raw = wav.readframes(frames)
    samples = array.array("h")
    samples.frombytes(raw)
    if sys.byteorder != "little":
        samples.byteswap()
    if len(samples) != frames * 2:
        raise ValueError("truncated stereo PCM")
    left_start = first_sustained(samples[::2], rate)
    right_start = first_sustained(samples[1::2], rate)
    actual_ms = (right_start - left_start) * 1000.0 / rate
    if not expected_delay_ms - 18 <= actual_ms <= expected_delay_ms + 18:
        raise ValueError(
            f"right-channel onset offset {actual_ms:.1f}ms; "
            f"expected about {expected_delay_ms}ms")
    return (f"[AUDIO-DELAY] PASS measured right-channel onset delay="
            f"{actual_ms:.1f}ms (expected {expected_delay_ms}ms), "
            f"rate={rate}Hz, frames={frames}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("wav", type=Path)
    args = parser.parse_args()
    try:
        print(inspect(args.wav))
        return 0
    except (OSError, EOFError, ValueError, wave.Error) as exc:
        print(f"[AUDIO-DELAY] FAIL {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
