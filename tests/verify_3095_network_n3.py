#!/usr/bin/env python3
"""N3 real QEMU PCAP proof of IPv4 ICMP echo request and inbound echo reply.

The N1 TX and N2 DHCP DORA gates must both pass first. Synthetic fixtures
test the parser only; only an actual QEMU run establishes NIC RX evidence.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import re
import struct

from verify_3095_network_n1 import pcap_frames
from verify_3095_network_n2 import (
    LEASE, MAC, SERVER, START, fake_dhcp, synthetic_capture, validate as validate_n2
)

PING_START = "[NET3095-N3] ICMP_ATTEMPT=START"
PING_PASS = "[NET3095-N3] ICMP=PASS from=10.0.2.2"
ECHO_ID = 0x3095
ECHO_SEQ = 1


def normalize_icmp_serial(serial: str) -> str:
    """Repair only exact N3 markers split by a known BSP timer-log splice.

    A single-vCPU scheduler tick can interrupt DiagnosticLog.WriteString at
    any character. Some boots emit a matching LAPIC/SCHED pair, while others
    splice a standalone SCHED tick into the middle of an exact N3 marker.
    Reconstruct ONLY an exact marker with a complete known tick record.
    Arbitrary insertions, malformed fragments and duplicate markers must
    still fail. Independent PCAP evidence is mandatory.
    """
    lines = serial.replace("\r", "").splitlines()
    repaired = []
    i = 0
    while i < len(lines):
        line = lines[i]
        if line.startswith("[NET3095-N3"):
            # QEMU N5 evidence (run 41360128503) has an actual echo reply
            # and a SCHED-only tick spliced into ICMP=PASS. Require the
            # remaining line to reconstruct the exact expected marker;
            # never strip arbitrary scheduler diagnostics globally.
            head, sep, tick = line.partition("[SCHED] Tick ")
            sched_only = (re.fullmatch(r"([0-9]+) enabled=[01]", tick)
                          if sep else None)
            if (sched_only and i + 1 < len(lines)
                    and head + lines[i + 1] in (PING_START, PING_PASS)):
                repaired.append(head + lines[i + 1])
                i += 2
                continue
            head, sep, tick = line.partition("[LAPIC] Timer tick ")
            lapic = re.fullmatch(r"([0-9]+) RSP=[0-9A-Fa-f]+", tick) if sep else None
            sched = (re.fullmatch(r"\[SCHED\] Tick ([0-9]+) enabled=[01]", lines[i + 1])
                     if i + 1 < len(lines) else None)
            if (lapic and sched and lapic.group(1) == sched.group(1)
                    and i + 2 < len(lines)
                    and head + lines[i + 2] in (PING_START, PING_PASS)):
                repaired.append(head + lines[i + 2])
                i += 3
                continue
        repaired.append(line)
        i += 1
    return "\n".join(repaired)


def internet_checksum(data: bytes) -> int:
    """RFC 1071 one's-complement checksum; zero validates a wire header."""
    if len(data) & 1:
        data += b"\x00"
    total = sum(int.from_bytes(data[i:i + 2], "big")
                for i in range(0, len(data), 2))
    while total > 0xFFFF:
        total = (total & 0xFFFF) + (total >> 16)
    return (~total) & 0xFFFF


def icmp_frame(frame: bytes):
    if len(frame) < 14 + 20 + 8 or frame[12:14] != b"\x08\x00":
        return None
    if frame[14] >> 4 != 4 or frame[23] != 1:
        return None
    ihl = (frame[14] & 15) * 4
    if ihl < 20 or len(frame) < 14 + ihl + 8:
        return None
    length = int.from_bytes(frame[16:18], "big")
    # IPv4 fragments cannot establish a complete ICMP echo proof.
    if (int.from_bytes(frame[20:22], "big") & 0x3FFF) or length < ihl + 8:
        return None
    if len(frame) < 14 + length:
        return None
    offset = 14 + ihl
    typ, code, _checksum, ident, seq = struct.unpack_from(">BBHHH", frame, offset)
    if typ not in (0, 8) or code != 0 or ident != ECHO_ID or seq != ECHO_SEQ:
        return None
    if (frame[22] == 0 or
            internet_checksum(frame[14:14 + ihl]) != 0 or
            internet_checksum(frame[offset:14 + length]) != 0):
        raise ValueError("N3 ICMP packet has invalid IPv4/ICMP checksum or TTL")
    return {
        "type": typ, "src": frame[26:30], "dst": frame[30:34],
        "src_mac": frame[6:12], "dst_mac": frame[:6],
        "payload": frame[offset + 8:14 + length],
    }


def validate(serial: str, pcap: bytes) -> str:
    serial = normalize_icmp_serial(serial)
    validate_n2(serial, pcap)
    lines = [line.strip() for line in serial.replace("\r", "").splitlines()]
    if any("[NET3095-N3][FAIL]" in line for line in lines):
        raise ValueError("guest ICMP probe explicitly failed")
    if lines.count(PING_START) != 1 or lines.count(PING_PASS) != 1:
        raise ValueError("missing or duplicate ICMP attempt/reply marker")
    if lines.index(PING_START) >= lines.index(PING_PASS):
        raise ValueError("ICMP markers out of order")
    leases = [m for line in lines if (m := LEASE.fullmatch(line))]
    if len(leases) != 1:
        raise ValueError("missing unique guest DHCP lease")
    guest_ip = bytes(int(x) for x in leases[0].group(1).split("."))
    frames = [p for f in pcap_frames(pcap) if (p := icmp_frame(f))]
    for i, req in enumerate(frames):
        if (req["type"] != 8 or req["src"] != guest_ip or req["dst"] != SERVER
            or req["src_mac"] != MAC or req["dst_mac"] == MAC or
            req["dst_mac"] == b"\x00" * 6 or
            (req["dst_mac"][0] & 1)):
            continue
        for reply in frames[i + 1:]:
            if (reply["type"] == 0 and reply["src"] == SERVER and
                reply["dst"] == guest_ip and reply["dst_mac"] == MAC and
                reply["src_mac"] == req["dst_mac"] and
                reply["src_mac"] != MAC and
                reply["src_mac"] != b"\x00" * 6 and
                not (reply["src_mac"][0] & 1) and
                len(req["payload"]) == 32 and
                req["payload"] == reply["payload"]):
                return "[NET3095-N3-GATE] PASS real QEMU ICMP echo request/reply with matching 32-byte payload guest=10.0.2.15 gateway=10.0.2.2"
    raise ValueError("no correlated QEMU ICMP echo RX/TX proof")


def fake_icmp(reply: bool, mac: bytes, ip: bytes, seq: int = ECHO_SEQ) -> bytes:
    src, dst = (SERVER, ip) if reply else (ip, SERVER)
    hdr = bytearray(20)
    hdr[0] = 0x45
    hdr[2:4] = (60).to_bytes(2, "big")
    hdr[8:10] = bytes([64, 1])
    hdr[12:16], hdr[16:20] = src, dst
    # Same 32-byte echo payload length observed in the real QEMU N3 PCAP.
    # Synthetic frames must carry valid wire checksums to test the parser.
    icmp = bytearray(struct.pack(">BBHHH", 0 if reply else 8, 0, 0, ECHO_ID, seq) +
                     bytes(range(32)))
    hdr[10:12] = internet_checksum(hdr).to_bytes(2, "big")
    icmp[2:4] = internet_checksum(icmp).to_bytes(2, "big")
    return ((mac if reply else bytes.fromhex("525400123499")) +
            (bytes.fromhex("525400123499") if reply else mac) +
            b"\x08\x00" + hdr + icmp)


def self_test() -> None:
    mac = MAC
    ip = bytes([10, 0, 2, 15])
    raw = bytes([255] * 6) + mac + b"\x88\xb5" + b"ZONDERQ3095TX"
    raw += bytes(60 - len(raw))
    frames = [
        raw, fake_dhcp(1, 1, mac, ip), fake_dhcp(2, 1, mac, ip),
        fake_dhcp(3, 2, mac, ip, ip), fake_dhcp(5, 2, mac, ip),
        fake_icmp(False, mac, ip), fake_icmp(True, mac, ip)
    ]
    serial = "\n".join([
        "[NET3095-N1] BOOT", "[NET3095-N1] discovered=1",
        "[NET3095-N1] cpu=bsp adapter=0 name=e1000e mac=52:54:00:12:34:56 ready=yes link=up",
        "[NET3095-N1] PASS READY_LINK=1",
        "[NET3095-N1] PCI_IDENTITY=PASS bdf=00:02.0 vendor=8086 device=10D3 command=0x0006 (read-only; DMA/ISR unproven)",
        "[NET3095-N1] DRIVER_BOUND=PASS node=pci:0000:00:02.0 resources=8 irq_sources=1 handler_faults=0 isr_executions=unknown", "[NET3095-N1] TX_QUEUED=yes",
        "[NET3095-N1] PCI_TX_STABLE=PASS command=0x0006 (read-only; PCAP required for TX proof)",
        "[NET3095-N1] DHCP=NOT_ATTEMPTED IP=UNASSIGNED",
        START, "[NET3095-N2] DHCP=PASS ip=10.0.2.15 mask=255.255.255.0 gateway=10.0.2.2",
        PING_START, PING_PASS
    ])
    assert "PASS" in validate(serial, synthetic_capture(frames))
    torn = serial.replace(
        PING_PASS,
        "[NET3095-N3] ICMP=PASS from=10.[LAPIC] Timer tick 5 RSP=FFFF80007F651C90\n"
        "[SCHED] Tick 5 enabled=1\n0.2.2",
    )
    assert "PASS" in validate(torn, synthetic_capture(frames))
    # Real N5 QEMU failure: the timer interrupted the START marker before
    # its closing bracket. The PCAP had the actual ICMP request and reply.
    torn_start = serial.replace(
        PING_START,
        "[NET3095-N3[LAPIC] Timer tick 5 RSP=FFFF80007F5C0BA0\n"
        "[SCHED] Tick 5 enabled=1\n] ICMP_ATTEMPT=START",
    )
    assert "PASS" in validate(torn_start, synthetic_capture(frames))
    # Replay the exact scheduler-only splice seen in real N5 serial evidence.
    torn_sched = serial.replace(
        PING_PASS,
        "[NET3095-N3] ICMP=PASS from=10[SCHED] Tick 6 enabled=1\n.0.2.2",
    )
    assert "PASS" in validate(torn_sched, synthetic_capture(frames))
    # A tick can split any part of the marker, not only 'from=10'.
    for marker in (PING_START, PING_PASS):
        for offset in range(len("[NET3095-N3"), len(marker)):
            split = (marker[:offset] + "[SCHED] Tick 6 enabled=1\n" +
                     marker[offset:])
            assert "PASS" in validate(
                serial.replace(marker, split), synthetic_capture(frames))
    bad = [
        (torn_sched.replace("Tick 6", "Tick not-a-number"), frames),
        (torn_sched.replace("enabled=1", "enabled=2"), frames),
        (torn_sched.replace("\n.0.2.2", "\n.0.2.3"), frames),
        (torn_sched + "\n" + PING_PASS, frames),
        (torn_start.replace("[SCHED] Tick 5", "[SCHED] Tick 6"), frames),
        (torn_start.replace("RSP=FFFF80007F5C0BA0", "RSP=NOTHEX"), frames),
        (torn_start.replace("] ICMP_ATTEMPT=START", "] ICMP_ATTEMPT=OTHER"), frames),
        (torn_start + "\n" + PING_START, frames),
        (torn.replace("\n0.2.2", "\n0.2.3"), frames),
        (torn.replace("[SCHED] Tick 5 enabled=1", "[OTHER] injected"), frames),
        (torn.replace("RSP=FFFF80007F651C90", "RSP=NOTHEX"), frames),
        (torn + "\n" + PING_PASS, frames),
        (torn + "\n" + "[NET3095-N3] ICMP=PASS from=10.[LAPIC] Timer tick 5 RSP=FFFF80007F651C90\n"
         "[SCHED] Tick 5 enabled=1\n0.2.2", frames),
        (serial.replace(PING_START, ""), frames),
        (serial.replace(PING_PASS, ""), frames),
        (serial + "\n[NET3095-N3][FAIL] timeout", frames),
        (serial, frames[:-1]),
        (serial, frames[:5] + [frames[6], frames[5]]),
        (serial, frames[:6] + [fake_icmp(True, mac, ip, seq=2)]),
        (serial, frames[:6] + [fake_icmp(True, bytes.fromhex("525400000001"), ip)]),
        (serial.replace("10.0.2.15", "10.0.2.25"), frames),
        (serial, frames[:6] + [frames[6][:24] + bytes([frames[6][24] ^ 1]) + frames[6][25:]]),
        (serial, frames[:6] + [frames[6][:36] + bytes([frames[6][36] ^ 1]) + frames[6][37:]]),
        (serial, frames[:6] + [frames[6][:45] + bytes([frames[6][45] ^ 1]) + frames[6][46:]]),
        (serial, frames[:6] + [frames[6][:22] + b"\x00" + frames[6][23:]]),
        (serial, frames[:6] + [frames[6][:6] + bytes.fromhex("525400000001") + frames[6][12:]]),
        (serial, frames[:5] + [bytes.fromhex("525400000001") + frames[5][6:]] + frames[6:]),
        (serial, frames[:6] + [frames[6][:6] + b"\x00" * 6 + frames[6][12:]]),
        (serial, frames[:5] + [b"\xff" * 6 + frames[5][6:]] + frames[6:]),
    ]
    for text, packets in bad:
        try:
            validate(text, synthetic_capture(packets))
        except ValueError:
            continue
        raise AssertionError("accepted invalid ICMP round-trip proof")
    print("[NET3095-N3-CONTRACT] PASS synthetic ICMP echo positive + negative cases")


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
            print(validate(args.serial_log.read_text(errors="replace"), args.pcap.read_bytes()))
        except (ValueError, OSError) as exc:
            ap.exit(1, f"[NET3095-N3-GATE][FAIL] {exc}\n")
