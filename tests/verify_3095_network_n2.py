#!/usr/bin/env python3
"""QEMU-only E1000E RX proof: correlate DHCP DORA with serial lease and N1 TX.

This checks real QEMU filter-dump frames, not just a guest log saying DHCP worked.
Synthetic self-tests below are parser checks, never boot or hardware evidence.
"""
from __future__ import annotations

import ipaddress
from pathlib import Path
import argparse
import re
import struct

from verify_3095_network_n1 import validate as validate_n1, pcap_frames, synthetic_pcap

START = "[NET3095-N2] DHCP_ATTEMPT=START"
LEASE = re.compile(
    r"^\[NET3095-N2\] DHCP=PASS ip=(10\.0\.2\.[0-9]{1,3}) "
    r"mask=(255\.255\.255\.0) gateway=(10\.0\.2\.2)$"
)
MAC = bytes.fromhex("525400123456")
SERVER = bytes([10, 0, 2, 2])


def dhcp_frame(frame: bytes):
    """Return a DHCP record or None. Reject truncated/malformed DHCP records."""
    if len(frame) < 14 or frame[12:14] != b"\x08\x00":
        return None
    if len(frame) < 34 or frame[14] >> 4 != 4:
        return None
    ihl = (frame[14] & 15) * 4
    if ihl < 20 or len(frame) < 14 + ihl + 8 or frame[23] != 17:
        return None
    iplen = int.from_bytes(frame[16:18], "big")
    if iplen < ihl + 8 or len(frame) < 14 + iplen:
        return None
    udp_start = 14 + ihl
    sport, dport, udplen, _checksum = struct.unpack_from(">HHHH", frame, udp_start)
    if udplen < 8 + 240 or udplen > iplen - ihl:
        return None
    bootp = frame[udp_start + 8:udp_start + udplen]
    if len(bootp) < 240 or bootp[1:3] != b"\x01\x06":
        return None
    if bootp[236:240] != b"\x63\x82\x53\x63":
        return None
    opts = {}
    at = 240
    end = False
    while at < len(bootp):
        kind = bootp[at]
        at += 1
        if kind == 255:
            end = True
            break
        if kind == 0:
            continue
        if at >= len(bootp):
            return None
        length = bootp[at]
        at += 1
        if at + length > len(bootp) or kind in opts:
            return None
        opts[kind] = bootp[at:at + length]
        at += length
    if not end or len(opts.get(53, b"")) != 1:
        return None
    typ = opts[53][0]
    if typ not in (1, 2, 3, 5):
        return None
    if typ in (1, 3):
        if bootp[0] != 1 or (sport, dport) != (68, 67):
            return None
    elif bootp[0] != 2 or (sport, dport) != (67, 68):
        return None
    return {
        "type": typ, "xid": bootp[4:8], "chaddr": bootp[28:34],
        "yiaddr": bootp[16:20], "requested": opts.get(50),
        "source_mac": frame[6:12], "source_ip": frame[26:30],
        "destination_mac": frame[:6],
    }


def validate(serial: str, pcap: bytes) -> str:
    # Reuse the already verified N1 raw TX proof, including its strict PCAP reader.
    n1 = validate_n1(serial, pcap)
    lines = [line.strip() for line in serial.replace("\r", "").splitlines()]
    if any("[NET3095-N2][FAIL]" in line for line in lines):
        raise ValueError("DHCP probe explicitly failed")
    lease_lines = [m for line in lines if (m := LEASE.fullmatch(line))]
    if lines.count(START) != 1 or len(lease_lines) != 1:
        raise ValueError("missing/duplicate DHCP attempt or lease marker")
    if lines.index(START) >= lines.index(lease_lines[0].group(0)):
        raise ValueError("DHCP markers out of order")
    ip = ipaddress.IPv4Address(lease_lines[0].group(1))
    if ip not in ipaddress.IPv4Network("10.0.2.0/24") or ip.packed in (
        bytes([10, 0, 2, 0]), SERVER, bytes([10, 0, 2, 255])
    ):
        raise ValueError("unexpected QEMU DHCP lease")
    mac = bytes.fromhex(n1.split("mac=")[-1].replace(":", ""))
    records = [r for frame in pcap_frames(pcap) if (r := dhcp_frame(frame))]
    # Pair DISCOVER/OFFER and REQUEST/ACK separately: stock Cosmos v3.0.95
    # allocates a fresh BOOTP transaction ID for each outbound packet.
    for i, discover in enumerate(records):
        if discover["type"] != 1 or discover["chaddr"] != mac or discover["source_mac"] != mac:
            continue
        for j in range(i + 1, len(records)):
            offer = records[j]
            if (offer["type"] != 2 or offer["chaddr"] != mac or
                offer["xid"] != discover["xid"] or offer["source_ip"] != SERVER):
                continue
            for k in range(j + 1, len(records)):
                request = records[k]
                if (request["type"] != 3 or request["chaddr"] != mac or
                    request["source_mac"] != mac or request["requested"] != offer["yiaddr"]):
                    continue
                for ack in records[k + 1:]:
                    if (ack["type"] == 5 and ack["chaddr"] == mac and
                        ack["xid"] == request["xid"] and ack["source_ip"] == SERVER and
                        ack["yiaddr"] == ip.packed and ack["yiaddr"] == offer["yiaddr"]):
                        return "[NET3095-N2-GATE] PASS real QEMU DHCP DISCOVER/OFFER/REQUEST/ACK + guest IPv4 lease " + str(ip)
    raise ValueError("no correlated QEMU DHCP DORA RX/TX proof")


def fake_dhcp(typ: int, xid: int, mac: bytes, ip: bytes, requested: bytes | None = None,
              source: bytes | None = None) -> bytes:
    """Synthetic packet fixture; deliberately not a hardware proof."""
    client = typ in (1, 3)
    bootp = bytearray(240)
    bootp[0:3] = bytes([1 if client else 2, 1, 6])
    bootp[4:8] = xid.to_bytes(4, "big")
    if not client:
        bootp[16:20] = ip
    bootp[28:34] = mac
    bootp[236:240] = bytes.fromhex("63825363")
    options = bytearray([53, 1, typ])
    if requested is not None:
        options += bytes([50, 4]) + requested
    options.append(255)
    bootp += options
    sport, dport = (68, 67) if client else (67, 68)
    udp = struct.pack(">HHHH", sport, dport, len(bootp) + 8, 0) + bootp
    src = bytes([0, 0, 0, 0]) if client else SERVER
    dst = bytes([255] * 4)
    iphdr = bytearray(20)
    iphdr[0] = 0x45
    iphdr[2:4] = (20 + len(udp)).to_bytes(2, "big")
    iphdr[8:10] = bytes([64, 17])
    iphdr[12:16], iphdr[16:20] = src, dst
    eth = (bytes([255] * 6) if client else mac) + (mac if client else bytes([0x52, 0x54, 0, 0x12, 0x34, 0x99])) + b"\x08\x00"
    return eth + iphdr + udp


def synthetic_capture(frames: list[bytes]) -> bytes:
    header = struct.pack("<IHHIIII", 0xA1B2C3D4, 2, 4, 0, 0, 65535, 1)
    return header + b"".join(struct.pack("<IIII", i + 1, 0, len(f), len(f)) + f for i, f in enumerate(frames))


def self_test() -> None:
    mac = MAC
    ip = bytes([10, 0, 2, 15])
    raw = bytes([255] * 6) + mac + b"\x88\xb5" + b"ZONDERQ3095TX"
    raw += bytes(60 - len(raw))
    frames = [raw, fake_dhcp(1, 1, mac, ip), fake_dhcp(2, 1, mac, ip),
              fake_dhcp(3, 2, mac, ip, ip), fake_dhcp(5, 2, mac, ip)]
    serial = "\n".join([
        "[NET3095-N1] BOOT", "[NET3095-N1] discovered=1",
        "[NET3095-N1] cpu=bsp adapter=0 name=e1000e mac=52:54:00:12:34:56 ready=yes link=up",
        "[NET3095-N1] PASS READY_LINK=1", "[NET3095-N1] TX_QUEUED=yes",
        "[NET3095-N1] DHCP=NOT_ATTEMPTED IP=UNASSIGNED",
        START, "[NET3095-N2] DHCP=PASS ip=10.0.2.15 mask=255.255.255.0 gateway=10.0.2.2"
    ])
    assert "PASS" in validate(serial, synthetic_capture(frames))
    negatives = [
        (serial.replace(START, ""), frames),
        (serial.replace("DHCP=PASS", "DHCP=FAIL"), frames),
        (serial + "\n[NET3095-N2][FAIL] bad", frames),
        (serial.replace("10.0.2.15", "10.0.2.99"), frames),
        (serial, frames[:-1]),
        (serial, [frames[0], frames[1], frames[3], frames[4]]),
        (serial, [frames[0], frames[2], frames[1], frames[3], frames[4]]),
        (serial, [frames[0], frames[1], frames[2], fake_dhcp(3, 2, mac, ip, bytes([10, 0, 2, 88])), frames[4]]),
        (serial, [frames[0], frames[1], frames[2], frames[3], fake_dhcp(5, 7, mac, ip)]),
        (serial, [frames[0], frames[1], fake_dhcp(2, 9, mac, ip), frames[3], frames[4]]),
        (serial, [frames[0], frames[1], fake_dhcp(2, 1, bytes([2, 0, 0, 0, 0, 1]), ip), frames[3], frames[4]]),
    ]
    for bad_serial, bad_frames in negatives:
        try:
            validate(bad_serial, synthetic_capture(bad_frames))
        except ValueError:
            continue
        raise AssertionError("accepted invalid DHCP RX proof")
    print("[NET3095-N2-CONTRACT] PASS synthetic DORA positive + negative cases")


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
            ap.exit(1, f"[NET3095-N2-GATE][FAIL] {exc}\n")
