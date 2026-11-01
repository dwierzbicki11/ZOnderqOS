#!/usr/bin/env python3
"""Diagnose suspected cyclic HDA PCM replay in actual QEMU WAV captures.

A matching active waveform one 16 KiB / 48 kHz stereo16 ring later is
consistent with a stale DMA buffer or delayed refill. This heuristic does
not prove that a guest IRQ was (or was not) delivered. Silence is excluded.
"""
import argparse
import array
from pathlib import Path
import sys
import wave


def analyze(path: Path, ring_bytes=16384, source_rate=48000):
    with wave.open(str(path), 'rb') as stream:
        rate = stream.getframerate()
        channels = stream.getnchannels()
        width = stream.getsampwidth()
        frames = stream.getnframes()
        if channels != 2 or width != 2 or rate not in (44100, 48000):
            raise ValueError('expected 44.1/48 kHz stereo PCM16 capture')
        if frames > rate * 90:
            raise ValueError('capture exceeds 90 seconds')
        samples = array.array('h')
        samples.frombytes(stream.readframes(frames))
    if sys.byteorder != 'little':
        samples.byteswap()
    if len(samples) != frames * 2:
        raise ValueError('truncated stereo PCM capture')
    left = samples[::2]
    ring_frames = round(rate * ring_bytes / (source_rate * 4))
    window = rate * 40 // 1000
    if len(left) < ring_frames + window:
        raise ValueError('capture too short for DMA ring replay analysis')
    best_fraction = -1.0
    best_start = 0
    for start in range(0, len(left) - ring_frames - window + 1, max(1, window // 2)):
        first = left[start:start + window]
        second = left[start + ring_frames:start + ring_frames + window]
        if (sum(abs(value) >= 256 for value in first) < window * 0.95 or
                sum(abs(value) >= 256 for value in second) < window * 0.95):
            continue
        fraction = sum(a == b for a, b in zip(first, second)) / window
        if fraction > best_fraction:
            best_fraction = fraction
            best_start = start
    if best_fraction < 0:
        raise ValueError('no two sufficiently active windows for replay analysis')
    return best_fraction, best_start / rate * 1000, ring_frames / rate * 1000


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('wav', type=Path)
    parser.add_argument('--strict', action='store_true',
                        help='fail if active PCM repeats at the ring interval')
    args = parser.parse_args()
    try:
        fraction, start_ms, period_ms = analyze(args.wav)
        suspected = fraction >= 0.97
        level = 'WARN suspected ring replay' if suspected else 'INFO no ring replay detected'
        print(f'[AUDIO-DMA] {level}: identical active PCM={fraction:.2%} '
              f'at {start_ms:.1f}ms and {start_ms + period_ms:.1f}ms '
              f'(16KiB/48kHz stereo16 period={period_ms:.2f}ms); '
              f'not proof of guest ISR delivery')
        return 1 if args.strict and suspected else 0
    except (OSError, EOFError, ValueError, wave.Error) as exc:
        print(f'[AUDIO-DMA] INCONCLUSIVE {exc}', file=sys.stderr)
        return 2 if args.strict else 0


if __name__ == '__main__':
    sys.exit(main())
