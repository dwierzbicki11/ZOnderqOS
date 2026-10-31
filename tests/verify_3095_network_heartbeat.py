#!/usr/bin/env python3
"""Fail-closed managed-guest heartbeat continuity gate for isolated QEMU N1-N6.

A running QEMU process, a count of repeated markers, or a synthetic self-test
is not evidence of live guest progress. The real gate consumes UART output
after the network PCAP proof and after a separate eight-second wait.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import re

MARKER = "[NET3095-N1] HEARTBEAT="
VALUE = re.compile(r"\[NET3095-N1\] HEARTBEAT=([0-9]{1,12})$")


def parse(serial: str) -> list[int]:
    values: list[int] = []
    for line in serial.replace("\r", "").splitlines():
        if MARKER not in line:
            continue
        if line.count(MARKER) != 1:
            raise ValueError("duplicated guest heartbeat marker on one line")
        match = VALUE.search(line.strip())
        if match is None:
            raise ValueError("malformed guest heartbeat marker")
        value = int(match.group(1))
        if str(value) != match.group(1):
            raise ValueError("noncanonical heartbeat number")
        if value != len(values) + 1:
            raise ValueError("repeated, skipped or reset guest heartbeat")
        values.append(value)
    return values


def validate(serial: str, before_count: int, min_new: int = 6) -> str:
    if before_count < 0 or min_new < 1:
        raise ValueError("invalid heartbeat baseline or minimum")
    values = parse(serial)
    if before_count > len(values):
        raise ValueError("heartbeat log shrank during stability window")
    advanced = len(values) - before_count
    if advanced < min_new:
        raise ValueError(f"only {advanced} new guest heartbeats; need {min_new}")
    return f"[NET3095-N1-HEARTBEAT] PASS {advanced} advancing managed ticks after proof"


def self_test() -> None:
    def log(numbers: list[int]) -> str:
        return "\n".join(MARKER + str(n) for n in numbers) + "\n"

    assert parse("") == []
    assert parse(log([1, 2, 3])) == [1, 2, 3]
    assert "PASS 6" in validate(log(list(range(1, 9))), 2)
    assert "PASS 6" in validate(log(list(range(1, 7))), 0)
    bad = [
        (log([1, 2, 3, 4, 5, 6, 7]), 2),  # only five new
        (log([1, 2, 2, 3, 4, 5, 6, 7, 8]), 2),  # repeated
        (log([1, 2, 4, 5, 6, 7, 8, 9]), 2),  # skipped
        (log([1, 2, 1, 2, 3, 4, 5, 6]), 2),  # reset
        (log([1, 2, 3]), 4),  # truncated log
        (log([0, 1, 2, 3, 4, 5, 6]), 0),  # invalid initial tick
        (log([1, 2, 3, 4, 5, 6, 7, 8]) + MARKER + "junk\n", 2),
        (log([1, 2, 3, 4, 5, 6, 7, 8]) +
         MARKER + "9 " + MARKER + "9\n", 2),
        (log([1, 2, 3, 4, 5, 6, 7, 8]) + MARKER + "08\n", 2),
        (log([1, 2, 3, 4, 5, 6, 7, 8]), -1),
    ]
    for serial, baseline in bad:
        try:
            validate(serial, baseline)
        except ValueError:
            continue
        raise AssertionError("accepted invalid guest heartbeat continuity")
    print("[NET3095-N1-HEARTBEAT-CONTRACT] PASS synthetic monotonicity and negative cases only")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("serial_log", nargs="?", type=Path)
    parser.add_argument("--count", action="store_true",
                        help="print validated baseline heartbeat count")
    parser.add_argument("--after-count", type=int, default=None,
                        help="require six advancing ticks since this baseline")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
    else:
        if args.serial_log is None:
            parser.error("real QEMU serial log required")
        if args.count == (args.after_count is not None):
            parser.error("choose exactly one of --count or --after-count")
        try:
            serial = args.serial_log.read_text(errors="replace")
            if args.count:
                print(len(parse(serial)))
            else:
                print(validate(serial, args.after_count))
        except (OSError, ValueError) as exc:
            parser.exit(1, f"[NET3095-N1-HEARTBEAT][FAIL] {exc}\n")
