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


# Shared by the Python API and CLI; adding a diagnostic pitch must update
# one allowlist rather than silently failing the real QEMU capture gate.
SUPPORTED_TEST_HZ = (400, 440, 550, 660, 740, 880, 1100, 1320, 1500, 1760, 2200, 2640, 2860, 3080, 3520, 3700, 3960)


def inspect(path: Path, expected_hz: int = 440):
    if expected_hz not in SUPPORTED_TEST_HZ:
        raise ValueError("unsupported expected test frequency")
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
    # A stereo output must carry both channels. Looking only at the left
    # channel would incorrectly certify a broken right-channel HDA path.
    if channels == 2:
        right = samples[1::2]
        right_peak = max(abs(x) for x in right)
        if right_peak < 256 or right_peak * 2 < peak or right_peak > peak * 2:
            raise ValueError(f"stereo channel imbalance: left={peak} right={right_peak}")

    # Emulated HDA can insert short (~21 ms) silence gaps even while a
    # real 250 ms PCM tone is present. Do not mistake that for a click, but
    # never accept a single short burst: require >=200 ms of active samples
    # AND two disjoint 80 ms windows with the expected pitch on both channels.
    if sum(abs(x) >= 256 for x in left) < rate // 5:
        raise ValueError("no sustained 200 ms PCM tone (may be noise/clicks)")
    window_frames = rate * 8 // 100
    starts = []
    for i in range(0, len(left) - window_frames + 1, max(1, rate // 200)):
        chunk = left[i:i + window_frames]
        active = sum(abs(x) >= 256 for x in chunk)
        if active * 100 >= len(chunk) * 95:
            if not starts or i >= starts[0] + window_frames:
                starts.append(i)
            if len(starts) == 2:
                break
    if len(starts) != 2:
        raise ValueError("no two disjoint 80 ms PCM tone windows (may be noise/clicks)")

    measured = []
    for start in starts:
        chunk = left[start:start + window_frames]
        crossings = sum((a < 0) != (b < 0) for a, b in zip(chunk, chunk[1:]))
        hz = crossings * rate / (2 * (len(chunk) - 1))
        if not expected_hz * 0.90 <= hz <= expected_hz * 1.10:
            raise ValueError(f"captured signal has {hz:.1f}Hz, expected about {expected_hz}Hz")
        measured.append(hz)

        if channels == 2:
            right_chunk = right[start:start + window_frames]
            right_active = sum(abs(x) >= 256 for x in right_chunk)
            if right_active * 100 < len(right_chunk) * 95:
                raise ValueError("stereo right channel has no sustained 80 ms tone")
            right_crossings = sum((a < 0) != (b < 0) for a, b in zip(right_chunk, right_chunk[1:]))
            right_hz = right_crossings * rate / (2 * (len(right_chunk) - 1))
            if not expected_hz * 0.90 <= right_hz <= expected_hz * 1.10:
                raise ValueError(f"stereo right channel wrong pitch: {right_hz:.1f}Hz (expected {expected_hz}Hz)")
            aligned = sum((a < 0) == (b < 0) for a, b in zip(chunk, right_chunk))
            if aligned * 100 < len(chunk) * 95:
                raise ValueError("stereo channels are not phase-aligned")

    hz = sum(measured) / len(measured)

    return f"[AUDIO-WAV] PASS captured PCM: {rate}Hz {channels}ch, peak={peak}, tone={hz:.1f}Hz, frames={frames}"


def inspect_silent_tail(path: Path):
    """Require captured zero PCM after a float probe's 250ms tone.

    The guest fixture appends 4096 zero frames (>=85ms). A backend WAV
    must contain at least 300ms total and finish with >=20ms of near-zero
    samples. This is a captured-PCM check, not a guest DMA/IRQ assertion.
    """
    with wave.open(str(path), "rb") as wav:
        channels = wav.getnchannels()
        rate = wav.getframerate()
        width = wav.getsampwidth()
        frames = wav.getnframes()
        if channels != 2 or width != 2 or rate not in (44100, 48000):
            raise ValueError("float tail capture must be stereo PCM16")
        if frames < rate * 3 // 10:
            raise ValueError("float tail capture ended before 300ms")
        raw = wav.readframes(frames)
    samples = array.array("h")
    samples.frombytes(raw)
    if sys.byteorder != "little":
        samples.byteswap()
    # A silent backend file is not proof that the preceding float probe played.
    # Require substantial non-silent stereo PCM before the final quiet tail.
    pre_tail = samples[:-((rate // 50) * 2)]
    if (sum(abs(sample) >= 256 for sample in pre_tail[::2]) < rate // 10
            or sum(abs(sample) >= 256 for sample in pre_tail[1::2]) < rate // 10):
        raise ValueError("missing sustained float tone before silent tail")
    tail_frames = rate // 50  # 20ms
    tail = samples[-tail_frames * 2:]
    if len(tail) != tail_frames * 2:
        raise ValueError("missing 20ms float tail")
    peak = max(abs(sample) for sample in tail)
    if peak > 32:
        raise ValueError(f"non-silent float WAV tail: peak={peak}")
    return f"[AUDIO-TAIL] PASS captured 20ms zero PCM tail, peak={peak}, frames={frames}"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("wav", type=Path, help="QEMU WAV backend output file")
    parser.add_argument("--expected-hz", type=int, choices=SUPPORTED_TEST_HZ, default=440)
    args = parser.parse_args()
    try:
        print(inspect(args.wav, expected_hz=args.expected_hz))
    except (OSError, EOFError, ValueError, wave.Error) as exc:
        print(f"[AUDIO-WAV] FAIL {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
