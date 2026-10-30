#!/usr/bin/env python3
"""N4 TCP RX/TX gate: correlate a real QEMU PCAP with guest and host evidence.

Synthetic fixtures validate the parser only. A PASS from this file is a
real-network proof only when called with the actual QEMU filter-dump capture.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import struct

from verify_3095_network_n1 import pcap_frames
from verify_3095_network_n2 import (
    LEASE, MAC, SERVER, START, fake_dhcp, synthetic_capture,
)
from verify_3095_network_n3 import (
    PING_START, PING_PASS, fake_icmp, validate as validate_n3,
)

TCP_START = "[NET3095-N4] TCP_ATTEMPT=START"
REQUEST = b"ZONDERQ3095-TCP-PING\n"
RESPONSE = b"ZONDERQ3095-TCP-PONG\n"
TCP_PASS = f"[NET3095-N4] TCP=PASS from=10.0.2.2 port=18095 bytes={len(RESPONSE)}"
PORT = 18095


def tcp_frame(frame: bytes):
    """Extract bounded, unfragmented IPv4 TCP segments from Ethernet frames."""
    if len(frame) < 14 + 20 + 20 or frame[12:14] != b"\x08\x00":
        return None
    if frame[14] >> 4 != 4 or frame[23] != 6:
        return None
    ihl = (frame[14] & 15) * 4
    if ihl < 20 or len(frame) < 14 + ihl + 20:
        return None
    length = int.from_bytes(frame[16:18], "big")
    if (int.from_bytes(frame[20:22], "big") & 0x3FFF) or length < ihl + 20:
        return None
    if len(frame) < 14 + length:
        return None
    offset = 14 + ihl
    thl = (frame[offset + 12] >> 4) * 4
    if thl < 20 or length < ihl + thl:
        return None
    sport, dport, seq, ack = struct.unpack_from(">HHII", frame, offset)
    return {
        "src": frame[26:30], "dst": frame[30:34],
        "src_mac": frame[6:12], "dst_mac": frame[:6],
        "sport": sport, "dport": dport, "seq": seq, "ack": ack,
        "flags": frame[offset + 13],
        "payload": frame[offset + thl:14 + length],
    }


def validate(serial: str, pcap: bytes) -> str:
    validate_n3(serial, pcap)
    lines = [line.strip() for line in serial.replace("\r", "").splitlines()]
    if any("[NET3095-N4][FAIL]" in line for line in lines):
        raise ValueError("guest TCP probe explicitly failed")
    if lines.count(TCP_START) != 1 or lines.count(TCP_PASS) != 1:
        raise ValueError("missing or duplicate TCP attempt/reply marker")
    if lines.index(TCP_START) >= lines.index(TCP_PASS) or lines.index(PING_PASS) >= lines.index(TCP_START):
        raise ValueError("TCP/ICMP markers out of order")
    leases = [m for line in lines if (m := LEASE.fullmatch(line))]
    if len(leases) != 1:
        raise ValueError("missing unique guest lease")
    guest = bytes(int(part) for part in leases[0].group(1).split("."))
    packets = [p for frame in pcap_frames(pcap) if (p := tcp_frame(frame))]
    for i, syn in enumerate(packets):
        if not (
            syn["src"] == guest and syn["dst"] == SERVER and
            syn["src_mac"] == MAC and syn["dport"] == PORT and
            (syn["flags"] & 0x12) == 0x02 and not syn["payload"]
        ):
            continue
        sport = syn["sport"]
        for j in range(i + 1, len(packets)):
            synack = packets[j]
            if not (
                synack["src"] == SERVER and synack["dst"] == guest and
                synack["dst_mac"] == MAC and synack["src_mac"] != MAC and
                synack["sport"] == PORT and synack["dport"] == sport and
                (synack["flags"] & 0x12) == 0x12 and
                synack["ack"] == (syn["seq"] + 1) & 0xFFFFFFFF
            ):
                continue
            for k in range(j + 1, len(packets)):
                ack = packets[k]
                if not (
                    ack["src"] == guest and ack["dst"] == SERVER and
                    ack["src_mac"] == MAC and ack["sport"] == sport and
                    ack["dport"] == PORT and (ack["flags"] & 0x17) == 0x10 and
                    ack["ack"] == (synack["seq"] + 1) & 0xFFFFFFFF and
                    not ack["payload"]
                ):
                    continue
                for l in range(k + 1, len(packets)):
                    request = packets[l]
                    if not (
                        request["src"] == guest and request["dst"] == SERVER and
                        request["src_mac"] == MAC and request["sport"] == sport and
                        request["dport"] == PORT and request["payload"] == REQUEST and
                        (request["flags"] & 0x10)
                    ):
                        continue
                    for response in packets[l + 1:]:
                        if (
                            response["src"] == SERVER and response["dst"] == guest and
                            response["dst_mac"] == MAC and response["src_mac"] != MAC and
                            response["sport"] == PORT and response["dport"] == sport and
                            response["payload"] == RESPONSE and
                            (response["flags"] & 0x10) and
                            response["ack"] == (request["seq"] + len(REQUEST)) & 0xFFFFFFFF
                        ):
                            return "[NET3095-N4-GATE] PASS real QEMU TCP handshake and echo RX/TX guest=10.0.2.15 server=10.0.2.2:18095"
    raise ValueError("no correlated QEMU TCP handshake and echo RX/TX proof")


def fake_tcp(reply: bool, flags: int, seq: int, ack: int, payload: bytes = b"",
             sport: int = 49152, dport: int = PORT) -> bytes:
    ip = bytes([10, 0, 2, 15])
    src, dst = (SERVER, ip) if reply else (ip, SERVER)
    srcmac = bytes.fromhex("525400123499")
    ethernet = ((MAC if reply else srcmac) +
                (srcmac if reply else MAC) + b"\x08\x00")
    header = bytearray(20)
    header[0] = 0x45
    header[2:4] = (40 + len(payload)).to_bytes(2, "big")
    header[8:10] = bytes([64, 6])
    header[12:16], header[16:20] = src, dst
    tcp = bytearray(20)
    struct.pack_into(">HHII", tcp, 0, dport if reply else sport,
                     sport if reply else dport, seq, ack)
    tcp[12] = 0x50
    tcp[13] = flags
    tcp[14:16] = (8192).to_bytes(2, "big")
    return ethernet + header + tcp + payload


def self_test() -> None:
    ip = bytes([10, 0, 2, 15])
    raw = bytes([255] * 6) + MAC + b"\x88\xb5" + b"ZONDERQ3095TX"
    raw += bytes(60 - len(raw))
    frames = [
        raw, fake_dhcp(1, 1, MAC, ip), fake_dhcp(2, 1, MAC, ip),
        fake_dhcp(3, 2, MAC, ip, ip), fake_dhcp(5, 2, MAC, ip),
        fake_icmp(False, MAC, ip), fake_icmp(True, MAC, ip),
        fake_tcp(False, 0x02, 1000, 0),
        fake_tcp(True, 0x12, 5000, 1001),
        fake_tcp(False, 0x10, 1001, 5001),
        fake_tcp(False, 0x18, 1001, 5001, REQUEST),
        fake_tcp(True, 0x18, 5001, 1001 + len(REQUEST), RESPONSE),
    ]
    serial = "\n".join([
        "[NET3095-N1] BOOT", "[NET3095-N1] discovered=1",
        "[NET3095-N1] cpu=bsp adapter=0 name=e1000e mac=52:54:00:12:34:56 ready=yes link=up",
        "[NET3095-N1] PASS READY_LINK=1", "[NET3095-N1] TX_QUEUED=yes",
        "[NET3095-N1] DHCP=NOT_ATTEMPTED IP=UNASSIGNED",
        START, "[NET3095-N2] DHCP=PASS ip=10.0.2.15 mask=255.255.255.0 gateway=10.0.2.2",
        PING_START, PING_PASS, TCP_START, TCP_PASS,
    ])
    assert "PASS" in validate(serial, synthetic_capture(frames))
    negatives = [
        (serial.replace(TCP_START, ""), frames),
        (serial.replace(TCP_PASS, ""), frames),
        (serial + "\n" + TCP_PASS, frames),
        (serial + "\n[NET3095-N4][FAIL] bad", frames),
        (serial.replace(TCP_START, "").replace(PING_PASS, TCP_START + "\n" + PING_PASS), frames),
        (serial, frames[:8] + frames[9:]),
        (serial, frames[:11]),
        (serial, frames[:11] + [fake_tcp(True, 0x18, 5001, 1001 + len(REQUEST), b"WRONG")]),
        (serial, frames[:8] + [fake_tcp(True, 0x12, 5000, 9999)] + frames[9:]),
        (serial, frames[:11] + [fake_tcp(True, 0x18, 5001, 1001 + len(REQUEST), RESPONSE, dport=18096)]),
        (serial, frames[:10] + [fake_tcp(False, 0x18, 1001, 5001, b"BAD") , frames[-1]]),
    ]
    for bad_serial, bad_frames in negatives:
        try:
            validate(bad_serial, synthetic_capture(bad_frames))
        except ValueError:
            continue
        raise AssertionError("accepted invalid TCP RX/TX proof")
    print("[NET3095-N4-CONTRACT] PASS synthetic TCP handshake/echo positive + negative cases")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("serial_log", nargs="?", type=Path)
    parser.add_argument("pcap", nargs="?", type=Path)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
    else:
        if args.serial_log is None or args.pcap is None:
            parser.error("real QEMU serial log AND PCAP required")
        try:
            print(validate(args.serial_log.read_text(errors="replace"), args.pcap.read_bytes()))
        except (ValueError, OSError) as exc:
            parser.exit(1, f"[NET3095-N4-GATE][FAIL] {exc}\n")
