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


def checked_irq_lines(stderr: str):
    """Reject malformed IRQ notifications instead of silently ignoring them."""
    # Split only on LF: splitlines() treats embedded CR as a new line and
    # can turn a malformed IRQ event into a valid one. Strip only terminal
    # CR for legitimate CRLF records; preserve embedded CR for rejection.
    for raw in stderr.split("\n"):
        if raw.endswith("\r"):
            raw = raw[:-1]
        # Do not normalize whitespace around evidence: indented or padded
        # lines are not exact QEMU trace events and must not satisfy a gate.
        line = raw
        if re.match(r"^qemu-system-[^:]+: terminating on signal ", line):
            break
        # Reject disguised notifications, even with a corrupted prefix.\n        # Unrelated E1000E tracepoints are not IRQ delivery evidence.\n        if any(name in line for name in (\n            "e1000e_irq_legacy_notify", "e1000e_irq_msi_notify",\n            "e1000e_irq_msix_notify_vec")):
            if not any(pattern.fullmatch(line) for pattern in (LEGACY, MSI, MSIX)):
                raise ValueError("malformed QEMU E1000E IRQ notification")
        yield line


def count_notifications(stderr: str) -> tuple[int, int, int]:
    legacy = msi = msix = 0
    for line in checked_irq_lines(stderr):
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


def count_legacy_cycles(stderr: str) -> int:
    """Count distinct low-then-high INTx transitions in QEMU's trace.

    Repeated 'state: 1' notifications without any intervening deassertion
    must not be reported as independent interrupt cycles. This measures
    QEMU device-side line activity, NEVER guest ISR execution.
    """
    armed = False
    cycles = 0
    for line in checked_irq_lines(stderr):
        match = LEGACY.fullmatch(line)
        if match is None:
            continue
        if match.group(1) == "0":
            armed = True
        elif armed:
            cycles += 1
            armed = False
    return cycles


def report(stderr: str, require_notify: bool = False, require_legacy_cycle: bool = False) -> str:
    legacy, msi, msix = count_notifications(stderr)
    total = legacy + msi + msix
    legacy_cycles = count_legacy_cycles(stderr)
    # A zero-cause MSI is not an emitted interrupt, but its presence still
    # violates an explicitly legacy-only trace contract. Check raw events,
    # not only the nonzero notification counters.
    if require_legacy_cycle and any(
        MSI.fullmatch(line) or MSIX.fullmatch(line)
        for line in checked_irq_lines(stderr)
    ):
        raise ValueError("MSI/MSI-X notification in legacy INTx-only QEMU trace")
    if require_legacy_cycle and not legacy_cycles:
        raise ValueError("no complete QEMU E1000E legacy INTx low-to-high cycle")
    if require_notify and not total:
        raise ValueError("no QEMU E1000E IRQ notification trace event")
    status = "EMITTED" if total else "NOT_OBSERVED"
    return (f"[NET3095-E1000E-IRQ-TRACE] {status} legacy={legacy} "
            f"msi={msi} msix={msix} legacy_cycles={legacy_cycles}; "
            "QEMU-only, guest handler/IRQ delivery UNPROVEN")


def self_test() -> None:
    trace = ("1@123.456:e1000e_irq_legacy_notify IRQ line state: 0\n"
             "1@123.457:e1000e_irq_legacy_notify IRQ line state: 1\n"
             "1@123.458:e1000e_irq_msi_notify MSI notify 0x80\n"
             "e1000e_irq_msix_notify_vec MSI-X notify vector 0x0\n")
    assert count_notifications(trace) == (1, 1, 1)
    assert count_legacy_cycles(trace) == 1
    # A mixed-mode trace must never satisfy an INTx-only proof gate.
    try:
        report(trace, require_legacy_cycle=True)
    except ValueError as exc:
        assert "MSI/MSI-X" in str(exc)
    else:
        raise AssertionError("accepted MSI/MSI-X events as legacy-only IRQ trace")
    legacy_only = ("e1000e_irq_legacy_notify IRQ line state: 0\n"
                   "e1000e_irq_legacy_notify IRQ line state: 1\n")
    assert "legacy_cycles=1" in report(legacy_only, require_legacy_cycle=True)
    for mixed in ("e1000e_irq_msi_notify MSI notify 0x0\n",
                  "e1000e_irq_msi_notify MSI notify 0x80\n",
                  "e1000e_irq_msix_notify_vec MSI-X notify vector 0x0\n"):
        try:
            report(legacy_only + mixed, require_legacy_cycle=True)
        except ValueError as exc:
            assert "MSI/MSI-X" in str(exc)
        else:
            raise AssertionError("accepted mixed IRQ modes in INTx-only gate")
    for malformed in (
        "e1000e_irq_msi_notify MSI notify garbage\n",
        "e1000e_irq_msix_notify_vec MSI-X notify vector invalid\n",
        "e1000e_irq_legacy_notify IRQ line state: 2\n",
        "e1000e_irq_legacy_notifyEXTRA IRQ line state: 1\n",
        "e1000e_irq_msi_notify_extra MSI notify 0x80\n",
        "123@bad:e1000e_irq_legacy_notify IRQ line state: 1\n",
        "garbage:e1000e_irq_legacy_notify IRQ line state: 1\\n",
        "qemu: e1000e_irq_msi_notify MSI notify 0x80\\n",
        "e1000e_irq_legacy_notify IRQ line state: 1\\x00\\n",
        "123@456.789x:e1000e_irq_msi_notify MSI notify 0x80\n",
        "e1000e_irq_msix_notify_vec_extra MSI-X notify vector 0x0\n",
        " e1000e_irq_legacy_notify IRQ line state: 1\n",
        "e1000e_irq_legacy_notify IRQ line state: 1 \n",
        "e1000e_irq_legacy_notify IRQ line state: \r1\n",
        "e1000e_irq_legacy_notify IRQ line state: 1\rgarbage\n",
        "e1000e_irq_legacy_notify IRQ line state: 1\rgarbage\r\n",
        "e1000e_irq_legacy_notify IRQ line state: 0\r" +
        "e1000e_irq_legacy_notify IRQ line state: 1\n",
    ):
        try:
            report(legacy_only + malformed, require_legacy_cycle=True)
        except ValueError as exc:
            assert "malformed" in str(exc)
        else:
            raise AssertionError("accepted malformed IRQ event alongside valid INTx")
    # Windows CRLF traces remain supported; embedded CR cannot manufacture
    # a valid event from malformed bytes.
    assert count_notifications(legacy_only.replace("\n", "\r\n")) == (1, 0, 0)
    assert count_legacy_cycles(legacy_only.replace("\n", "\r\n")) == 1
    # A stuck asserted line may generate duplicate notifications, but it
    # cannot satisfy an opt-in complete legacy INTx transition gate.
    stuck = "e1000e_irq_legacy_notify IRQ line state: 1\n" * 8
    assert "legacy_cycles=0" in report(stuck, require_notify=True)
    try:
        report(stuck, require_legacy_cycle=True)
    except ValueError as exc:
        assert "no complete" in str(exc)
    else:
        raise AssertionError("accepted repeated INTx high notifications as cycles")
    assert count_legacy_cycles(
        "e1000e_irq_legacy_notify IRQ line state: 1\n" * 8) == 0
    assert count_legacy_cycles(
        ("e1000e_irq_legacy_notify IRQ line state: 0\n"
         "e1000e_irq_legacy_notify IRQ line state: 1\n") * 4) == 4
    assert "EMITTED legacy=1 msi=1 msix=1" in report(trace, True)
    bad = ("", "e1000e_irq_pending_interrupts ICR PENDING: 0x80\n",
           "e1000e_irq_legacy_notify IRQ line state: 0\n",
           "e1000e_irq_msi_notify MSI notify 0x0\n",
           "something e1000e_irq_msi_notify MSI notify 0x80\n",
           "e1000e_irq_msix_notify_vec MSI-X notify vector garbage\n",
           "e1000e_irq_msi_notify MSI notify 0x80 spoof\n")
    for sample in bad:
        if sample.startswith(("e1000e_irq_legacy_notify", "e1000e_irq_msi_notify", "e1000e_irq_msix_notify_vec")) and sample not in (
            "e1000e_irq_legacy_notify IRQ line state: 0\n",
            "e1000e_irq_msi_notify MSI notify 0x0\n",
        ):
            try:
                count_notifications(sample)
            except ValueError:
                continue
            raise AssertionError("accepted malformed IRQ notification")
        assert count_notifications(sample) == (0, 0, 0), repr(sample)
        assert "NOT_OBSERVED" in report(sample)
        try:
            report(sample, True)
        except ValueError:
            pass
        else:
            raise AssertionError("accepted missing/spoofed IRQ notification")
    assert count_notifications("e1000e_irq_legacy_notify IRQ line state: 1\n" * 2) == (2, 0, 0)
    termination = "qemu-system-x86_64: terminating on signal 15 from pid 9791 (bash)\n"
    before = "e1000e_irq_legacy_notify IRQ line state: 1\n"
    assert count_notifications(before + termination + before) == (1, 0, 0)
    assert count_notifications(termination + before) == (0, 0, 0)
    assert count_legacy_cycles(
        "e1000e_irq_legacy_notify IRQ line state: 0\n" +
        before + termination +
        "e1000e_irq_legacy_notify IRQ line state: 0\n" + before) == 1
    try:
        report(termination + before, True)
    except ValueError:
        pass
    else:
        raise AssertionError("counted IRQ after QEMU termination")
    print("[NET3095-E1000E-IRQ-CONTRACT] PASS synthetic trace parser tests only")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("stderr", nargs="?", type=Path)
    parser.add_argument("--require-notify", action="store_true")
    parser.add_argument("--require-legacy-cycle", action="store_true",
                        help="require QEMU-observed INTx deassert/assert cycle; not guest ISR proof")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
    else:
        if args.stderr is None:
            parser.error("real QEMU stderr trace required")
        try:
            print(report(args.stderr.read_text(errors="replace"),
                         args.require_notify, args.require_legacy_cycle))
        except (OSError, ValueError) as exc:
            parser.exit(1, f"[NET3095-E1000E-IRQ-TRACE][FAIL] {exc}\n")
