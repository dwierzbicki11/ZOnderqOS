#!/usr/bin/env python3
"""Quantify dropouts in captured HDA mixqueue WAV without turning failures into PASS.

Expected effects relative to first sustained left onset:
left 440 Hz 0..250 ms; right 880 Hz 50..300 ms; left 660 Hz 300..400 ms.
This tool is diagnostic only; verify-audio-queue-capture.py remains the gate.
"""
import argparse
import array
from pathlib import Path
import sys
import wave

THRESHOLD = 256


def first_sustained(samples, rate):
    window = max(32, rate // 200)
    if len(samples) < window:
        raise ValueError("capture too short")
    active = sum(abs(v) >= THRESHOLD for v in samples[:window])
    if active * 5 >= window * 4:
        return 0
    for end in range(window, len(samples)):
        active += abs(samples[end]) >= THRESHOLD
        active -= abs(samples[end - window]) >= THRESHOLD
        if active * 5 >= window * 4:
            return end - window + 1
    raise ValueError("no sustained signal")


def window_metrics(samples, rate, origin, start_ms, end_ms):
    start = origin + start_ms * rate // 1000
    end = origin + end_ms * rate // 1000
    if start < 0 or end > len(samples) or start >= end:
        raise ValueError("capture too short for requested window")
    active = 0
    longest_inactive = 0
    current_inactive = 0
    for sample in samples[start:end]:
        if abs(sample) >= THRESHOLD:
            active += 1
            current_inactive = 0
        else:
            current_inactive += 1
            if current_inactive > longest_inactive:
                longest_inactive = current_inactive
    return active / (end - start), 1000.0 * longest_inactive / rate


def inspect(path):
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
    pcm = array.array("h")
    pcm.frombytes(raw)
    if sys.byteorder != "little":
        pcm.byteswap()
    if len(pcm) != frames * 2:
        raise ValueError("truncated stereo PCM")
    left, right = pcm[::2], pcm[1::2]
    origin = first_sustained(left, rate)
    right_origin = first_sustained(right, rate)
    windows = (
        ("left-first", left, 60, 140),
        ("right-effect", right, 100, 180),
        ("left-gap", left, 265, 285),
        ("left-last", left, 325, 375),
        ("right-after-end", right, 325, 345),
        ("left-after-end", left, 425, 445),
    )
    results = {}
    for name, samples, start, end in windows:
        results[name] = window_metrics(samples, rate, origin, start, end)
    offset_ms = (right_origin - origin) * 1000.0 / rate
    return rate, frames, offset_ms, results


def format_report(path):
    rate, frames, offset, windows = inspect(path)
    lines = [f"[AUDIO-QUEUE-DIAG] {rate}Hz frames={frames} right_offset={offset:.1f}ms (diagnostic, not PASS)"]
    for name, (active, longest) in windows.items():
        lines.append(f"[AUDIO-QUEUE-DIAG] {name}: active={active:.2%} longest_silence={longest:.1f}ms")
    if windows["left-last"][0] < 0.85:
        lines.append("[AUDIO-QUEUE-DIAG] WARN last effect incomplete; possible HDA DMA refill/TCG underrun, not proven ISR failure")
    if windows["right-after-end"][0] > 0.08:
        lines.append("[AUDIO-QUEUE-DIAG] WARN right-channel PCM persists beyond scheduled end")
    if windows["left-after-end"][0] > 0.08:
        lines.append("[AUDIO-QUEUE-DIAG] WARN left-channel PCM persists beyond scheduled end")
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("wav", type=Path)
    args = parser.parse_args()
    try:
        print(format_report(args.wav))
        return 0
    except (OSError, EOFError, ValueError, wave.Error) as exc:
        print(f"[AUDIO-QUEUE-DIAG] INVALID {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
