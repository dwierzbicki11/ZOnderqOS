#!/usr/bin/env python3
"""Host-only A/B for append+metadata lookup versus same-handle length.

Models the I/O shape of ServiceManager; Python's append and host filesystem
are NOT proof of Cosmos FileStream or FAT32 performance.
Uses fstat on the open handle (rather than tell) to model Stream.Length,
which queries file size rather than the current write position.
"""
import argparse
import os
import statistics
import tempfile
import time

LIMIT = 64 * 1024
ENTRY = b"2026-11-06 00:00:00Z heartbeat\n"
ROTATED = b"service log rotated\n"


def run(path, count, mode):
    if mode not in ("stat", "stream") or count < 1:
        raise ValueError("invalid benchmark mode or iteration count")
    metadata_reads = rotations = 0
    start = time.perf_counter_ns()
    for _ in range(count):
        with open(path, "ab") as handle:
            handle.write(ENTRY)
            handle.flush()
            if mode == "stream":
                length = os.fstat(handle.fileno()).st_size
        if mode == "stat":
            length = os.path.getsize(path)
            metadata_reads += 1
        if length > LIMIT:
            # The append handle is closed before truncation in both variants.
            with open(path, "wb") as handle:
                handle.write(ROTATED)
            rotations += 1
    elapsed_ms = (time.perf_counter_ns() - start) / 1e6
    with open(path, "rb") as handle:
        data = handle.read()
    return elapsed_ms, metadata_reads, rotations, data


def benchmark(iterations, repeats):
    if iterations < 1 or repeats < 1:
        raise ValueError("iterations and repeats must be positive")
    samples = {"stat": [], "stream": []}
    with tempfile.TemporaryDirectory(prefix="zonderq-stream-log-") as root:
        for index in range(repeats):
            results = {}
            modes = ("stat", "stream") if index % 2 == 0 else ("stream", "stat")
            for mode in modes:
                path = os.path.join(root, mode + ".log")
                with open(path, "wb"):
                    pass
                results[mode] = run(path, iterations, mode)
                samples[mode].append(results[mode][0])
            if results["stat"][2:] != results["stream"][2:]:
                raise RuntimeError("rotation/content mismatch")
            if results["stat"][1] != iterations or results["stream"][1] != 0:
                raise RuntimeError("unexpected metadata lookup count")
            if len(results["stat"][3]) > LIMIT:
                raise RuntimeError("log exceeds rotation limit")
    return samples


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--iterations", type=int, default=3000)
    parser.add_argument("--repeats", type=int, default=5)
    args = parser.parse_args()
    try:
        samples = benchmark(args.iterations, args.repeats)
    except ValueError as exc:
        parser.error(str(exc))
    print("HOST-only Python I/O-shape comparison; NOT Cosmos/FAT32 speedup")
    print(f"iterations={args.iterations} repeats={args.repeats}")
    for mode in ("stat", "stream"):
        values = samples[mode]
        print(f"{mode}: median_ms={statistics.median(values):.3f} min_ms={min(values):.3f} max_ms={max(values):.3f}")
    print(f"median_ratio_stat_over_stream={statistics.median(samples['stat']) / statistics.median(samples['stream']):.3f}")


if __name__ == "__main__":
    main()
