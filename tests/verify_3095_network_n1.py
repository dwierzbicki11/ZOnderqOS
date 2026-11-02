#!/usr/bin/env python3
"""Strict N1 serial + real QEMU PCAP proof. Synthetic self-tests are not boot proof."""
from __future__ import annotations

import argparse
from pathlib import Path
import re
import struct

BOOT = "[NET3095-N1] BOOT"
END = "[NET3095-N1] DHCP=NOT_ATTEMPTED IP=UNASSIGNED"
TX = "[NET3095-N1] TX_QUEUED=yes"
DEVICE = re.compile(
    r"^\[NET3095-N1\] cpu=bsp adapter=(\d+) name=e1000e "
    r"mac=([0-9a-fA-F]{2}(?::[0-9a-fA-F]{2}){5}) "
    r"ready=yes link=up$"
)
DISCOVER = re.compile(r"^\[NET3095-N1\] discovered=(\d+)$")
ACCEPT = re.compile(r"^\[NET3095-N1\] PASS READY_LINK=(\d+)$")
PAYLOAD = b"ZONDERQ3095TX"
ETHER_TYPE = b"\x88\xb5"
BROADCAST = b"\xff" * 6
DRIVER = re.compile(
    r"^\[NET3095-N1\] DRIVER_BOUND=PASS node=pci:0000:00:02\.0 "
    r"resources=([0-9]+) irq_sources=([0-9]+) "
    r"handler_faults=0 isr_executions=unknown$"
)

PCI = re.compile(
    r"^\[NET3095-N1\] PCI_IDENTITY=PASS bdf=00:02\.0 vendor=8086 "
    r"device=10D3 command=0x([0-9A-Fa-f]{4}) "
    r"\(read-only; DMA/ISR unproven\)$"
)
PCI_TX = re.compile(
    r"^\[NET3095-N1\] PCI_TX_STABLE=PASS command=0x([0-9A-Fa-f]{4}) "
    r"\(read-only; PCAP required for TX proof\)$"
)


# Optional in historical N1 fixtures, mandatory as a paired, ordered,
# internally consistent claim whenever the new QEMU guest emits it.
# PCI routing metadata does NOT establish execution of the guest ISR.
PCI_IRQ_ROUTE = re.compile(
    r"^\[NET3095-N1\] PCI_IRQ_ROUTE=PASS line=([0-9]+) pin=([0-9]+) "
    r"\(read-only; guest ISR unproven\)$"
)
PCI_IRQ_STABLE = re.compile(
    r"^\[NET3095-N1\] PCI_IRQ_ROUTE_STABLE=PASS line=([0-9]+) pin=([0-9]+) "
    r"\(read-only; guest ISR unproven\)$"
)

# Stock Cosmos serial writes can be preempted by a timer callback.
# A bare scheduler tick OR the complete LAPIC+SCHED pair may split one N1
# marker (observed in real N2 QEMU run 41478097771). Repair only if removing
# that exact, bounded diagnostic sequence reconstructs a STRICT N1 marker.
# Never strip unrelated lines, partial ticks, or mismatched tick numbers.
SCHED_TICK = re.compile(
    r"(?:\[LAPIC\] Timer tick (?P<tick>[0-9]+) RSP=[0-9A-Fa-f]+\n"
    r"\[SCHED\] Tick (?P=tick) enabled=1\n"
    r"|\[SCHED\] Tick [0-9]+ enabled=1\n)"
)
# Dynamic inventory and DriverKit records are just as vulnerable to a serial
# tick as fixed BOOT/TX/END markers. Reconstruct only records that satisfy the
# original strict full-line validators, never arbitrary diagnostic fragments.
N1_MARKERS = (
    re.compile(re.escape(BOOT)), DISCOVER, DEVICE, ACCEPT, PCI,
    PCI_IRQ_ROUTE, DRIVER, re.compile(re.escape(TX)), PCI_IRQ_STABLE,
    PCI_TX, re.compile(re.escape(END)),
)


def normalize_n1_serial(data: str) -> str:
    raw = data.replace("\r", "")
    for pattern in N1_MARKERS:
        found = []
        for tick in SCHED_TICK.finditer(raw):
            start = raw.rfind("\n", 0, tick.start()) + 1
            end = raw.find("\n", tick.end())
            if end < 0:
                end = len(raw)
            # A standalone tick before or after the marker is not a splice.
            if start == tick.start() or end == tick.end():
                continue
            candidate = raw[start:tick.start()] + raw[tick.end():end]
            if pattern.fullmatch(candidate):
                found.append((start, end, candidate))
        if len(found) > 1:
            raise ValueError("duplicate scheduler-preempted N1 marker")
        if found:
            start, end, candidate = found[0]
            raw = raw[:start] + candidate + raw[end:]
    return raw


def validate_serial(data: str) -> bytes:
    lines = [line.strip() for line in normalize_n1_serial(data).splitlines()]
    if any(re.search(r"kernel panic|fatal:\s*exception|page fault", line, re.I)
           or "[NET3095-N1][FAIL]" in line
           or "[SMP3094-MANAGED-STAGE23] PASS" in line
           for line in lines):
        raise ValueError("kernel fault, probe failure or unrelated SMP marker")
    if lines.count(BOOT) != 1 or lines.count(END) != 1 or lines.count(TX) != 1:
        raise ValueError("missing or repeated BOOT, TX or END marker")
    # Reject extra malformed/unknown N1 claims instead of ignoring them while
    # accepting a separate valid copy. Heartbeats have their own liveness gate.
    for line in lines:
        if not line.startswith("[NET3095-N1]") or line.startswith(
            "[NET3095-N1] HEARTBEAT="
        ):
            continue
        if (line not in (BOOT, TX, END) and
                not any(pattern.fullmatch(line) for pattern in
                        (DISCOVER, DEVICE, ACCEPT, PCI, PCI_IRQ_ROUTE,
                         DRIVER, PCI_IRQ_STABLE, PCI_TX))):
            raise ValueError("unexpected or malformed N1 diagnostic marker")
    found = [m for line in lines if (m := DISCOVER.fullmatch(line))]
    ready = [m for line in lines if (m := ACCEPT.fullmatch(line))]
    devices = [m for line in lines if (m := DEVICE.fullmatch(line))]
    bindings = [m for line in lines if (m := DRIVER.fullmatch(line))]
    identities = [m for line in lines if (m := PCI.fullmatch(line))]
    tx_states = [m for line in lines if (m := PCI_TX.fullmatch(line))]
    if len(identities) != 1 or len(tx_states) != 1:
        raise ValueError("missing, duplicated or malformed E1000E PCI identity/command")
    before_command = int(identities[0].group(1), 16)
    after_command = int(tx_states[0].group(1), 16)
    if (before_command & 0x0006) != 0x0006 or after_command != before_command:
        raise ValueError("E1000E PCI MMIO/busmaster missing or command changed during TX")
    before_routes = [m for line in lines if (m := PCI_IRQ_ROUTE.fullmatch(line))]
    after_routes = [m for line in lines if (m := PCI_IRQ_STABLE.fullmatch(line))]
    if len(before_routes) != len(after_routes) or len(before_routes) > 1:
        raise ValueError("missing, duplicated or unpaired E1000E PCI IRQ route")
    if before_routes:
        before_line, before_pin = map(int, before_routes[0].groups())
        after_line, after_pin = map(int, after_routes[0].groups())
        if (not 1 <= before_line <= 254 or not 1 <= before_pin <= 4 or
                (before_line, before_pin) != (after_line, after_pin) or
                before_command & 0x0400):
            raise ValueError("invalid or changed E1000E legacy PCI INTx route")
    binding_claims = [line for line in lines
                      if line.startswith("[NET3095-N1] DRIVER_BOUND=")]
    if len(bindings) != 1 or binding_claims != [bindings[0].group(0)]:
        raise ValueError("missing, duplicated or malformed E1000E DriverKit binding")
    if int(bindings[0].group(1)) < 1 or int(bindings[0].group(2)) < 1:
        raise ValueError("invalid E1000E PCI resources or IRQ source registration")
    if len(found) != 1 or len(ready) != 1 or len(devices) != 1:
        raise ValueError("expected one inventory, ready count and E1000E")
    adapter_claims = [line for line in lines
                      if line.startswith("[NET3095-N1] cpu=bsp adapter=")]
    if len(adapter_claims) != 1 or adapter_claims[0] != devices[0].group(0):
        raise ValueError("unexpected or malformed additional NIC inventory")
    count = int(found[0].group(1))
    ready_count = int(ready[0].group(1))
    index = int(devices[0].group(1))
    # This isolated QEMU profile must not silently accept an extra NIC.
    if count != 1 or ready_count != 1 or index != 0:
        raise ValueError("invalid NIC count or registration index")
    mac = bytes.fromhex(devices[0].group(2).replace(":", ""))
    if mac == b"\x00" * 6 or mac == BROADCAST or (mac[0] & 1):
        raise ValueError("invalid unicast MAC")
    offsets = [lines.index(BOOT), lines.index(found[0].group(0)),
               lines.index(devices[0].group(0)), lines.index(ready[0].group(0)),
               lines.index(identities[0].group(0)), lines.index(bindings[0].group(0)),
               lines.index(TX), lines.index(tx_states[0].group(0)), lines.index(END)]
    if before_routes:
        # New records are interleaved before DriverKit binding and before
        # the TX-stability record; enforce that order in real QEMU serial.
        offsets.insert(5, lines.index(before_routes[0].group(0)))
        offsets.insert(-2, lines.index(after_routes[0].group(0)))
    if offsets != sorted(set(offsets)):
        raise ValueError("out-of-order or duplicated probe markers")
    return mac


def pcap_frames(data: bytes):
    if len(data) < 24:
        raise ValueError("missing PCAP header")
    magic = data[:4]
    if magic in (b"\xd4\xc3\xb2\xa1", b"\x4d\x3c\xb2\xa1"):
        endian = "<"
    elif magic in (b"\xa1\xb2\xc3\xd4", b"\xa1\xb2\x3c\x4d"):
        endian = ">"
    else:
        raise ValueError("invalid PCAP magic")
    major, minor, _tz, _sig, snaplen, linktype = struct.unpack(
        endian + "HHIIII", data[4:24]
    )
    if (major, minor) != (2, 4) or linktype != 1 or snaplen < 60:
        raise ValueError("expected Ethernet PCAP v2.4")
    offset = 24
    while offset < len(data):
        if len(data) - offset < 16:
            raise ValueError("truncated PCAP record header")
        _sec, _usec, captured, original = struct.unpack(
            endian + "IIII", data[offset:offset + 16]
        )
        offset += 16
        # A truncated snapshot can contain a valid-looking header and proof
        # payload while concealing the remainder of a damaged Ethernet frame.
        # QEMU filter-dump is configured for complete frames: reject partial
        # records instead of treating them as successful TX/RX evidence.
        if captured > snaplen or captured != original or captured < 14 or captured > 65535:
            raise ValueError("truncated or invalid Ethernet PCAP record")
        if offset + captured > len(data):
            raise ValueError("truncated PCAP frame")
        yield data[offset:offset + captured]
        offset += captured


def validate(serial: str, pcap: bytes) -> str:
    mac = validate_serial(serial)
    wanted = BROADCAST + mac + ETHER_TYPE + PAYLOAD
    expected = wanted + bytes(60 - len(wanted))
    # N6 reuses this validator and legitimately adds tagged 128-frame bursts.
    # Accept those only as a separate class: they must not substitute for,
    # duplicate or conceal a malformed initial N1 Ethernet transmission.
    burst_prefix = b"ZONDERQ3095-BURST-"
    initial_count = 0
    # Only N6 may transmit the 128-frame tagged burst. A stand-alone N1
    # probe that emits unexpected diagnostics is not a valid single-TX proof.
    allow_burst = "[NET3095-N6] TX_BURST_ATTEMPT=START count=128" in serial
    # Consume every record, including the tail, before accepting evidence.
    for frame in pcap_frames(pcap):
        if frame[12:14] != ETHER_TYPE:
            continue
        if frame[14:].startswith(burst_prefix):
            if not allow_burst:
                raise ValueError("unexpected N6 burst frame during N1-only proof")
            continue  # N6's own validator checks every burst byte and order.
        if frame != expected:
            raise ValueError("malformed or unexpected N1 diagnostic Ethernet frame")
        initial_count += 1
    if initial_count != 1:
        raise ValueError("expected exactly one complete QEMU-captured N1 TX frame")
    return f"[NET3095-N1-GATE] PASS E1000E_LINK_AND_RAW_TX mac={mac.hex(':')}"


def synthetic_pcap(frame: bytes) -> bytes:
    header = struct.pack("<IHHIIII", 0xA1B2C3D4, 2, 4, 0, 0, 65535, 1)
    record = struct.pack("<IIII", 1, 0, len(frame), len(frame))
    return header + record + frame


def self_test() -> None:
    serial = "\n".join([
        BOOT,
        "[NET3095-N1] discovered=1",
        "[NET3095-N1] cpu=bsp adapter=0 name=e1000e mac=52:54:00:12:34:56 ready=yes link=up",
        "[NET3095-N1] PASS READY_LINK=1",
        "[NET3095-N1] PCI_IDENTITY=PASS bdf=00:02.0 vendor=8086 device=10D3 command=0x0006 (read-only; DMA/ISR unproven)",
        "[NET3095-N1] PCI_IRQ_ROUTE=PASS line=11 pin=1 (read-only; guest ISR unproven)",
        "[NET3095-N1] DRIVER_BOUND=PASS node=pci:0000:00:02.0 resources=8 irq_sources=1 handler_faults=0 isr_executions=unknown",
        TX,
        "[NET3095-N1] PCI_IRQ_ROUTE_STABLE=PASS line=11 pin=1 (read-only; guest ISR unproven)",
        "[NET3095-N1] PCI_TX_STABLE=PASS command=0x0006 (read-only; PCAP required for TX proof)", END,
    ])
    frame = BROADCAST + bytes.fromhex("525400123456") + ETHER_TYPE + PAYLOAD
    frame += b"\x00" * (60 - len(frame))
    good = synthetic_pcap(frame)
    assert "PASS" in validate(serial, good)
    # Real N6 QEMU run 41400794536 had a scheduler tick inserted into
    # TX_QUEUED=yes after '='; PCAP and all later HTTP/TCP exchanges existed.
    # Exercise every internal split position of every N1 marker, not
    # merely that one observed scheduler interleaving.
    for marker in serial.splitlines():
        for pos in range(1, len(marker)):
            torn = serial.replace(
                marker, marker[:pos] + "[SCHED] Tick 7 enabled=1\n" + marker[pos:]
            )
            assert "PASS" in validate(torn, good)
            # Real N2 QEMU run 41478097771: the LAPIC callback and its
            # matching scheduler tick interrupted PCI_TX_STABLE mid-word.
            paired = (
                "[LAPIC] Timer tick 5 RSP=FFFF80007F64EB80\n"
                "[SCHED] Tick 5 enabled=1\n"
            ).replace("\\\n", "\n")
            paired_torn = serial.replace(
                marker, marker[:pos] + paired + marker[pos:]
            )
            assert "PASS" in validate(paired_torn, good)
        for bad_pair in (
            "[LAPIC] Timer tick 5 RSP=FFFF80007F64EB80\n[SCHED] Tick 6 enabled=1\n",
            "[LAPIC] Timer tick 5 RSP=INVALID\n[SCHED] Tick 5 enabled=1\n",
            "[LAPIC] Timer tick 5 RSP=FFFF80007F64EB80\n",
            "[LAPIC] Timer tick 5 RSP=FFFF80007F64EB80\n[SCHED] Tick 5 enabled=0\n",
            "[LAPIC] Timer tick 5 RSP=FFFF80007F64EB80\n[OTHER] Tick 5 enabled=1\n",
        ):
            torn = serial.replace(marker, marker[:5] + bad_pair + marker[5:])
            try:
                validate(torn, good)
            except ValueError:
                pass
            else:
                raise AssertionError("accepted incomplete/mismatched LAPIC scheduler splice")
        for bad_tick in (
            "[SCHED] Tick 7 enabled=0\n",
            "[SCHED] Tick seven enabled=1\n",
            "[SCHED] Tick 7 enabled=1",
        ):
            torn = serial.replace(marker, marker[:5] + bad_tick + marker[5:])
            try:
                validate(torn, good)
            except ValueError:
                pass
            else:
                raise AssertionError("accepted malformed scheduler-spliced N1 marker")
        duplicate_torn = serial.replace(
            marker, marker[:5] + "[SCHED] Tick 7 enabled=1\n" + marker[5:]
        ) + "\n" + marker
        try:
            validate(duplicate_torn, good)
        except ValueError:
            pass
        else:
            raise AssertionError("accepted duplicated N1 marker after splice")
    bad_serial = [
        serial.replace(BOOT, ""),
        serial + "\n" + BOOT,
        serial.replace("discovered=1", "discovered=0"),
        serial.replace("discovered=1", "discovered=2"),
        serial.replace("READY_LINK=1", "READY_LINK=2"),
        serial.replace("adapter=0", "adapter=1"),
        serial + "\n[NET3095-N1] cpu=bsp adapter=1 name=rtl8139 mac=52:54:00:12:34:99 ready=no link=down",
        serial + "\n[NET3095-N1] cpu=bsp adapter=1 name=e1000e mac=52:54:00:12:34:99 ready=yes link=up",
        serial.replace("name=e1000e", "name=virtio-net"),
        serial.replace("mac=52:54:00:12:34:56", "mac=00:00:00:00:00:00"),
        serial.replace("mac=52:54:00:12:34:56", "mac=53:54:00:12:34:56"),
        serial.replace("ready=yes", "ready=no"),
        serial.replace("link=up", "link=down"),
        serial.replace("READY_LINK=1", "READY_LINK=0"),
        serial.replace("[NET3095-N1] DRIVER_BOUND=PASS node=pci:0000:00:02.0 resources=8 irq_sources=1 handler_faults=0 isr_executions=unknown", ""),
        serial + "\n[NET3095-N1] DRIVER_BOUND=PASS node=pci:0000:00:02.0 resources=8 irq_sources=1 handler_faults=0 isr_executions=unknown",
        serial.replace("resources=8 irq_sources=1", "resources=0 irq_sources=1"),
        serial.replace("resources=8 irq_sources=1", "resources=8 irq_sources=0"),
        serial.replace("node=pci:0000:00:02.0", "node=pci:0000:00:03.0"),
        serial.replace(TX, "[NET3095-N1] TX_QUEUED=no"),
        serial.replace("[NET3095-N1] PCI_IDENTITY=PASS bdf=00:02.0 vendor=8086 device=10D3 command=0x0006 (read-only; DMA/ISR unproven)", ""),
        serial + "\n[NET3095-N1] PCI_IDENTITY=PASS bdf=00:02.0 vendor=8086 device=10D3 command=0x0006 (read-only; DMA/ISR unproven)",
        serial.replace("vendor=8086 device=10D3", "vendor=10EC device=8136"),
        serial.replace("bdf=00:02.0", "bdf=00:03.0"),
        serial.replace("PCI_IDENTITY=PASS", "PCI_IDENTITY=UNKNOWN"),
        serial.replace("PCI_TX_STABLE=PASS", "PCI_TX_STABLE=UNKNOWN"),
        serial.replace("PCI_IRQ_ROUTE=PASS", "PCI_IRQ_ROUTE=UNKNOWN"),
        serial.replace("PCI_IRQ_ROUTE_STABLE=PASS", "PCI_IRQ_ROUTE_STABLE=UNKNOWN"),
        serial.replace("[NET3095-N1] PCI_IRQ_ROUTE=PASS line=11 pin=1 (read-only; guest ISR unproven)", ""),
        serial.replace("[NET3095-N1] PCI_IRQ_ROUTE_STABLE=PASS line=11 pin=1 (read-only; guest ISR unproven)", ""),
        serial.replace("PCI_IRQ_ROUTE=PASS line=11", "PCI_IRQ_ROUTE=PASS line=0"),
        serial.replace("PCI_IRQ_ROUTE=PASS line=11", "PCI_IRQ_ROUTE=PASS line=255"),
        serial.replace("PCI_IRQ_ROUTE=PASS line=11", "PCI_IRQ_ROUTE=PASS line=12"),
        serial.replace("PCI_IRQ_ROUTE_STABLE=PASS line=11", "PCI_IRQ_ROUTE_STABLE=PASS line=12"),
        serial.replace("PCI_IRQ_ROUTE=PASS line=11 pin=1", "PCI_IRQ_ROUTE=PASS line=11 pin=0"),
        serial.replace("PCI_IRQ_ROUTE_STABLE=PASS line=11 pin=1", "PCI_IRQ_ROUTE_STABLE=PASS line=11 pin=5"),
        serial + "\n[NET3095-N1] PCI_IRQ_ROUTE=PASS line=11 pin=1 (read-only; guest ISR unproven)",
        serial + "\n[NET3095-N1] PCI_IRQ_ROUTE_STABLE=PASS line=11 pin=1 (read-only; guest ISR unproven)",
        serial.replace("[NET3095-N1] PCI_IDENTITY=PASS bdf=00:02.0 vendor=8086 device=10D3 command=0x0006 (read-only; DMA/ISR unproven)", "[NET3095-N1] PCI_IDENTITY=PASS bdf=00:02.0 vendor=8086 device=10D3 command=0x0002 (read-only; DMA/ISR unproven)"),
        serial.replace("[NET3095-N1] PCI_TX_STABLE=PASS command=0x0006 (read-only; PCAP required for TX proof)", ""),
        serial + "\n[NET3095-N1] PCI_TX_STABLE=PASS command=0x0006 (read-only; PCAP required for TX proof)",
        serial.replace("[NET3095-N1] PCI_TX_STABLE=PASS command=0x0006 (read-only; PCAP required for TX proof)", "[NET3095-N1] PCI_TX_STABLE=PASS command=0x0002 (read-only; PCAP required for TX proof)"),
        serial.replace("[NET3095-N1] PCI_TX_STABLE=PASS command=0x0006 (read-only; PCAP required for TX proof)", "[NET3095-N1] PCI_TX_STABLE=PASS command=0x0007 (read-only; PCAP required for TX proof)"),
        serial.replace("[NET3095-N1] PCI_TX_STABLE=PASS command=0x0006 (read-only; PCAP required for TX proof)", "").replace(
            "[NET3095-N1] PCI_IDENTITY=PASS bdf=00:02.0 vendor=8086 device=10D3 command=0x0006 (read-only; DMA/ISR unproven)", "[NET3095-N1] PCI_IDENTITY=PASS bdf=00:02.0 vendor=8086 device=10D3 command=0x0006 (read-only; DMA/ISR unproven)\n[NET3095-N1] PCI_TX_STABLE=PASS command=0x0006 (read-only; PCAP required for TX proof)"),
        serial + "\n[NET3095-N1] TX_QUEUED=no",
        serial + "\n[NET3095-N1] discovered=bad",
        serial + "\n[NET3095-N1] PASS READY_LINK=unknown",
        serial + "\n[NET3095-N1] BOOTX",
        serial + "\n[NET3095-N1] DHCP=NOT_ATTEMPTED IP=UNASSIGNEDX",
        serial.replace(TX + "\n", ""),
        serial.replace(END, ""),
        serial + "\nKERNEL PANIC",
        serial + "\n[NET3095-N1][FAIL] TX timeout",
        serial.replace(TX + "\n", "") + "\n" + TX,
    ]
    for sample in bad_serial:
        try:
            validate(sample, good)
        except ValueError:
            pass
        else:
            raise AssertionError("accepted invalid serial evidence")
    bad_pcap = [
        b"", good[:10], good[:-1], good[:24],
        synthetic_pcap(frame.replace(PAYLOAD, b"ZONDERQ3095RX")),
        synthetic_pcap(b"\x00" * 6 + frame[6:]),
        synthetic_pcap(frame[:6] + b"\x00" * 6 + frame[12:]),
        synthetic_pcap(frame[:12] + b"\x08\x00" + frame[14:]),
        good[:20] + b"\x00\x00\x00\x00" + good[24:],
        good + b"\x00",  # Corrupt tail after an otherwise matching frame.
        good[:24] + struct.pack("<IIII", 1, 0, len(frame), len(frame) + 4) + frame,
        good + struct.pack("<IIII", 2, 0, 0, 0),
        good + struct.pack("<IIII", 2, 0, 13, 13) + frame[:13],
    ]
    # The N1 proof must not accept a good prefix with corrupted padding,
    # duplicate N1 transmissions, or a forged diagnostic EtherType.
    burst = BROADCAST + bytes.fromhex("525400123456") + ETHER_TYPE + b"ZONDERQ3095-BURST-000"
    burst += bytes(60 - len(burst))
    n6_serial = serial + "\n[NET3095-N6] TX_BURST_ATTEMPT=START count=128"
    assert "PASS" in validate(n6_serial, synthetic_pcap(frame) +
                              synthetic_pcap(burst)[24:])
    # N1-only cannot hide a second tagged TX; N6 uses a separate full
    # sequence/PCAP validator for these 128 frames.
    try:
        validate(serial, synthetic_pcap(frame) + synthetic_pcap(burst)[24:])
    except ValueError:
        pass
    else:
        raise AssertionError("accepted N6 burst in stand-alone N1 proof")
    bad_pcap.extend([
        synthetic_pcap(frame + b"X"),
        synthetic_pcap(frame[:-1] + b"X"),
        synthetic_pcap(frame[:6] + bytes.fromhex("525400123499") + frame[12:]),
        synthetic_pcap(frame) + synthetic_pcap(frame)[24:],
        synthetic_pcap(frame) + synthetic_pcap(frame[:-1] + b"X")[24:],
        synthetic_pcap(frame) + synthetic_pcap(
            frame[:14] + b"ZONDERQ3095-OTHER" + bytes(60 - 14 - 18))[24:],
        synthetic_pcap(frame) + synthetic_pcap(
            burst[:14] + b"ZONDERQ3095-BURST" + burst[32:])[24:],
    ])
    for data in bad_pcap:
        try:
            validate(serial, data)
        except ValueError:
            pass
        else:
            raise AssertionError("accepted invalid PCAP evidence")
    print("[NET3095-N1-CONTRACT] PASS synthetic serial/PCAP positive + negative cases")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("serial_log", nargs="?", type=Path)
    ap.add_argument("pcap", nargs="?", type=Path)
    ap.add_argument("--self-test", action="store_true")
    args = ap.parse_args()
    if args.self_test:
        self_test()
    else:
        if args.serial_log is None or args.pcap is None:
            ap.error("real QEMU serial log AND PCAP required")
        try:
            print(validate(args.serial_log.read_text(errors="replace"),
                           args.pcap.read_bytes()))
        except (ValueError, OSError) as exc:
            ap.exit(1, f"[NET3095-N1-GATE][FAIL] {exc}\n")
