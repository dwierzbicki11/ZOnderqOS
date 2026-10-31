#!/usr/bin/env python3
"""Correlate stock Cosmos E1000E line binding with real guest serial evidence.

This establishes a connected legacy line and a published NIC, not that the
interrupt handler executed. QEMU-side trace emission is checked separately.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import re

from verify_3095_e1000e_irq_trace import count_notifications

MAC = r"([0-9A-Fa-f]{2}(?::[0-9A-Fa-f]{2}){5})"
NODE = r"(pci:[0-9a-fA-F]{4}:[0-9a-fA-F]{2}:[0-9a-fA-F]{2}\.[0-7])"
BOUND = re.compile(r"^\[Drivers\] " + NODE + r" offer E1000EDriver -> bound$")
DRIVER = re.compile(r"^\[Drivers\] " + NODE + r" E1000EDriver: mac " + MAC +
                    r", link up, line (\d{1,3}), polling every 10 ms$")
GUEST = re.compile(r"^\[NET3095-N1\] cpu=bsp adapter=0 name=e1000e mac=" + MAC +
                   r" ready=yes link=up$")


def check_guest_line(serial: str) -> str:
    """Reject missing/ambiguous driver binding, line or MAC correlation."""
    lines = serial.replace("\r", "").splitlines()

    def matches(pattern: re.Pattern[str]) -> list[re.Match[str]]:
        return [m for line in lines if (m := pattern.fullmatch(line))]

    bound, driver, guest = matches(BOUND), matches(DRIVER), matches(GUEST)
    if len(bound) != 1 or len(driver) != 1 or len(guest) != 1:
        raise ValueError("expected exactly one bound E1000E, line and ready guest adapter")
    if any((" E1000EDriver: mac " in line and not DRIVER.fullmatch(line)) or
           (" offer E1000EDriver -> " in line and not BOUND.fullmatch(line))
           for line in lines):
        raise ValueError("malformed additional E1000E driver/offer claim")
    if not (lines.index(driver[0].group(0)) < lines.index(bound[0].group(0)) <
            lines.index(guest[0].group(0))):
        raise ValueError("E1000E driver, binding and guest readiness out of order")
    if bound[0].group(1) != driver[0].group(1):
        raise ValueError("driver bound node differs from line diagnostic node")
    if driver[0].group(2).upper() != guest[0].group(1).upper():
        raise ValueError("driver and guest MAC differ")
    irq_line = int(driver[0].group(3))
    if not 1 <= irq_line <= 254:
        raise ValueError("invalid PCI legacy IRQ line")
    return f"guest-bound=yes line={irq_line} mac={driver[0].group(2).upper()}"



def check_legacy_notification(serial: str, qemu_stderr: str) -> str:
    """Correlate an assigned guest legacy line with a QEMU legacy assertion.

    A generic MSI/MSI-X event cannot stand in for the guest's legacy line.
    This does not observe guest ISR execution: the driver also polls.
    """
    binding = check_guest_line(serial)
    legacy, msi, msix = count_notifications(qemu_stderr)
    if legacy < 1:
        raise ValueError("no QEMU E1000E legacy IRQ assertion for guest legacy line")
    return (f"{binding} qemu-legacy-assertions={legacy} "
            f"msi={msi} msix={msix}; guest handler execution UNPROVEN")


def self_test() -> None:
    serial = (
        "[Drivers] pci:0000:00:02.0 E1000EDriver: mac 52:54:00:12:34:56, link up, line 11, polling every 10 ms\n"
        "[Drivers] pci:0000:00:02.0 offer E1000EDriver -> bound\n"
        "[NET3095-N1] cpu=bsp adapter=0 name=e1000e mac=52:54:00:12:34:56 ready=yes link=up\n"
    )
    assert "line=11" in check_guest_line(serial)
    trace = "e1000e_irq_legacy_notify IRQ line state: 1\n"
    assert "qemu-legacy-assertions=1" in check_legacy_notification(serial, trace)
    for invalid_trace in ("", "e1000e_irq_legacy_notify IRQ line state: 0\n",
                          "e1000e_irq_msi_notify MSI notify 0x80\n",
                          "e1000e_irq_msix_notify_vec MSI-X notify vector 0x0\n",
                          "qemu-system-x86_64: terminating on signal 15 from pid 12 (bash)\n" + trace):
        try:
            check_legacy_notification(serial, invalid_trace)
        except ValueError:
            pass
        else:
            raise AssertionError("accepted QEMU trace without a live legacy assertion")
    negatives = (
        serial.replace("line 11", "no line"),
        serial.replace("line 11", "line 255"),
        serial.replace("line 11", "line 0"),
        serial.replace("link up", "link down"),
        serial.replace("-> bound", "-> declined"),
        serial.replace("pci:0000:00:02.0 offer", "pci:0000:00:03.0 offer"),
        serial.replace("adapter=0", "adapter=1"),
        serial.replace("mac=52:54:00:12:34:56", "mac=02:54:00:12:34:56"),
        serial.replace("mac 52:54:00:12:34:56", "mac 02:54:00:12:34:56"),
        serial.replace("polling every 10 ms", "no polling"),
        serial + "[Drivers] pci:0000:00:02.0 E1000EDriver: mac malformed\\n",
        serial + "[Drivers] pci:0000:00:02.0 offer E1000EDriver -> declined\\n",
        "".join(reversed(serial.splitlines(keepends=True))),
        serial + serial,
        "",
    )
    for bad in negatives:
        try:
            check_guest_line(bad)
        except ValueError:
            continue
        raise AssertionError("accepted malformed guest binding: " + repr(bad))
    print("[NET3095-E1000E-IRQ-BINDING-CONTRACT] PASS synthetic serial cases only")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("serial", nargs="?", type=Path)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--trace-stderr", type=Path,
                        help="real QEMU stderr: require legacy assertion, not just MSI")
    args = parser.parse_args()
    if args.self_test:
        self_test()
    else:
        if args.serial is None:
            parser.error("real QEMU guest serial log required")
        try:
            serial = args.serial.read_text(errors="replace")
            if args.trace_stderr is not None:
                proof = check_legacy_notification(
                    serial, args.trace_stderr.read_text(errors="replace"))
            else:
                proof = check_guest_line(serial) + "; handler execution UNPROVEN"
            print("[NET3095-E1000E-IRQ-BINDING] " + proof)
        except (OSError, ValueError) as exc:
            parser.exit(1, f"[NET3095-E1000E-IRQ-BINDING][FAIL] {exc}\n")
