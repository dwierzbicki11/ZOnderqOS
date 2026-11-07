#!/usr/bin/env python3
"""Host-only A/B for ServiceManager append+stat overhead; NOT Cosmos/FAT32."""
import argparse
import os
import statistics
import tempfile
import time

LIMIT = 64 * 1024
ROTATED = b"service log rotated\n"
ENTRY = b"2026-11-06 00:00:00Z heartbeat\n"

def run(path, count, mode):
    if mode not in ("stat", "tracked"):
        raise ValueError("mode must be stat or tracked")
    if count < 1:
        raise ValueError("count must be positive")
    stats = rotations = 0
    started = time.perf_counter_ns()
    # Include the initial read needed to resume existing logs in timed I/O.
    tracked = 0
    if mode == "tracked":
        tracked = os.path.getsize(path)
        stats += 1
    for _ in range(count):
        with open(path, "ab") as file:
            file.write(ENTRY)
        if mode == "stat":
            length = os.path.getsize(path)
            stats += 1
        else:
            tracked += len(ENTRY)
            length = tracked
        if length > LIMIT:
            with open(path, "wb") as file:
                file.write(ROTATED)
            tracked = len(ROTATED)
            rotations += 1
    elapsed = time.perf_counter_ns() - started
    with open(path, "rb") as file:
        contents = file.read()
    return elapsed, stats, rotations, contents

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--iterations", type=int, default=10000)
    parser.add_argument("--repeats", type=int, default=5)
    args = parser.parse_args()
    if args.iterations < 1 or args.repeats < 1:
        parser.error("iterations and repeats must be positive")
    samples = {"stat": [], "tracked": []}
    with tempfile.TemporaryDirectory(prefix="zonderq-service-io-") as root:
        for repetition in range(args.repeats):
            results = {}
            order = ("stat", "tracked") if repetition % 2 == 0 else ("tracked", "stat")
            for mode in order:
                path = os.path.join(root, mode + ".log")
                with open(path, "wb"):
                    pass
                results[mode] = run(path, args.iterations, mode)
                samples[mode].append(results[mode][0] / 1e6)
            a, b = results["stat"], results["tracked"]
            if a[2] != b[2] or a[3] != b[3]:
                raise RuntimeError("rotation/content mismatch")
            if a[1] != args.iterations or b[1] != 1:
                raise RuntimeError("unexpected stat-call count")
            if len(a[3]) > LIMIT:
                raise RuntimeError("log exceeds size limit")
    print("HOST filesystem benchmark; NOT Cosmos/FAT32 or production speedup")
    print(f"iterations={args.iterations} repeats={args.repeats} entry_bytes={len(ENTRY)}")
    for mode in ("stat", "tracked"):
        values = samples[mode]
        print(f"{mode}: median_ms={statistics.median(values):.3f} min_ms={min(values):.3f} max_ms={max(values):.3f} stat_calls={results[mode][1]} rotations={results[mode][2]}")
    print(f"median_ratio_stat_over_tracked={statistics.median(samples['stat'])/statistics.median(samples['tracked']):.3f}")

if __name__ == "__main__":
    main()
