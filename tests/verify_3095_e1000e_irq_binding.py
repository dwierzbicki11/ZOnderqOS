#!/usr/bin/env python3
"""Correlate stock Cosmos E1000E line binding with real guest serial evidence.

This establishes a connected legacy line and a published NIC, not that the
interrupt handler executed. QEMU-side trace emission is checked separately.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import re

from verify_3095_e1000e_irq_trace import count_notifications, count_legacy_cycles

MAC = r"([0-9A-Fa-f]{2}(?::[0-9A-Fa-f]{2}){5})"
NODE = r"(pci:[0-9a-fA-F]{4}:[0-9a-fA-F]{2}:[0-9a-fA-F]{2}\.[0-7])"
BOUND = re.compile(r"^\[Drivers\] " + NODE + r" offer E1000EDriver -> bound$")
DRIVER = re.compile(r"^\[Drivers\] " + NODE + r" E1000EDriver: mac " + MAC +
                    r", link up, line (\d{1,3}), polling every 10 ms$")
GUEST = re.compile(r"^\[NET3095-N1\] cpu=bsp adapter=0 name=e1000e mac=" + MAC +
                   r" ready=yes link=up$")
BASELINE = re.compile(r"^\\[NET3095-N6\\] DRIVER_BASELINE=PASS node=" + NODE +
                      r" resources=(\\d+) irq_sources=(\\d+) "
                      r"handler_faults=0 isr_executions=unknown$".replace("\\\\", "\\"))
HEALTH = re.compile(r"^\[NET3095-N6\] DRIVER_HEALTH=PASS node=" + NODE +
                    r" driver=E1000EDriver resources=(\d+) published=(\d+)" +
                    r" irq_sources=(\d+) handler_faults=(\d+) isr_executions=unknown$")


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



def check_driver_health(serial: str) -> str:
    """Validate the read-only DriverKit node snapshot after all six HTTP sessions.

    irq_sources is a count of PCI sources, not E1000E ISR executions.
    A zero handler-fault count only means no exception was observed.
    """
    lines = serial.replace("\r", "").splitlines()
    claims = [line for line in lines
              if line.startswith("[NET3095-N6] DRIVER_HEALTH")]
    if len(claims) != 1:
        raise ValueError("missing or duplicate E1000E DriverKit health marker")
    match = HEALTH.fullmatch(claims[0])
    if not match:
        raise ValueError("malformed E1000E DriverKit health marker")
    baseline_lines = [line for line in lines
                      if line.startswith("[NET3095-N6] DRIVER_BASELINE")]
    if len(baseline_lines) != 1:
        raise ValueError("missing or duplicate E1000E DriverKit baseline")
    baseline = BASELINE.fullmatch(baseline_lines[0])
    if baseline is None:
        raise ValueError("malformed E1000E DriverKit baseline")
    node, resources, published, sources, faults = match.groups()
    baseline_node, baseline_resources, baseline_sources = baseline.groups()
    if (baseline_node != node or baseline_resources != resources or
            baseline_sources != sources):
        raise ValueError("E1000E DriverKit resources/IRQ sources changed during N6 stress")
    bound = [m for line in lines if (m := BOUND.fullmatch(line))]
    if (node != "pci:0000:00:02.0" or len(bound) != 1 or
            bound[0].group(1) != node or int(resources) < 1 or
            int(published) != 1 or int(sources) < 1 or int(faults) != 0):
        raise ValueError("E1000E DriverKit state invalid or mismatched after stress")
    link = "[NET3095-N6] POST_STRESS_LINK=PASS mac="
    link_lines = [line for line in lines if line.startswith(link)]
    guest = [m for line in lines if (m := GUEST.fullmatch(line))]
    if (len(link_lines) != 1 or len(guest) != 1 or
            link_lines[0] != link + guest[0].group(1) or
            not lines.index(baseline_lines[0]) < lines.index(link_lines[0]) <
            lines.index(claims[0])):
        raise ValueError("E1000E health was not sampled after matching post-stress link")
    if any("[NET3095-N6][FAIL]" in line for line in lines):
        raise ValueError("guest reported N6 failure despite DriverKit marker")
    return f"driverkit-bound=yes irq-sources={sources} handler-faults=0 ISR-UNPROVEN"


def check_legacy_notification(serial: str, qemu_stderr: str) -> str:
    """Correlate an assigned guest legacy line with a QEMU legacy assertion.

    A generic MSI/MSI-X event cannot stand in for the guest's legacy line.
    This does not observe guest ISR execution: the driver also polls.
    """
    binding = check_guest_line(serial)
    health = check_driver_health(serial)
    legacy, msi, msix = count_notifications(qemu_stderr)
    cycles = count_legacy_cycles(qemu_stderr)
    if legacy < 4 or cycles < 4:
        raise ValueError("fewer than four QEMU E1000E legacy INTx low/high cycles")
    return (f"{binding} {health} qemu-legacy-assertions={legacy} "
            f"qemu-legacy-cycles={cycles} msi={msi} msix={msix}; "
            "guest handler execution UNPROVEN")


def self_test() -> None:
    serial = (
        "[Drivers] pci:0000:00:02.0 E1000EDriver: mac 52:54:00:12:34:56, link up, line 11, polling every 10 ms\n"
        "[Drivers] pci:0000:00:02.0 offer E1000EDriver -> bound\n"
        "[NET3095-N1] cpu=bsp adapter=0 name=e1000e mac=52:54:00:12:34:56 ready=yes link=up\n"
        "[NET3095-N6] DRIVER_BASELINE=PASS node=pci:0000:00:02.0 resources=8 irq_sources=1 handler_faults=0 isr_executions=unknown\n"
        "[NET3095-N6] POST_STRESS_LINK=PASS mac=52:54:00:12:34:56\n"
        "[NET3095-N6] DRIVER_HEALTH=PASS node=pci:0000:00:02.0 driver=E1000EDriver resources=8 published=1 irq_sources=1 handler_faults=0 isr_executions=unknown\n"
    )
    assert "line=11" in check_guest_line(serial)
    assert "ISR-UNPROVEN" in check_driver_health(serial)
    trace = ("e1000e_irq_legacy_notify IRQ line state: 0\n"
             "e1000e_irq_legacy_notify IRQ line state: 1\n") * 4
    assert "qemu-legacy-assertions=4" in check_legacy_notification(serial, trace)
    assert "qemu-legacy-cycles=4" in check_legacy_notification(serial, trace)
    for invalid_trace in ("", "e1000e_irq_legacy_notify IRQ line state: 0\n",
                          "e1000e_irq_legacy_notify IRQ line state: 1\n" * 5,
                          ("e1000e_irq_legacy_notify IRQ line state: 0\n"
                           "e1000e_irq_legacy_notify IRQ line state: 1\n") * 3,
                          "e1000e_irq_msi_notify MSI notify 0x80\n",
                          "e1000e_irq_msix_notify_vec MSI-X notify vector 0x0\n",
                          "qemu-system-x86_64: terminating on signal 15 from pid 12 (bash)\n" + trace):
        try:
            check_legacy_notification(serial, invalid_trace)
        except ValueError:
            pass
        else:
            raise AssertionError("accepted QEMU trace without a live legacy assertion")
    bad_health = (
        serial.replace("DRIVER_BASELINE=PASS", "DRIVER_BASELINE=FAIL"),
        serial.replace("[NET3095-N6] DRIVER_BASELINE=PASS", "[NET3095-N6] DRIVER_BASELINE=FAIL"),
        serial.replace("resources=8 irq_sources=1", "resources=9 irq_sources=1"),
        serial.replace("resources=8 irq_sources=1", "resources=8 irq_sources=2"),
        serial.replace("DRIVER_BASELINE=PASS node=pci:0000:00:02.0", "DRIVER_BASELINE=PASS node=pci:0000:00:03.0"),
        serial.replace("[NET3095-N6] DRIVER_BASELINE=PASS node=pci:0000:00:02.0 resources=8 irq_sources=1 handler_faults=0 isr_executions=unknown\\n", ""),
        serial + "[NET3095-N6] DRIVER_BASELINE=PASS node=pci:0000:00:02.0 resources=8 irq_sources=1 handler_faults=0 isr_executions=unknown\\n",
        serial.replace(
            "[NET3095-N6] DRIVER_BASELINE=PASS node=pci:0000:00:02.0 resources=8 irq_sources=1 handler_faults=0 isr_executions=unknown\\n",
            "").replace(
            "[NET3095-N6] POST_STRESS_LINK=PASS mac=52:54:00:12:34:56\\n",
            "[NET3095-N6] POST_STRESS_LINK=PASS mac=52:54:00:12:34:56\\n"
            "[NET3095-N6] DRIVER_BASELINE=PASS node=pci:0000:00:02.0 resources=8 irq_sources=1 handler_faults=0 isr_executions=unknown\\n"),
        serial.replace("handler_faults=0", "handler_faults=1"),
        serial.replace("resources=8", "resources=0"),
        serial.replace("published=1", "published=0"),
        serial.replace("irq_sources=1", "irq_sources=0"),
        serial.replace("POST_STRESS_LINK=PASS mac=52:54:00:12:34:56", "POST_STRESS_LINK=PASS mac=02:54:00:12:34:56"),
        serial.replace("isr_executions=unknown", "isr_executions=1"),
        serial.replace("node=pci:0000:00:02.0", "node=pci:0000:00:03.0"),
        serial.replace("pci:0000:00:02.0 offer", "pci:0000:00:03.0 offer"),
        serial.replace("[NET3095-N6] DRIVER_HEALTH=PASS", "[NET3095-N6] DRIVER_HEALTH=FAIL"),
        serial.replace("[NET3095-N6] POST_STRESS_LINK=PASS", "[NET3095-N6] POST_STRESS_LINK=FAIL"),
        serial.replace("[NET3095-N6] POST_STRESS_LINK=PASS mac=52:54:00:12:34:56\n", ""),
        serial.replace("[NET3095-N6] DRIVER_HEALTH=PASS", "[NET3095-N6][FAIL] bad\n[NET3095-N6] DRIVER_HEALTH=PASS"),
        serial + serial.splitlines()[-1] + "\n",
    )
    for bad in bad_health:
        try:
            check_driver_health(bad)
        except ValueError:
            continue
        raise AssertionError("accepted invalid DriverKit health: " + repr(bad))
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
                proof = (check_guest_line(serial) + " " +
                         check_driver_health(serial) + "; handler execution UNPROVEN")
            print("[NET3095-E1000E-IRQ-BINDING] " + proof)
        except (OSError, ValueError) as exc:
            parser.exit(1, f"[NET3095-E1000E-IRQ-BINDING][FAIL] {exc}\n")
