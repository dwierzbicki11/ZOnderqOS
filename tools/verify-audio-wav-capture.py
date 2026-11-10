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
        if len(raw) != frames * channels * width:
            raise ValueError("truncated capture PCM payload")

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
            if not starts:
                starts.append(i)
            elif i >= starts[0] + max(window_frames, rate // 8):
                if len(starts) == 1:
                    starts.append(i)
                else:
                    # Keep the middle of the sustained signal too: checking
                    # only the first and last windows misses mid-tone faults.
                    starts.append(i)
    if len(starts) < 2:
        raise ValueError("no two disjoint 80 ms PCM tone windows (may be noise/clicks)")
    # Validate every sustained candidate window, including transient faults.

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
            # A healthy peak elsewhere must not mask a weak channel in
            # either of the two pitch-bearing windows.
            left_window_peak = max(abs(x) for x in chunk)
            right_window_peak = max(abs(x) for x in right_chunk)
            if (right_window_peak * 2 < left_window_peak
                    or right_window_peak > left_window_peak * 2):
                raise ValueError("stereo channel imbalance in tone window")
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


def inspect_silent_tail(path: Path, expected_hz: int = 2860):
    """Require captured zero PCM after a float probe's 250ms tone.

    The guest fixture appends 4096 zero frames (>=85ms). A backend WAV
    must contain at least 300ms total and finish with >=20ms of near-zero
    samples. This is a captured-PCM check, not a guest DMA/IRQ assertion.
    """
    if expected_hz not in (2860, 3960):
        raise ValueError("unsupported float tail probe frequency")
    with wave.open(str(path), "rb") as wav:
        channels = wav.getnchannels()
        rate = wav.getframerate()
        width = wav.getsampwidth()
        frames = wav.getnframes()
        if channels != 2 or width != 2 or rate not in (44100, 48000):
            raise ValueError("float tail capture must be stereo PCM16")
        if frames < rate * 3 // 10:
            raise ValueError("float tail capture ended before 300ms")
        if frames > rate * 90:
            raise ValueError("float tail capture longer than 90 s; isolate one probe per run")
        raw = wav.readframes(frames)
        if len(raw) != frames * channels * width:
            raise ValueError("truncated capture PCM payload")
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
    # Require two separated pitch-bearing windows before the quiet tail.
    # An arbitrary loud noise burst is not proof that the float tone played.
    left = pre_tail[::2]
    right = pre_tail[1::2]
    window_frames = rate * 8 // 100
    starts = []
    for i in range(0, len(left) - window_frames + 1, max(1, rate // 200)):
        chunk = left[i:i + window_frames]
        if sum(abs(x) >= 256 for x in chunk) * 100 < len(chunk) * 95:
            continue
        if starts and i < starts[0] + max(window_frames, rate // 8):
            continue
        if not starts:
            starts.append(i)
        elif len(starts) == 1:
            starts.append(i)
        else:
            # Also inspect a middle window: a transient pitch fault can
            # escape checks limited to the first and final windows.
            starts.append(i)
    if len(starts) < 2:
        raise ValueError("missing two sustained float tone windows")
    # Validate all candidate windows; first/last alone miss transient faults.
    for start in starts:
        for channel in (left, right):
            chunk = channel[start:start + window_frames]
            if sum(abs(x) >= 256 for x in chunk) * 100 < len(chunk) * 95:
                raise ValueError("missing sustained float tone in stereo channel")
            crossings = sum((a < 0) != (b < 0) for a, b in zip(chunk, chunk[1:]))
            hz = crossings * rate / (2 * (len(chunk) - 1))
            if not expected_hz * 0.90 <= hz <= expected_hz * 1.10:
                raise ValueError(f"float tail wrong pitch: {hz:.1f}Hz")
        # A barely audible right channel can share pitch and phase while
        # indicating a broken stereo path. Check balance in each window.
        left_peak = max(abs(x) for x in left[start:start + window_frames])
        right_peak = max(abs(x) for x in right[start:start + window_frames])
        if right_peak * 2 < left_peak or right_peak > left_peak * 2:
            raise ValueError("float tail stereo channel imbalance")
        # Both channels must carry the same in-phase probe.
        left_chunk = left[start:start + window_frames]
        right_chunk = right[start:start + window_frames]
        aligned = sum((a < 0) == (b < 0) for a, b in zip(left_chunk, right_chunk))
        if aligned * 100 < window_frames * 95:
            raise ValueError("float tail stereo channels are not phase-aligned")
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
