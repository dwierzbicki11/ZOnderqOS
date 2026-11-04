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
from verify_3095_network_n1 import normalize_n1_serial, PCI_IRQ_ROUTE, PCI_IRQ_STABLE

MAC = r"([0-9A-Fa-f]{2}(?::[0-9A-Fa-f]{2}){5})"
NODE = r"(pci:[0-9a-fA-F]{4}:[0-9a-fA-F]{2}:[0-9a-fA-F]{2}\.[0-7])"
BOUND = re.compile(r"^\[Drivers\] " + NODE + r" offer E1000EDriver -> bound$")
DRIVER = re.compile(r"^\[Drivers\] " + NODE + r" E1000EDriver: mac " + MAC +
                    r", link up, line (\d{1,3}), polling every 10 ms$")
GUEST = re.compile(r"^\[NET3095-N1\] cpu=bsp adapter=0 name=e1000e mac=" + MAC +
                   r" ready=yes link=up$")
BASELINE = re.compile(r"^\[NET3095-N6\] DRIVER_BASELINE=PASS node=" + NODE +
                      r" resources=(\d+) irq_sources=(\d+) "
                      r"handler_faults=0 isr_executions=unknown$")
HEALTH = re.compile(r"^\[NET3095-N6\] DRIVER_HEALTH=PASS node=" + NODE +
                    r" driver=E1000EDriver resources=(\d+) published=(\d+)" +
                    r" irq_sources=(\d+) handler_faults=(\d+) isr_executions=unknown$")
PCI_STREAM = re.compile(r"^\[NET3095-N6\] HTTP_PCI_STABLE=PASS attempt=([1-6]) "
                        r"command=0x([0-9A-Fa-f]{4}) bar0=0x([0-9A-Fa-f]+) "
                        r"irq_line=([0-9]+) irq_pin=([0-9]+) "
                        r"\(read-only; ISR unproven\)$")
STREAM = re.compile(r"^\[NET3095-N6\] HTTP_DRIVER_STABLE=PASS attempt=([1-6]) "
                    r"resources=(\d+) irq_sources=(\d+)$")
BURST = re.compile(r"^\[NET3095-N6\] TX_DRIVER_STABLE=PASS count=128 checks=5 "
                   r"resources=(\d+) irq_sources=(\d+)$")
ICMP_CHECK = re.compile(r"^\[NET3095-N6\] ICMP_DRIVER_CHECK=PASS "
                        r"phase=(post_before|post_after|final_before|final_after) "
                        r"seq=(\d+) resources=(\d+) irq_sources=(\d+)$")
TX_CHECK = re.compile(r"^\[NET3095-N6\] TX_DRIVER_CHECK=PASS sent=(\d+) "
                      r"resources=(\d+) irq_sources=(\d+)$")
TX_NIC_CHECK = re.compile(r"^\[NET3095-N6\] TX_NIC_CHECK=PASS sent=(\d+)$")


def check_guest_line(serial: str) -> str:
    """Reject missing/ambiguous driver binding, line or MAC correlation."""
    lines = normalize_n1_serial(serial).replace("\r", "").splitlines()

    def matches(pattern: re.Pattern[str]) -> list[re.Match[str]]:
        return [m for line in lines if (m := pattern.fullmatch(line))]

    bound, driver, guest = matches(BOUND), matches(DRIVER), matches(GUEST)
    # A malformed or conflicting readiness claim must not be hidden by a
    # later valid line. This is guest evidence, not an ISR execution proof.
    guest_claims = [line for line in lines
                    if line.startswith("[NET3095-N1] cpu=") and
                    " adapter=" in line]
    if len(guest_claims) != 1 or not GUEST.fullmatch(guest_claims[0]):
        raise ValueError("malformed or duplicate N1 guest adapter readiness")
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
    # Cross-check PCI INTx metadata against the stock driver's own line.
    # This remains a routing observation, NOT a guest ISR execution count.
    # A malformed route claim must not disappear from the evidence merely
    # because its text no longer matches the strict PASS expression.
    route_claims = [line for line in lines
                    if line.startswith("[NET3095-N1] PCI_IRQ_ROUTE=")]
    stable_claims = [line for line in lines
                     if line.startswith("[NET3095-N1] PCI_IRQ_ROUTE_STABLE=")]
    routes, stable_routes = matches(PCI_IRQ_ROUTE), matches(PCI_IRQ_STABLE)
    if len(route_claims) != len(routes) or len(stable_claims) != len(stable_routes):
        raise ValueError("malformed PCI legacy IRQ route claim")
    if routes or stable_routes:
        if len(routes) != 1 or len(stable_routes) != 1:
            raise ValueError("missing or duplicated PCI legacy IRQ route marker")
        route_line, route_pin = map(int, routes[0].groups())
        stable_line, stable_pin = map(int, stable_routes[0].groups())
        if (route_line != irq_line or not 1 <= route_pin <= 4 or
                (route_line, route_pin) != (stable_line, stable_pin) or
                not (lines.index(guest[0].group(0)) <
                     lines.index(routes[0].group(0)) <
                     lines.index(stable_routes[0].group(0)))):
            raise ValueError("PCI legacy IRQ route differs from bound E1000E driver")
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
    driver_lines = [m for line in lines if (m := DRIVER.fullmatch(line))]
    if len(driver_lines) != 1 or driver_lines[0].group(1) != node:
        raise ValueError("missing or mismatched E1000E driver IRQ line")
    # Do not let malformed or conflicting driver diagnostics disappear simply
    # because they fail the strict regex. A later valid line must not mask one.
    driver_claims = [line for line in lines if " E1000EDriver: mac " in line]
    bound_claims = [line for line in lines if " offer E1000EDriver -> " in line]
    if (len(driver_claims) != 1 or len(bound_claims) != 1 or
            not DRIVER.fullmatch(driver_claims[0]) or
            not BOUND.fullmatch(bound_claims[0])):
        raise ValueError("malformed or duplicate E1000E binding evidence")
    driver_irq_line = int(driver_lines[0].group(3))
    # Match the N1 PCI INTx pin as well as the line against every N6
    # PCI snapshot. A stable but wrong pin is not a valid binding proof.
    route_claims = [line for line in lines
                    if line.startswith("[NET3095-N1] PCI_IRQ_ROUTE=")]
    stable_route_claims = [line for line in lines
                           if line.startswith("[NET3095-N1] PCI_IRQ_ROUTE_STABLE=")]
    route_pin = None
    if route_claims or stable_route_claims:
        if len(route_claims) != 1 or len(stable_route_claims) != 1:
            raise ValueError("incomplete N1 PCI IRQ route evidence")
        route = PCI_IRQ_ROUTE.fullmatch(route_claims[0])
        stable_route = PCI_IRQ_STABLE.fullmatch(stable_route_claims[0])
        if (route is None or stable_route is None or
                route.groups() != stable_route.groups() or
                int(route.group(1)) != driver_irq_line or
                not 1 <= int(route.group(2)) <= 4):
            raise ValueError("N1 PCI IRQ route inconsistent with driver")
        route_pin = int(route.group(2))
    if (driver_irq_line < 1 or driver_irq_line > 254):
        raise ValueError("invalid E1000E driver IRQ line")
    if (node != "pci:0000:00:02.0" or len(bound) != 1 or
            bound[0].group(1) != node or int(resources) < 1 or
            int(published) != 1 or int(sources) < 1 or int(faults) != 0):
        raise ValueError("E1000E DriverKit state invalid or mismatched after stress")
    # A healthy final snapshot must belong to the original, ordered binding.
    # Otherwise a later rebind could mask an invalid initial attachment.
    guest_claims = [line for line in lines
                    if line.startswith("[NET3095-N1] cpu=bsp adapter=")]
    if (len(guest_claims) != 1 or GUEST.fullmatch(guest_claims[0]) is None or
            not (lines.index(driver_claims[0]) < lines.index(bound_claims[0]) <
                 lines.index(guest_claims[0]) < lines.index(baseline_lines[0]))):
        raise ValueError("E1000E initial binding/guest readiness invalid or out of order")
    link = "[NET3095-N6] POST_STRESS_LINK=PASS mac="
    link_lines = [line for line in lines if line.startswith(link)]
    guest = [m for line in lines if (m := GUEST.fullmatch(line))]
    if (len(link_lines) != 1 or len(guest) != 1 or
            link_lines[0] != link + guest[0].group(1) or
            not lines.index(baseline_lines[0]) < lines.index(link_lines[0]) <
            lines.index(claims[0])):
        raise ValueError("E1000E health was not sampled after matching post-stress link")
    # Confirm PCI and IRQ source ownership at all four 32-entry TX ring
    # boundaries and after the final send. A queued TX marker alone is not
    # proof of DMA or IRQ execution; the independent PCAP validates TX.
    burst_claims = [line for line in lines
                    if line.startswith("[NET3095-N6] TX_DRIVER_STABLE")]
    burst_start = "[NET3095-N6] TX_BURST_ATTEMPT=START count=128"
    burst_queued = "[NET3095-N6] TX_BURST=QUEUED count=128"
    post_burst = "[NET3095-N6] POST_BURST_ICMP_ATTEMPT=START"
    if (len(burst_claims) != 1 or lines.count(burst_start) != 1 or
            lines.count(burst_queued) != 1 or lines.count(post_burst) != 1):
        raise ValueError("missing/duplicate TX DriverKit ownership checkpoint")
    burst = BURST.fullmatch(burst_claims[0])
    if (burst is None or burst.group(1) != resources or
            burst.group(2) != sources or not
            (lines.index(baseline_lines[0]) < lines.index(burst_start) <
             lines.index(burst_queued) < lines.index(burst_claims[0]) <
             lines.index(post_burst) < lines.index(claims[0]))):
        raise ValueError("TX DriverKit ownership changed or checkpoint out of order")
    # A summary claiming checks=5 is insufficient: require five individual
    # read-only DriverDiagnostics snapshots across the 32-slot TX ring.
    tx_checks = [line for line in lines
                 if line.startswith("[NET3095-N6] TX_DRIVER_CHECK")]
    if len(tx_checks) != 5:
        raise ValueError("missing or duplicate individual TX DriverKit checkpoints")
    # Each DriverKit snapshot must be paired with a live NIC/lease snapshot.
    tx_nic_checks = [line for line in lines
                     if line.startswith("[NET3095-N6] TX_NIC_CHECK")]
    if len(tx_nic_checks) != 5:
        raise ValueError("missing or duplicate TX NIC/lease checkpoints")
    previous_check = lines.index(burst_start)
    for expected_sent, claim in zip((0, 32, 64, 96, 128), tx_checks):
        checkpoint = TX_CHECK.fullmatch(claim)
        if (checkpoint is None or int(checkpoint.group(1)) != expected_sent or
                checkpoint.group(2) != resources or checkpoint.group(3) != sources):
            raise ValueError("invalid TX DriverKit checkpoint count or ownership")
        check_index = lines.index(claim)
        if not previous_check < check_index < lines.index(burst_queued):
            raise ValueError("TX DriverKit checkpoints missing or out of order")
        nic_claim = tx_nic_checks[expected_sent // 32]
        nic_match = TX_NIC_CHECK.fullmatch(nic_claim)
        nic_index = lines.index(nic_claim)
        if (nic_match is None or int(nic_match.group(1)) != expected_sent or
                not check_index < nic_index < lines.index(burst_queued)):
            raise ValueError("TX NIC/lease checkpoint invalid or out of order")
        previous_check = nic_index
    # Sample the guest NIC, original DHCP lease and DriverKit ownership
    # before/after each post-burst ICMP echo and the final echo. These are
    # read-only source counts, NOT evidence of guest ISR execution.
    anchors = (
        "[NET3095-N6] POST_BURST_ICMP_ATTEMPT=START",
        "[NET3095-N6] POST_BURST_ICMP=PASS from=10.0.2.2 count=4",
        "[NET3095-N6] POST_BURST_HTTP_ATTEMPT=START",
        "[NET3095-N6] FINAL_ICMP_ATTEMPT=START",
        "[NET3095-N6] FINAL_ICMP=PASS from=10.0.2.2 seq=1",
    )
    if any(lines.count(marker) != 1 for marker in anchors):
        raise ValueError("missing or duplicate post-stress ICMP marker")
    post_start, post_done, http_start, final_start, final_done = map(lines.index, anchors)
    if not (lines.index(burst_claims[0]) < post_start < post_done < http_start <
            final_start < final_done < lines.index(link_lines[0])):
        raise ValueError("post-stress ICMP phases out of order")
    checkpoints = [line for line in lines
                   if line.startswith("[NET3095-N6] ICMP_DRIVER_CHECK")]
    expected = ([(phase, seq) for seq in range(2, 6)
                 for phase in ("post_before", "post_after")] +
                [("final_before", 1), ("final_after", 1)])
    if len(checkpoints) != len(expected):
        raise ValueError("missing or duplicate ICMP DriverKit checkpoints")
    previous = post_start
    for (phase, seq), line in zip(expected, checkpoints):
        match = ICMP_CHECK.fullmatch(line)
        if (match is None or match.group(1) != phase or
                int(match.group(2)) != seq or match.group(3) != resources or
                match.group(4) != sources):
            raise ValueError("invalid ICMP checkpoint phase, sequence or ownership")
        pos = lines.index(line)
        if not previous < pos:
            raise ValueError("ICMP DriverKit checkpoints out of order")
        if phase.startswith("post_") and pos >= post_done:
            raise ValueError("post-burst ICMP checkpoint outside echo phase")
        if phase.startswith("final_") and not final_start < pos < final_done:
            raise ValueError("final ICMP checkpoint outside echo phase")
        previous = pos
    # Require six independent, ordered ownership snapshots. A single final
    # snapshot cannot detect a resource/IRQ registration that churned and
    # recovered between TCP sessions. These remain source counts, not ISRs.
    stream_lines = [line for line in lines
                    if line.startswith("[NET3095-N6] HTTP_DRIVER_STABLE")]
    pci_lines = [line for line in lines
                 if line.startswith("[NET3095-N6] HTTP_PCI_STABLE")]
    if len(stream_lines) != 6 or len(pci_lines) != 6:
        raise ValueError("missing/duplicate per-stream PCI or DriverKit snapshots")
    previous_nic_index = lines.index(baseline_lines[0])
    pci_baseline = None
    for attempt, stream_line in enumerate(stream_lines, 1):
        stream = STREAM.fullmatch(stream_line)
        pci = PCI_STREAM.fullmatch(pci_lines[attempt - 1])
        nic_line = f"[NET3095-N6] HTTP_NIC_STABLE=PASS attempt={attempt}"
        if (stream is None or int(stream.group(1)) != attempt or
                stream.group(2) != resources or stream.group(3) != sources or
                lines.count(nic_line) != 1):
            raise ValueError("mismatched per-stream DriverKit ownership")
        if pci is None or int(pci.group(1)) != attempt:
            raise ValueError("invalid per-stream PCI mapping claim")
        command, bar0, irq_line, irq_pin = (int(pci.group(2), 16),
                                            int(pci.group(3), 16),
                                            int(pci.group(4)), int(pci.group(5)))
        if ((command & 0x0006) != 0x0006 or (command & 0x0400) or
                bar0 == 0 or (bar0 & 0xF) or
                irq_line != driver_irq_line or irq_pin < 1 or irq_pin > 4 or
                (route_pin is not None and irq_pin != route_pin)):
            raise ValueError("invalid per-stream PCI MMIO/INTx assignment")
        current_pci = (command, bar0, irq_line, irq_pin)
        if pci_baseline is None:
            pci_baseline = current_pci
        elif current_pci != pci_baseline:
            raise ValueError("PCI BAR/COMMAND/INTx changed between HTTP streams")
        pci_index = lines.index(pci_lines[attempt - 1])
        stream_index = lines.index(stream_line)
        nic_index = lines.index(nic_line)
        if not (previous_nic_index < pci_index < stream_index < nic_index <
                lines.index(claims[0])):
            raise ValueError("per-stream PCI/DriverKit snapshots out of order")
        if (attempt <= 2 and nic_index >= lines.index(burst_start)) or (
                attempt >= 3 and stream_index <= http_start):
            raise ValueError("HTTP DriverKit snapshot on wrong side of TX burst")
        previous_nic_index = nic_index
    if previous_nic_index >= final_start:
        raise ValueError("final ICMP did not follow all six HTTP sessions")
    if any("[NET3095-N6][FAIL]" in line for line in lines):
        raise ValueError("guest reported N6 failure despite DriverKit marker")
    return f"driverkit-bound=yes irq-sources={sources} streams=6 handler-faults=0 ISR-UNPROVEN"


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
        + "".join(
            f"[NET3095-N6] HTTP_PCI_STABLE=PASS attempt={attempt} command=0x0006 bar0=0xFEBF0000 irq_line=11 irq_pin=1 (read-only; ISR unproven)\n"
            f"[NET3095-N6] HTTP_DRIVER_STABLE=PASS attempt={attempt} resources=8 irq_sources=1\n"
            f"[NET3095-N6] HTTP_NIC_STABLE=PASS attempt={attempt}\n"
            for attempt in range(1, 3)
        )
        + "[NET3095-N6] TX_BURST_ATTEMPT=START count=128\n"
        + "".join(
            f"[NET3095-N6] TX_DRIVER_CHECK=PASS sent={count} resources=8 irq_sources=1\n"
            f"[NET3095-N6] TX_NIC_CHECK=PASS sent={count}\n"
            for count in (0, 32, 64, 96, 128)
        )
        + "[NET3095-N6] TX_BURST=QUEUED count=128\n"
        + "[NET3095-N6] TX_DRIVER_STABLE=PASS count=128 checks=5 resources=8 irq_sources=1\n"
        + "[NET3095-N6] POST_BURST_ICMP_ATTEMPT=START\n"
        + "".join(
            f"[NET3095-N6] ICMP_DRIVER_CHECK=PASS phase={phase} seq={seq} resources=8 irq_sources=1\n"
            for seq in range(2, 6) for phase in ("post_before", "post_after")
        )
        + "[NET3095-N6] POST_BURST_ICMP=PASS from=10.0.2.2 count=4\n"
        + "[NET3095-N6] POST_BURST_HTTP_ATTEMPT=START\n"
        + "".join(
            f"[NET3095-N6] HTTP_PCI_STABLE=PASS attempt={attempt} command=0x0006 bar0=0xFEBF0000 irq_line=11 irq_pin=1 (read-only; ISR unproven)\n"
            f"[NET3095-N6] HTTP_DRIVER_STABLE=PASS attempt={attempt} resources=8 irq_sources=1\n"
            f"[NET3095-N6] HTTP_NIC_STABLE=PASS attempt={attempt}\n"
            for attempt in range(3, 7)
        )
        + "[NET3095-N6] FINAL_ICMP_ATTEMPT=START\n"
        + "[NET3095-N6] ICMP_DRIVER_CHECK=PASS phase=final_before seq=1 resources=8 irq_sources=1\n"
        + "[NET3095-N6] ICMP_DRIVER_CHECK=PASS phase=final_after seq=1 resources=8 irq_sources=1\n"
        + "[NET3095-N6] FINAL_ICMP=PASS from=10.0.2.2 seq=1\n"
        + "[NET3095-N6] POST_STRESS_LINK=PASS mac=52:54:00:12:34:56\n"
        "[NET3095-N6] DRIVER_HEALTH=PASS node=pci:0000:00:02.0 driver=E1000EDriver resources=8 published=1 irq_sources=1 handler_faults=0 isr_executions=unknown\n"
    )
    route_marker = "[NET3095-N1] PCI_IRQ_ROUTE=PASS line=11 pin=1 (read-only; guest ISR unproven)"
    stable_marker = "[NET3095-N1] PCI_IRQ_ROUTE_STABLE=PASS line=11 pin=1 (read-only; guest ISR unproven)"
    route_serial = serial.replace(
        "[NET3095-N6] DRIVER_BASELINE=PASS",
        route_marker + "\n" + stable_marker + "\n[NET3095-N6] DRIVER_BASELINE=PASS",
        1)
    assert "line=11" in check_guest_line(route_serial)
    for bad_route in (
        route_serial.replace(route_marker, ""),
        route_serial.replace(stable_marker, ""),
        route_serial.replace("PCI_IRQ_ROUTE=PASS line=11", "PCI_IRQ_ROUTE=PASS line=12"),
        route_serial.replace("PCI_IRQ_ROUTE_STABLE=PASS line=11", "PCI_IRQ_ROUTE_STABLE=PASS line=12"),
        route_serial.replace("PCI_IRQ_ROUTE=PASS line=11 pin=1", "PCI_IRQ_ROUTE=PASS line=11 pin=5"),
        route_serial + "\n" + route_marker,
        route_serial.replace("PCI_IRQ_ROUTE=PASS", "PCI_IRQ_ROUTE=FAIL"),
        route_serial.replace("PCI_IRQ_ROUTE_STABLE=PASS", "PCI_IRQ_ROUTE_STABLE=FAIL"),
        route_serial.replace("guest ISR unproven", "guest ISR proven"),
    ):
        try:
            check_guest_line(bad_route)
        except ValueError:
            pass
        else:
            raise AssertionError("accepted inconsistent PCI IRQ route/driver line")
    for malformed_guest in (
        "[NET3095-N1] cpu=bsp adapter=0 name=e1000e mac=BAD ready=yes link=up",
        "[NET3095-N1] cpu=bsp adapter=0 name=e1000e mac=52:54:00:12:34:56 ready=no link=up",
        "[NET3095-N1] cpu=ap adapter=0 name=e1000e mac=52:54:00:12:34:56 ready=yes link=up",
        "[NET3095-N1] cpu=bsp adapter=0 name=e1000e mac=52:54:00:12:34:56 ready=yes link=up",
    ):
        try:
            check_guest_line(route_serial.replace(
                "[NET3095-N6] DRIVER_BASELINE=PASS",
                malformed_guest + "\\n[NET3095-N6] DRIVER_BASELINE=PASS", 1))
        except ValueError:
            pass
        else:
            raise AssertionError("accepted malformed or duplicate guest readiness")
    assert "line=11" in check_guest_line(serial)
    assert "ISR-UNPROVEN" in check_driver_health(serial)
    assert "ISR-UNPROVEN" in check_driver_health(route_serial)
    # A malformed earlier claim cannot be hidden by the later valid one.
    for malformed in (
        "[Drivers] pci:0000:00:02.0 E1000EDriver: mac BAD, link up, line 11, polling every 10 ms",
        "[Drivers] pci:0000:00:02.0 offer E1000EDriver -> failed",
        "[Drivers] pci:0000:00:02.0 offer E1000EDriver -> bound",
    ):
        bad_serial = route_serial.replace(
            "[NET3095-N6] DRIVER_BASELINE=PASS",
            malformed + "\n[NET3095-N6] DRIVER_BASELINE=PASS", 1)
        try:
            check_driver_health(bad_serial)
        except ValueError:
            pass
        else:
            raise AssertionError("accepted malformed or duplicate driver binding")
    for bad_pin in (
        route_serial.replace("irq_pin=1", "irq_pin=2"),
        route_serial.replace("PCI_IRQ_ROUTE_STABLE=PASS", "PCI_IRQ_ROUTE_STABLE=FAIL"),
        route_serial.replace("PCI_IRQ_ROUTE=PASS line=11 pin=1",
                             "PCI_IRQ_ROUTE=PASS line=11 pin=2"),
    ):
        try:
            check_driver_health(bad_pin)
        except ValueError:
            pass
        else:
            raise AssertionError("accepted mismatched N1/N6 PCI IRQ pin")
    assert "streams=6" in check_driver_health(serial)
    assert "TX_DRIVER_STABLE" in serial
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
    baseline_marker = ("[NET3095-N6] DRIVER_BASELINE=PASS node=pci:0000:00:02.0 "
                       "resources=8 irq_sources=1 handler_faults=0 isr_executions=unknown\n")
    link_marker = "[NET3095-N6] POST_STRESS_LINK=PASS mac=52:54:00:12:34:56\n"
    bad_health = (
        serial.replace("handler_faults=0", "handler_faults=1"),
        serial.replace("resources=8", "resources=0"),
        serial.replace("published=1", "published=0"),
        serial.replace("irq_sources=1", "irq_sources=0"),
        serial.replace("resources=8 irq_sources=1", "resources=9 irq_sources=1"),
        serial.replace("resources=8 irq_sources=1", "resources=8 irq_sources=2"),
        serial.replace("DRIVER_BASELINE=PASS", "DRIVER_BASELINE=FAIL"),
        serial.replace("DRIVER_BASELINE=PASS node=pci:0000:00:02.0",
                       "DRIVER_BASELINE=PASS node=pci:0000:00:03.0"),
        serial.replace(baseline_marker, ""),
        serial + baseline_marker,
        serial.replace(baseline_marker, "").replace(link_marker, link_marker + baseline_marker),
        serial.replace("POST_STRESS_LINK=PASS mac=52:54:00:12:34:56",
                       "POST_STRESS_LINK=PASS mac=02:54:00:12:34:56"),
        serial.replace("isr_executions=unknown", "isr_executions=1"),
        serial.replace("node=pci:0000:00:02.0", "node=pci:0000:00:03.0"),
        serial.replace("pci:0000:00:02.0 offer", "pci:0000:00:03.0 offer"),
        serial.replace("[NET3095-N1] cpu=bsp adapter=0", "[NET3095-N1] cpu=bsp adapter=1"),
        serial.replace("[NET3095-N1] cpu=bsp adapter=0", "[NET3095-N1] cpu=ap adapter=0"),
        serial.replace("[NET3095-N1] cpu=bsp adapter=0", "[NET3095-N1] cpu=bsp adapter=0 ready=no"),
        serial.replace("[NET3095-N6] DRIVER_BASELINE=PASS",
                       "[NET3095-N1] cpu=bsp adapter=0 name=e1000e mac=52:54:00:12:34:56 ready=yes link=up\n[NET3095-N6] DRIVER_BASELINE=PASS", 1),
        serial.replace("[NET3095-N1] cpu=bsp adapter=0 name=e1000e mac=52:54:00:12:34:56 ready=yes link=up\n", "")
              .replace("[Drivers] pci:0000:00:02.0 E1000EDriver: mac",
                       "[NET3095-N1] cpu=bsp adapter=0 name=e1000e mac=52:54:00:12:34:56 ready=yes link=up\n[Drivers] pci:0000:00:02.0 E1000EDriver: mac", 1),
        serial.replace("[NET3095-N6] DRIVER_HEALTH=PASS", "[NET3095-N6] DRIVER_HEALTH=FAIL"),
        serial.replace("[NET3095-N6] POST_STRESS_LINK=PASS", "[NET3095-N6] POST_STRESS_LINK=FAIL"),
        serial.replace(link_marker, ""),
        serial.replace("[NET3095-N6] DRIVER_HEALTH=PASS",
                       "[NET3095-N6][FAIL] bad\n[NET3095-N6] DRIVER_HEALTH=PASS"),
        serial + serial.splitlines()[-1] + "\n",
    )
    stream_line = ("[NET3095-N6] HTTP_DRIVER_STABLE=PASS attempt=3 "
                   "resources=8 irq_sources=1\n")
    nic_line = "[NET3095-N6] HTTP_NIC_STABLE=PASS attempt=3\n"
    bad_health += (
        serial.replace(stream_line, ""),
        serial + stream_line,
        serial.replace(stream_line, stream_line.replace("resources=8", "resources=9")),
        serial.replace(stream_line, stream_line.replace("irq_sources=1", "irq_sources=2")),
        serial.replace(stream_line, stream_line.replace("attempt=3", "attempt=4")),
        serial.replace(nic_line, ""),
        serial + nic_line,
        serial.replace(stream_line + nic_line, nic_line + stream_line),
    )
    pci_line = ("[NET3095-N6] HTTP_PCI_STABLE=PASS attempt=3 command=0x0006 "
                "bar0=0xFEBF0000 irq_line=11 irq_pin=1 (read-only; ISR unproven)\n")
    bad_health += (
        serial.replace(pci_line, ""),
        serial + pci_line,
        serial.replace(pci_line, pci_line.replace("attempt=3", "attempt=4")),
        serial.replace(pci_line, pci_line.replace("command=0x0006", "command=0x0002")),
        serial.replace(pci_line, pci_line.replace("command=0x0006", "command=0x0406")),
        serial.replace(pci_line, pci_line.replace("bar0=0xFEBF0000", "bar0=0xFEBE0000")),
        serial.replace(pci_line, pci_line.replace("bar0=0xFEBF0000", "bar0=0x0")),
        serial.replace(pci_line, pci_line.replace("irq_line=11", "irq_line=12")),
        serial.replace("line 11, polling every 10 ms", "line 12, polling every 10 ms"),
        serial.replace(pci_line, pci_line.replace("irq_pin=1", "irq_pin=0")),
        serial.replace(pci_line, pci_line.replace("HTTP_PCI_STABLE=PASS", "HTTP_PCI_STABLE=FAIL")),
        serial.replace(pci_line + stream_line, stream_line + pci_line),
    )
    burst_marker = ("[NET3095-N6] TX_DRIVER_STABLE=PASS count=128 checks=5 "
                    "resources=8 irq_sources=1\n")
    burst_queued = "[NET3095-N6] TX_BURST=QUEUED count=128\n"
    burst_start = "[NET3095-N6] TX_BURST_ATTEMPT=START count=128\n"
    check32 = "[NET3095-N6] TX_DRIVER_CHECK=PASS sent=32 resources=8 irq_sources=1\n"
    check64 = "[NET3095-N6] TX_DRIVER_CHECK=PASS sent=64 resources=8 irq_sources=1\n"
    nic32 = "[NET3095-N6] TX_NIC_CHECK=PASS sent=32\n"
    nic64 = "[NET3095-N6] TX_NIC_CHECK=PASS sent=64\n"
    bad_health += (
        serial.replace(burst_marker, ""),
        serial + burst_marker,
        serial.replace(burst_marker, burst_marker.replace("resources=8", "resources=9")),
        serial.replace(burst_marker, burst_marker.replace("irq_sources=1", "irq_sources=2")),
        serial.replace(burst_marker, burst_marker.replace("checks=5", "checks=4")),
        serial.replace(burst_queued, ""),
        serial.replace(burst_queued + burst_marker, burst_marker + burst_queued),
        serial.replace(burst_start, ""),
        serial + burst_start,
        serial.replace(check32, ""),
        serial + check32,
        serial.replace(check32, check32.replace("sent=32", "sent=33")),
        serial.replace(check32, check32.replace("resources=8", "resources=9")),
        serial.replace(check32, check32.replace("irq_sources=1", "irq_sources=2")),
        serial.replace(check32 + nic32 + check64 + nic64,
                       check64 + nic64 + check32 + nic32),
        serial.replace(check64, check64.replace("TX_DRIVER_CHECK=PASS", "TX_DRIVER_CHECK=FAIL")),
        serial.replace(nic32, ""),
        serial + nic32,
        serial.replace(nic32, nic32.replace("sent=32", "sent=33")),
        serial.replace(nic32, nic32.replace("TX_NIC_CHECK=PASS", "TX_NIC_CHECK=FAIL")),
        serial.replace(nic32 + check64, check64 + nic32),
        serial.replace(nic32 + check64 + nic64, nic64 + check64 + nic32),
        serial.replace(check32, "").replace(burst_queued, burst_queued + check32),
        serial.replace(burst_start, "").replace(burst_queued, burst_queued + burst_start),
    )
    post_before = "[NET3095-N6] ICMP_DRIVER_CHECK=PASS phase=post_before seq=3 resources=8 irq_sources=1\n"
    post_after = "[NET3095-N6] ICMP_DRIVER_CHECK=PASS phase=post_after seq=3 resources=8 irq_sources=1\n"
    final_before = "[NET3095-N6] ICMP_DRIVER_CHECK=PASS phase=final_before seq=1 resources=8 irq_sources=1\n"
    final_after = "[NET3095-N6] ICMP_DRIVER_CHECK=PASS phase=final_after seq=1 resources=8 irq_sources=1\n"
    bad_health += (
        serial.replace(post_before, ""),
        serial + post_before,
        serial.replace(post_before, post_before.replace("seq=3", "seq=4")),
        serial.replace(post_before, post_before.replace("resources=8", "resources=9")),
        serial.replace(post_after, post_after.replace("irq_sources=1", "irq_sources=2")),
        serial.replace(post_before + post_after, post_after + post_before),
        serial.replace(final_before, ""),
        serial + final_after,
        serial.replace(final_before + final_after, final_after + final_before),
        serial.replace("[NET3095-N6] POST_BURST_ICMP=PASS from=10.0.2.2 count=4\n", ""),
        serial.replace("[NET3095-N6] FINAL_ICMP_ATTEMPT=START\n", ""),
        serial.replace("[NET3095-N6] FINAL_ICMP=PASS from=10.0.2.2 seq=1\n", ""),
        serial.replace("[NET3095-N6] POST_BURST_HTTP_ATTEMPT=START\n", ""),
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
