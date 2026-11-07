#!/usr/bin/env python3
"""Fail-closed guest-side E1000E ISR/RX counter delta validator.

This is NOT a QEMU IRQ trace validator. Only counters sampled by the guest
from the real driver handler can establish guest ISR execution. No production
networking is enabled by this tool.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import re

MARKER = re.compile(
    r"^\[NET3095-ISR\] phase=(before|after) "
    r"interrupts=([0-9]+) rx_frames=([0-9]+) polling=(yes|no)$"
)


def validate(serial: str) -> str:
    samples = {}
    phases = []
    boot_count = 0
    for raw in serial.split("\n"):
        line = raw.removesuffix("\r")
        # Require a single boot in the complete evidence stream. Reject faults
        # even before the baseline or after the final sample.
        if line == "[NET3095-N1] BOOT":
            boot_count += 1
            if boot_count != 1 or samples:
                raise ValueError("multiple or late guest boot markers")
        if re.search(r"\[NET3095-[A-Z0-9-]+\]\[FAIL\]", line):
            raise ValueError("guest network failure in ISR evidence log")
        if "[NET3095-ISR]" not in line:
            continue
        match = MARKER.fullmatch(line)
        if match is None:
            raise ValueError("malformed guest ISR sample")
        if boot_count != 1:
            raise ValueError("ISR sample before guest boot")
        phase, irq, rx, polling = match.groups()
        if phase in samples:
            raise ValueError("duplicate guest ISR sample")
        samples[phase] = (int(irq), int(rx), polling)
        phases.append(phase)
    if boot_count != 1:
        raise ValueError("missing unique guest boot marker")
    if set(samples) != {"before", "after"}:
        raise ValueError("missing guest ISR before/after samples")
    if phases != ["before", "after"]:
        raise ValueError("guest ISR samples out of order")
    before, after = samples["before"], samples["after"]
    if before[2] != "no" or after[2] != "no":
        raise ValueError("polling enabled: IRQ RX not proven")
    if after[0] <= before[0]:
        raise ValueError("guest ISR count did not increase")
    if after[1] <= before[1]:
        raise ValueError("guest RX frame count did not increase")
    return (f"[NET3095-ISR] VERIFIED_GUEST_COUNTER_DELTA "
            f"interrupts={after[0] - before[0]} "
            f"rx_frames={after[1] - before[1]} polling=no "
            "(requires independently verified guest instrumentation)")


def self_test() -> None:
    good = ("[NET3095-N1] BOOT\n"
            "[NET3095-ISR] phase=before interrupts=3 rx_frames=7 polling=no\n"
            "[NET3095-ISR] phase=after interrupts=5 rx_frames=9 polling=no\n")
    assert "VERIFIED_GUEST_COUNTER_DELTA" in validate(good)
    bad = (
        "",
        good.splitlines()[1],
        good + good.splitlines()[2] + "\n",
        good.replace("interrupts=5", "interrupts=3"),
        good.replace("rx_frames=9", "rx_frames=7"),
        good.replace("polling=no", "polling=yes", 1),
        good.replace("phase=after", "phase=after extra=1"),
        good.replace("phase=before", "phase=after"),
        "\n".join(reversed(good.splitlines())) + "\n",
        good.replace("interrupts=5", "interrupts=-1"),
        good.replace("phase=after", "phase=after\rspoof"),
        good.replace("[NET3095-N1] BOOT\n", ""),
        good + "[NET3095-N1] BOOT\n",
        good.splitlines()[0] + "\n[NET3095-N1] BOOT\n" + "\n".join(good.splitlines()[1:]) + "\n",
        good.replace("[NET3095-ISR] phase=before", "[NET3095-N1][FAIL] reset\n[NET3095-ISR] phase=before"),
        good + "[NET3095-N2][FAIL] DHCP timeout\n",
        good.replace("[NET3095-N1] BOOT\n", "[NET3095-N6][FAIL] TCP reset\n[NET3095-N1] BOOT\n"),
    )
    for item in bad:
        try:
            validate(item)
        except ValueError:
            continue
        raise AssertionError(f"accepted invalid ISR evidence: {item!r}")
    print("[NET3095-ISR-CONTRACT] PASS synthetic parser tests only")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("serial", nargs="?", type=Path)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
    elif args.serial is None:
        parser.error("real guest serial log required")
    else:
        try:
            print(validate(args.serial.read_text(errors="replace")))
        except (OSError, ValueError) as exc:
            parser.exit(1, f"[NET3095-ISR][FAIL] {exc}\n")
