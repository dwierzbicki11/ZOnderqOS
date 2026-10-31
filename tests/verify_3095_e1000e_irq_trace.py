#!/usr/bin/env python3
"""Classify QEMU E1000E IRQ notifications; never claim guest IRQ delivery.

Only QEMU trace events from the real emulator stderr count. An IRQ asserted
by QEMU is not evidence that the guest installed/ran an interrupt handler;
stock Cosmos can receive packets through periodic polling instead.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import re

# QEMU's log backend writes e.g. "123@456.789:e1000e_irq_msi_notify MSI notify 0x80".
PREFIX = r"(?:[0-9]+@[0-9]+(?:\.[0-9]+)?:)?"
LEGACY = re.compile(r"^" + PREFIX + r"e1000e_irq_legacy_notify IRQ line state: ([01])$")
MSI = re.compile(r"^" + PREFIX + r"e1000e_irq_msi_notify MSI notify 0x([0-9a-fA-F]+)$")
MSIX = re.compile(r"^" + PREFIX + r"e1000e_irq_msix_notify_vec MSI-X notify vector 0x([0-9a-fA-F]+)$")


def count_notifications(stderr: str) -> tuple[int, int, int]:
    legacy = msi = msix = 0
    for line in stderr.replace("\r", "").splitlines():
        line = line.strip()
        match = LEGACY.fullmatch(line)
        if match is not None:
            legacy += match.group(1) == "1"
            continue
        match = MSI.fullmatch(line)
        if match is not None:
            # Zero causes must not be counted as an emitted IRQ.
            msi += int(match.group(1), 16) != 0
            continue
        match = MSIX.fullmatch(line)
        if match is not None:
            msix += 1
    return legacy, msi, msix


def report(stderr: str, require_notify: bool = False) -> str:
    legacy, msi, msix = count_notifications(stderr)
    total = legacy + msi + msix
    if require_notify and not total:
        raise ValueError("no QEMU E1000E IRQ notification trace event")
    status = "EMITTED" if total else "NOT_OBSERVED"
    return (f"[NET3095-E1000E-IRQ-TRACE] {status} legacy={legacy} "
            f"msi={msi} msix={msix}; QEMU-only, guest handler/IRQ delivery UNPROVEN")


def self_test() -> None:
    trace = ("1@123.456:e1000e_irq_legacy_notify IRQ line state: 0\n"
             "1@123.457:e1000e_irq_legacy_notify IRQ line state: 1\n"
             "1@123.458:e1000e_irq_msi_notify MSI notify 0x80\n"
             "e1000e_irq_msix_notify_vec MSI-X notify vector 0x0\n")
    assert count_notifications(trace) == (1, 1, 1)
    assert "EMITTED legacy=1 msi=1 msix=1" in report(trace, True)
    bad = ("", "e1000e_irq_pending_interrupts ICR PENDING: 0x80\n",
           "e1000e_irq_legacy_notify IRQ line state: 0\n",
           "e1000e_irq_msi_notify MSI notify 0x0\n",
           "something e1000e_irq_msi_notify MSI notify 0x80\n",
           "e1000e_irq_msix_notify_vec MSI-X notify vector garbage\n",
           "e1000e_irq_msi_notify MSI notify 0x80 spoof\n")
    for sample in bad:
        assert count_notifications(sample) == (0, 0, 0), repr(sample)
        assert "NOT_OBSERVED" in report(sample)
        try:
            report(sample, True)
        except ValueError:
            pass
        else:
            raise AssertionError("accepted missing/spoofed IRQ notification")
    assert count_notifications("e1000e_irq_legacy_notify IRQ line state: 1\n" * 2) == (2, 0, 0)
    print("[NET3095-E1000E-IRQ-CONTRACT] PASS synthetic trace parser tests only")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("stderr", nargs="?", type=Path)
    parser.add_argument("--require-notify", action="store_true")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
    else:
        if args.stderr is None:
            parser.error("real QEMU stderr trace required")
        try:
            print(report(args.stderr.read_text(errors="replace"), args.require_notify))
        except (OSError, ValueError) as exc:
            parser.exit(1, f"[NET3095-E1000E-IRQ-TRACE][FAIL] {exc}\n")
