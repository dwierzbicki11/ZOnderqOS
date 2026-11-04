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

MARKER = re.compile(r"(?<![A-Za-z0-9_])PERF_BOOT_INIT_MS=([0-9]+)(?![A-Za-z0-9_.+-])")
FREE_PAGES = re.compile(r"(?<![A-Za-z0-9_])free_pages=([0-9]+)(?![A-Za-z0-9_.+-])")


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
    values = [int(value) for value in FREE_PAGES.findall(log)]
    if not values:
        raise ValueError("memory service did not report free_pages")
    return {
        "scope": "second_boot_memory_service_free_physical_pages",
        "samples": len(values),
        "first_free_pages": values[0],
        "last_free_pages": values[-1],
        "minimum_free_pages": min(values),
        "maximum_free_pages": max(values),
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
        result["memory"] = memory_samples(
            args.memory_log.read_text(encoding="utf-8", errors="replace"))
    report = json.dumps(result, sort_keys=True, indent=2)
    print("[PERF-QEMU] guest initialization baseline (NOT full boot or FPS):")
    print(report)
    if args.output is not None:
        args.output.write_text(report + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
