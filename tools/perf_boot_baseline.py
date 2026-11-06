#!/usr/bin/env python3
"""Report guest-side initialization durations from two real FAT32 QEMU boots.

The marker is written by Kernel.BeforeRun after services and login setup.
It measures guest Stopwatch elapsed time, not QEMU process start, desktop
frame readiness, or host wall time. This parser never opens a disk image.
"""
import argparse
import json
from pathlib import Path
import re

MARKER = re.compile(r"(?<![A-Za-z0-9_])PERF_BOOT_INIT_MS=([0-9]+)(?=[ \t\r\n]|$)")
FREE_PAGES = re.compile(r"(?<![A-Za-z0-9_])free_pages=([0-9]+)(?=[ \t\r\n]|$)")


def samples(log: str) -> list[int]:
    return [int(value) for value in MARKER.findall(log)]


def compare(first: str, after: str) -> dict:
    before_samples = samples(first)
    after_samples = samples(after)
    if len(before_samples) != 1:
        raise ValueError("expected exactly one first-boot initialization marker")
    if len(after_samples) != len(before_samples) + 1:
        raise ValueError("second log must preserve first marker and append one")
    if after_samples[0] != before_samples[0]:
        raise ValueError("first-boot marker changed across FAT32 persistence")
    first_ms, second_ms = after_samples
    if first_ms <= 0 or second_ms <= 0:
        raise ValueError("nonpositive guest initialization duration")
    return {
        "clock": "guest_Stopwatch",
        "scope": "BeforeRun_to_login_preparation; excludes firmware and GUI",
        "first_boot_ms": first_ms,
        "second_boot_ms": second_ms,
        "second_minus_first_ms": second_ms - first_ms,
    }


def memory_samples(log: str) -> dict:
    """Actual guest physical allocator free-page counts; not GC heap/RSS."""
    return memory_samples_from_lines(log.splitlines(keepends=True))


def memory_samples_from_lines(lines) -> dict:
    """Parse memory log incrementally, without retaining the whole file in RAM."""
    def values():
        for line in lines:
            for match in FREE_PAGES.finditer(line):
                yield int(match.group(1))

    matches = values()
    first_match = next(matches, None)
    if first_match is None:
        raise ValueError("memory service did not report free_pages")

    first = previous = minimum = maximum = first_match
    count = 1
    decreasing = increasing = unchanged = 0
    total_decrease = total_increase = largest_decrease = 0
    for current in matches:
        count += 1
        minimum = min(minimum, current)
        maximum = max(maximum, current)
        change = current - previous
        if change < 0:
            decreasing += 1
            total_decrease -= change
            largest_decrease = max(largest_decrease, -change)
        elif change > 0:
            increasing += 1
            total_increase += change
        else:
            unchanged += 1
        previous = current

    return {
        "scope": "second_boot_memory_service_free_physical_pages",
        "samples": count,
        "first_free_pages": first,
        "last_free_pages": previous,
        "minimum_free_pages": minimum,
        "maximum_free_pages": maximum,
        "last_minus_first_free_pages": previous - first,
        "decreasing_intervals": decreasing,
        "increasing_intervals": increasing,
        "unchanged_intervals": unchanged,
        "total_decrease_pages": total_decrease,
        "total_increase_pages": total_increase,
        "largest_interval_decrease_pages": largest_decrease,
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("first_log", type=Path)
    parser.add_argument("second_log", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--memory-log", type=Path)
    args = parser.parse_args()
    result = compare(
        args.first_log.read_text(encoding="utf-8", errors="replace"),
        args.second_log.read_text(encoding="utf-8", errors="replace"),
    )
    if args.memory_log is not None:
        with args.memory_log.open("r", encoding="utf-8", errors="replace") as memory_file:
            result["memory"] = memory_samples_from_lines(memory_file)
    report = json.dumps(result, sort_keys=True, indent=2)
    print("[PERF-QEMU] guest initialization baseline (NOT full boot or FPS):")
    print(report)
    if args.output is not None:
        args.output.write_text(report + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
