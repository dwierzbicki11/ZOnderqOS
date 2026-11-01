#!/usr/bin/env python3
"""Verify four different 90 ms PCM tones in an actual QEMU HDA WAV capture.

Unlike a periodic single-tone test, the ordered 400/700/1000/1300 Hz
sequence changes every 90 ms. Replaying an old 16 KiB HDA DMA ring should
not satisfy the four independent frequency windows. This does not prove IRQ.
"""
import argparse
import array
from pathlib import Path
import sys
import wave

FREQUENCIES = (400, 700, 1000, 1300)


def first_sustained(samples, rate):
    window = max(32, rate // 200)
    if len(samples) < window:
        raise ValueError("capture too short")
    active = sum(abs(x) >= 256 for x in samples[:window])
    if active * 5 >= window * 4:
        return 0
    for at in range(window, len(samples)):
        active += abs(samples[at]) >= 256
        active -= abs(samples[at - window]) >= 256
        if active * 5 >= window * 4:
            return at - window + 1
    raise ValueError("no sustained PCM onset")


def segment(samples, rate, origin, start_ms, end_ms):
    start = origin + start_ms * rate // 1000
    end = origin + end_ms * rate // 1000
    if start < 0 or end > len(samples) or end <= start:
        raise ValueError("capture too short for four scheduled PCM segments and tail")
    return samples[start:end]


def require_continuous(samples, rate, name):
    # A 5 ms attack/release is intentional. A longer interior gap is not:
    # reject a stale/underrun HDA buffer even if its 45 ms pitch window passes.
    longest = 0
    current = 0
    active = 0
    for value in samples:
        if abs(value) >= 256:
            active += 1
            current = 0
        else:
            current += 1
            longest = max(longest, current)
    if active * 100 < len(samples) * 85 or longest > rate * 8 // 1000:
        raise ValueError(f"{name} has a PCM dropout or incomplete segment")


def inspect(path: Path):
    with wave.open(str(path), "rb") as wav:
        rate, channels, width, frames = (wav.getframerate(), wav.getnchannels(),
                                         wav.getsampwidth(), wav.getnframes())
        if rate not in (44100, 48000) or channels != 2 or width != 2:
            raise ValueError("requires 44.1/48 kHz stereo PCM16")
        if frames < rate // 2 or frames > rate * 90:
            raise ValueError("invalid capture duration")
        raw = wav.readframes(frames)
    samples = array.array("h")
    samples.frombytes(raw)
    if sys.byteorder != "little":
        samples.byteswap()
    if len(samples) != frames * 2:
        raise ValueError("truncated PCM capture")
    left, right = samples[::2], samples[1::2]
    origin = first_sustained(left, rate)
    right_origin = first_sustained(right, rate)
    if abs(right_origin - origin) > rate // 250:
        raise ValueError("stereo channel onset mismatch")

    pitches = []
    for i, expected in enumerate(FREQUENCIES):
        for name, channel in (("left", left), ("right", right)):
            require_continuous(segment(channel, rate, origin, i * 90 + 5, i * 90 + 85),
                               rate, f"{name} segment {i + 1}")
            window = segment(channel, rate, origin, i * 90 + 20, i * 90 + 65)
            fraction = sum(abs(x) >= 256 for x in window) / len(window)
            if fraction < 0.85:
                raise ValueError(f"{name} segment {i + 1} lacks sustained PCM")
            crossings = sum((a < 0) != (b < 0) for a, b in zip(window, window[1:]))
            pitch = crossings * rate / (2 * (len(window) - 1))
            if not expected * 0.85 <= pitch <= expected * 1.15:
                raise ValueError(f"{name} segment {i + 1}: {pitch:.1f} Hz, expected {expected} Hz")
            if name == "left":
                pitches.append(pitch)
    for name, channel in (("left", left), ("right", right)):
        tail = segment(channel, rate, origin, 375, 395)
        if sum(abs(x) >= 256 for x in tail) / len(tail) > 0.08:
            raise ValueError(f"{name} channel still active after sequence end")
    return ("[AUDIO-SEQUENCE] PASS four distinct 90ms HDA PCM segments "
            + "/".join(f"{pitch:.1f}" for pitch in pitches)
            + f"Hz, stereo, trailing silence, rate={rate}Hz")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("wav", type=Path)
    args = parser.parse_args()
    try:
        print(inspect(args.wav))
        return 0
    except (OSError, EOFError, ValueError, wave.Error) as exc:
        print(f"[AUDIO-SEQUENCE] FAIL {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
