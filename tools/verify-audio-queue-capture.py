#!/usr/bin/env python3
"""Verify three timed PCM effects in a real QEMU HDA WAV capture.

mixqueue emits left 440 Hz (0..250 ms), right 880 Hz (50..300 ms),
and a second left 660 Hz (300..400 ms). This gate inspects actual WAV
samples, not serial logs or a synthetic playback assertion.
"""
import argparse
import array
from pathlib import Path
import sys
import wave


def first_sustained(samples, rate):
    window = max(32, rate // 200)  # 5 ms
    if len(samples) < window:
        raise ValueError("capture too short")
    active = sum(abs(value) >= 256 for value in samples[:window])
    if active * 5 >= window * 4:
        return 0
    for index in range(window, len(samples)):
        active += abs(samples[index]) >= 256
        active -= abs(samples[index - window]) >= 256
        if active * 5 >= window * 4:
            return index - window + 1
    raise ValueError("no sustained audio onset")


def window_at(samples, rate, origin, start_ms, end_ms):
    start = origin + start_ms * rate // 1000
    end = origin + end_ms * rate // 1000
    if start < 0 or end > len(samples) or end <= start:
        raise ValueError("capture too short for scheduled windows")
    return samples[start:end]


def require_activity(samples, name, active=True):
    fraction = sum(abs(value) >= 256 for value in samples) / len(samples)
    if active and fraction < 0.85:
        raise ValueError(f"{name} lacks sustained signal ({fraction:.2%})")
    if not active and fraction > 0.08:
        raise ValueError(f"{name} should be silent ({fraction:.2%} active)")


def require_pitch(samples, rate, expected, name):
    require_activity(samples, name)
    crossings = sum((a < 0) != (b < 0) for a, b in zip(samples, samples[1:]))
    pitch = crossings * rate / (2 * (len(samples) - 1))
    if not expected * 0.85 <= pitch <= expected * 1.15:
        raise ValueError(f"{name} pitch {pitch:.1f}Hz; expected {expected}Hz")
    return pitch


def inspect(path: Path):
    with wave.open(str(path), "rb") as stream:
        rate = stream.getframerate()
        channels = stream.getnchannels()
        width = stream.getsampwidth()
        frames = stream.getnframes()
        if rate not in (44100, 48000) or channels != 2 or width != 2:
            raise ValueError("requires 44.1/48kHz stereo PCM16")
        if frames < rate // 2 or frames > rate * 90:
            raise ValueError("invalid capture duration")
        raw = stream.readframes(frames)
    samples = array.array("h")
    samples.frombytes(raw)
    if sys.byteorder != "little":
        samples.byteswap()
    if len(samples) != frames * 2:
        raise ValueError("truncated stereo PCM")
    left = samples[::2]
    right = samples[1::2]
    left_start = first_sustained(left, rate)
    right_start = first_sustained(right, rate)
    offset_ms = (right_start - left_start) * 1000.0 / rate
    if not 32 <= offset_ms <= 68:
        raise ValueError(f"right-channel onset {offset_ms:.1f}ms; expected 50ms")

    first_pitch = require_pitch(window_at(left, rate, left_start, 60, 140), rate, 440, "first left clip")
    right_pitch = require_pitch(window_at(right, rate, left_start, 100, 180), rate, 880, "right clip")
    require_activity(window_at(left, rate, left_start, 265, 285), "left inter-clip gap", False)
    last_pitch = require_pitch(window_at(left, rate, left_start, 325, 375), rate, 660, "last left clip")
    require_activity(window_at(right, rate, left_start, 325, 345), "right after EOF", False)
    require_activity(window_at(left, rate, left_start, 425, 445), "left after EOF", False)
    return (f"[AUDIO-QUEUE] PASS captured 3 timed PCM clips: "
            f"left={first_pitch:.1f}Hz then {last_pitch:.1f}Hz, "
            f"right={right_pitch:.1f}Hz at +{offset_ms:.1f}ms, rate={rate}Hz")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("wav", type=Path)
    args = parser.parse_args()
    try:
        print(inspect(args.wav))
        return 0
    except (OSError, EOFError, ValueError, wave.Error) as exc:
        print(f"[AUDIO-QUEUE] FAIL {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
