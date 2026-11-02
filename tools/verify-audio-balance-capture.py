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



def inspect_partial(path: Path, balance_percent: int):
    """Verify a *measured* partial balance, including independent channel pitch.

    The diagnostic uses equal-amplitude 440/880 Hz square waves. A partial
    balance attenuates one channel without changing its pitch; require two
    separate sustained windows so startup noise or one brief burst cannot pass.
    This validates captured PCM, not codec registers or physical speakers.
    """
    if balance_percent == 0 or not -99 <= balance_percent <= 99:
        raise ValueError("partial balance must be -99..-1 or 1..99")
    expected_left = 100 - max(0, balance_percent)
    expected_right = 100 - max(0, -balance_percent)
    if min(expected_left, expected_right) < 20:
        raise ValueError("partial balance test requires both gains >=20%")

    with wave.open(str(path), "rb") as stream:
        rate = stream.getframerate()
        frames = stream.getnframes()
        if stream.getnchannels() != 2 or stream.getsampwidth() != 2 or rate not in (44100, 48000):
            raise ValueError("partial balance capture requires stereo PCM16")
        if frames < rate // 5 or frames > rate * 90:
            raise ValueError("invalid partial balance capture duration")
        raw = stream.readframes(frames)
    samples = array.array("h")
    samples.frombytes(raw)
    if sys.byteorder != "little":
        samples.byteswap()
    if len(samples) != frames * 2:
        raise ValueError("truncated partial balance capture")
    left = samples[::2]
    right = samples[1::2]

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
        raise ValueError("partial balance lacks two sustained stereo windows")

    ratios = []
    for start in starts:
        l = left[start:start + window]
        r = right[start:start + window]
        for signal, hz_expected, name in ((l, 440, "left"), (r, 880, "right")):
            crossings = sum((a < 0) != (b < 0) for a, b in zip(signal, signal[1:]))
            hz = crossings * rate / (2 * (window - 1))
            if not hz_expected * 0.90 <= hz <= hz_expected * 1.10:
                raise ValueError(f"partial balance {name} pitch {hz:.1f}Hz")
        mean_left = sum(abs(v) for v in l) / len(l)
        mean_right = sum(abs(v) for v in r) / len(r)
        ratio = mean_left / mean_right
        expected_ratio = expected_left / expected_right
        if not 0.85 * expected_ratio <= ratio <= 1.15 * expected_ratio:
            raise ValueError(
                f"partial balance ratio {ratio:.3f}, expected {expected_ratio:.3f}")
        ratios.append(ratio)
    return (f"[AUDIO-BALANCE] PASS captured balance={balance_percent:+d} "
            f"left/right ratio={sum(ratios)/len(ratios):.3f}, "
            f"independent 440/880Hz, frames={frames}")


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
