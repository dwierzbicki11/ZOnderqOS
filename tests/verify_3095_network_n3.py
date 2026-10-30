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
    return {
        "type": typ, "src": frame[26:30], "dst": frame[30:34],
        "src_mac": frame[6:12], "dst_mac": frame[:6]
    }


def validate(serial: str, pcap: bytes) -> str:
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
            or req["src_mac"] != MAC):
            continue
        for reply in frames[i + 1:]:
            if (reply["type"] == 0 and reply["src"] == SERVER and
                reply["dst"] == guest_ip and reply["dst_mac"] == MAC and
                reply["src_mac"] != MAC):
                return "[NET3095-N3-GATE] PASS real QEMU ICMP echo request/reply guest=10.0.2.15 gateway=10.0.2.2"
    raise ValueError("no correlated QEMU ICMP echo RX/TX proof")


def fake_icmp(reply: bool, mac: bytes, ip: bytes, seq: int = ECHO_SEQ) -> bytes:
    src, dst = (SERVER, ip) if reply else (ip, SERVER)
    hdr = bytearray(20)
    hdr[0] = 0x45
    hdr[2:4] = (28).to_bytes(2, "big")
    hdr[8:10] = bytes([64, 1])
    hdr[12:16], hdr[16:20] = src, dst
    icmp = struct.pack(">BBHHH", 0 if reply else 8, 0, 0, ECHO_ID, seq)
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
        "[NET3095-N1] PASS READY_LINK=1", "[NET3095-N1] TX_QUEUED=yes",
        "[NET3095-N1] DHCP=NOT_ATTEMPTED IP=UNASSIGNED",
        START, "[NET3095-N2] DHCP=PASS ip=10.0.2.15 mask=255.255.255.0 gateway=10.0.2.2",
        PING_START, PING_PASS
    ])
    assert "PASS" in validate(serial, synthetic_capture(frames))
    bad = [
        (serial.replace(PING_START, ""), frames),
        (serial.replace(PING_PASS, ""), frames),
        (serial + "\n[NET3095-N3][FAIL] timeout", frames),
        (serial, frames[:-1]),
        (serial, frames[:5] + [frames[6], frames[5]]),
        (serial, frames[:6] + [fake_icmp(True, mac, ip, seq=2)]),
        (serial, frames[:6] + [fake_icmp(True, bytes.fromhex("525400000001"), ip)]),
        (serial.replace("10.0.2.15", "10.0.2.25"), frames),
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
