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
    for raw in serial.split("\n"):
        line = raw.removesuffix("\r")
        # Counters from different guest boots cannot establish an ISR delta.
        # A second boot after the baseline invalidates the measurement.
        if "[NET3095-N1] BOOT" in line and "before" in samples:
            raise ValueError("guest reboot between ISR samples")
        if re.search(r"\[NET3095-[A-Z0-9-]+\]\[FAIL\]", line) and "before" in samples:
            raise ValueError("guest network failure during ISR measurement")
        if "[NET3095-ISR]" not in line:
            continue
        match = MARKER.fullmatch(line)
        if match is None:
            raise ValueError("malformed guest ISR sample")
        phase, irq, rx, polling = match.groups()
        if phase in samples:
            raise ValueError("duplicate guest ISR sample")
        samples[phase] = (int(irq), int(rx), polling)
        phases.append(phase)
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
    good = ("[NET3095-ISR] phase=before interrupts=3 rx_frames=7 polling=no\n"
            "[NET3095-ISR] phase=after interrupts=5 rx_frames=9 polling=no\n")
    assert "VERIFIED_GUEST_COUNTER_DELTA" in validate(good)
    bad = (
        "",
        good.splitlines()[0],
        good + good.splitlines()[1] + "\n",
        good.replace("interrupts=5", "interrupts=3"),
        good.replace("rx_frames=9", "rx_frames=7"),
        good.replace("polling=no", "polling=yes", 1),
        good.replace("phase=after", "phase=after extra=1"),
        good.replace("phase=before", "phase=after"),
        "\n".join(reversed(good.splitlines())) + "\n",
        good.replace("interrupts=5", "interrupts=-1"),
        good.replace("phase=after", "phase=after\rspoof"),
        good.splitlines()[0] + "\n[NET3095-N1] BOOT\n" + good.splitlines()[1],
        good.splitlines()[0] + "\n[NET3095-N1][FAIL] reset\n" + good.splitlines()[1],
        good.splitlines()[0] + "\n[NET3095-N2][FAIL] DHCP timeout\n" + good.splitlines()[1],
        good.splitlines()[0] + "\n[NET3095-N6][FAIL] TCP reset\n" + good.splitlines()[1],
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
