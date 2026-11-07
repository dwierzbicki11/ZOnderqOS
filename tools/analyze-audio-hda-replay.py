#!/usr/bin/env python3
"""Diagnose suspected cyclic HDA PCM replay in actual QEMU WAV captures.

A matching active waveform one 16 KiB / 48 kHz stereo16 ring later is
consistent with a stale DMA buffer or delayed refill. QEMU's 44.1 kHz
resampling can change individual samples, so normalized correlation is also
reported. A periodic tone can match naturally: use nonperiodic test signals.
This heuristic does not prove guest IRQ delivery. Silence is excluded.
"""
import argparse
import array
import math
from pathlib import Path
import sys
import wave


def normalized_correlation(first, second):
    """Resampling-tolerant similarity for active windows; not an ISR proof."""
    n = len(first)
    sa, sb = sum(first), sum(second)
    aa = sum(x * x for x in first)
    bb = sum(y * y for y in second)
    ab = sum(x * y for x, y in zip(first, second))
    denominator = (n * aa - sa * sa) * (n * bb - sb * sb)
    return (n * ab - sa * sb) / math.sqrt(denominator) if denominator > 0 else 0.0


def analyze_channels(path: Path, ring_bytes=16384, source_rate=48000, include_correlation=False):
    # Reject invalid DMA geometry instead of interpreting a zero/negative lag
    # as a valid replay comparison (which could produce a false alarm).
    if not isinstance(ring_bytes, int) or ring_bytes <= 0 or ring_bytes % 4:
        raise ValueError('ring_bytes must be a positive stereo16 frame multiple')
    if not isinstance(source_rate, int) or source_rate < 8000 or source_rate > 192000:
        raise ValueError('source_rate must be a supported 8-192 kHz PCM rate')
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
    ring_frames = round(rate * ring_bytes / (source_rate * 4))
    if ring_frames <= 0:
        raise ValueError('DMA ring lag rounds to zero capture frames')
    window = rate * 40 // 1000
    if frames < ring_frames + window:
        raise ValueError('capture too short for DMA ring replay analysis')
    results = []
    for channel in (samples[::2], samples[1::2]):
        best_fraction = -1.0
        best_start = 0
        best_correlation = -2.0
        best_corr_start = 0
        for start in range(0, len(channel) - ring_frames - window + 1, max(1, window // 2)):
            first = channel[start:start + window]
            second = channel[start + ring_frames:start + ring_frames + window]
            # Use RMS activity rather than demanding 95% of individual samples
            # exceed a threshold: genuine sine waves cross zero regularly.
            if (sum(value * value for value in first) < window * 256 * 256 or
                    sum(value * value for value in second) < window * 256 * 256):
                continue
            fraction = sum(a == b for a, b in zip(first, second)) / window
            if fraction > best_fraction:
                best_fraction = fraction
                best_start = start
            if include_correlation:
                correlation = normalized_correlation(first, second)
                if correlation > best_correlation:
                    best_correlation = correlation
                    best_corr_start = start
        if best_fraction < 0:
            results.append(None)
        else:
            result = (best_fraction, best_start / rate * 1000,
                      ring_frames / rate * 1000)
            if include_correlation:
                result += (best_correlation, best_corr_start / rate * 1000)
            results.append(result)
    if all(result is None for result in results):
        raise ValueError('no two sufficiently active windows for replay analysis')
    return tuple(results)


def analyze(path: Path, ring_bytes=16384, source_rate=48000):
    """Backwards-compatible left-channel analysis; silent left is inconclusive."""
    left, _ = analyze_channels(path, ring_bytes, source_rate)
    if left is None:
        raise ValueError('no two sufficiently active left windows for replay analysis')
    return left


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('wav', type=Path)
    parser.add_argument('--ring-bytes', type=int, default=16384,
                        help='guest DMA ring size in bytes (default: 16384)')
    parser.add_argument('--source-rate', type=int, default=48000,
                        help='guest PCM frames per second (default: 48000)')
    parser.add_argument('--strict', action='store_true',
                        help='fail on suspected replay; use only with NONPERIODIC test fixtures')
    args = parser.parse_args()
    try:
        channels = analyze_channels(args.wav, ring_bytes=args.ring_bytes,
                                    source_rate=args.source_rate,
                                    include_correlation=True)
        suspected = False
        for name, result in zip(('left', 'right'), channels):
            if result is None:
                print(f'[AUDIO-DMA] {name}: INCONCLUSIVE (no active window pair)')
                continue
            fraction, start_ms, period_ms, correlation, corr_start_ms = result
            # Exact equality alone misses repeated PCM after QEMU resampling.
            # High correlation on a stationary periodic tone is inconclusive:
            # interpret this warning only on nonperiodic queue/sequence probes.
            replay = fraction >= 0.97 or correlation >= 0.995
            suspected = suspected or replay
            level = 'WARN suspected ring replay' if replay else 'INFO no ring replay detected'
            print(f'[AUDIO-DMA] {name}: {level}: identical active PCM={fraction:.2%} '
                  f'at {start_ms:.1f}ms and {start_ms + period_ms:.1f}ms; '
                  f'correlation={correlation:.4f} at {corr_start_ms:.1f}ms and '
                  f'{corr_start_ms + period_ms:.1f}ms '
                  f'(ring={args.ring_bytes} bytes/source={args.source_rate}Hz period={period_ms:.2f}ms); '
                  f'heuristic for nonperiodic fixtures, not proof of guest ISR delivery')
        # A missing active channel is not a clean stereo result. In strict
        # mode, do not silently pass a capture with one inconclusive channel.
        if args.strict and any(result is None for result in channels):
            print('[AUDIO-DMA] INCONCLUSIVE: at least one channel lacks active window pairs',
                  file=sys.stderr)
            return 2
        return 1 if args.strict and suspected else 0
    except (OSError, EOFError, ValueError, wave.Error) as exc:
        print(f'[AUDIO-DMA] INCONCLUSIVE {exc}', file=sys.stderr)
        return 2 if args.strict else 0


if __name__ == '__main__':
    sys.exit(main())
