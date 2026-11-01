#!/usr/bin/env python3
"""N4 TCP RX/TX gate: correlate a real QEMU PCAP with guest and host evidence.

Synthetic fixtures validate the parser only. A PASS from this file is a
real-network proof only when called with the actual QEMU filter-dump capture.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import struct
import re

from verify_3095_network_n1 import pcap_frames
from verify_3095_network_n2 import (
    LEASE, MAC, SERVER, START, fake_dhcp, synthetic_capture,
)
from verify_3095_network_n3 import (
    PING_START, PING_PASS, fake_icmp, normalize_icmp_serial,
    internet_checksum, validate as validate_n3,
)

TCP_START = "[NET3095-N4] TCP_ATTEMPT=START"
REQUEST = b"ZONDERQ3095-TCP-PING\n"
RESPONSE = b"ZONDERQ3095-TCP-PONG\n"
TCP_PASS = f"[NET3095-N4] TCP=PASS from=10.0.2.2 port=18095 bytes={len(RESPONSE)}"
PORT = 18095

# Stock Cosmos can print a scheduler tick between characters of a single
# serial marker. Only reconstruct this EXACT known N4 start tear; arbitrary
# inserted text, malformed markers and duplicates remain hard failures.
TCP_SCHED_TEAR = re.compile(
    r"(?m)^\[NET3095-N4\] TCP\[SCHED\] Tick [0-9]+ enabled=1\n_ATTEMPT=START$"
)


def normalize_tcp_serial(serial: str) -> str:
    # Preserve the historical exact N4 START repair, then handle the same
    # scheduler-only tear at any position of either expected N4 marker.
    # This is not a global log filter: the full reconstructed marker must
    # match exactly, and a malformed/disabled tick must remain a failure.
    lines = TCP_SCHED_TEAR.sub(TCP_START, serial.replace("\r", "")).splitlines()
    repaired = []
    i = 0
    while i < len(lines):
        line = lines[i]
        if line.startswith("[NET3095-N4"):
            head, sep, tick = line.partition("[SCHED] Tick ")
            if (sep and re.fullmatch(r"[0-9]+ enabled=1", tick)
                    and i + 1 < len(lines)
                    and head + lines[i + 1] in (TCP_START, TCP_PASS)):
                repaired.append(head + lines[i + 1])
                i += 2
                continue
        repaired.append(line)
        i += 1
    return "\n".join(repaired)


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
    # Fail closed on damaged wire data even if another matching TCP
    # exchange appears later in the capture. The QEMU 3.0.95 N6 PCAP
    # baseline has valid IPv4 and TCP checksums in both directions.
    if frame[22] == 0 or internet_checksum(frame[14:14 + ihl]) != 0:
        raise ValueError("invalid TCP IPv4 TTL or header checksum")
    segment = frame[offset:14 + length]
    pseudo = frame[26:34] + b"\x00\x06" + len(segment).to_bytes(2, "big")
    if internet_checksum(pseudo + segment) != 0:
        raise ValueError("invalid TCP segment checksum")
    sport, dport, seq, ack = struct.unpack_from(">HHII", frame, offset)
    return {
        "src": frame[26:30], "dst": frame[30:34],
        "src_mac": frame[6:12], "dst_mac": frame[:6],
        "sport": sport, "dport": dport, "seq": seq, "ack": ack,
        "flags": frame[offset + 13],
        "payload": frame[offset + thl:14 + length],
    }


def validate(serial: str, pcap: bytes) -> str:
    serial = normalize_tcp_serial(normalize_icmp_serial(serial))
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
            syn["src_mac"] == MAC and syn["dport"] == PORT and syn["sport"] != 0 and
            (syn["flags"] & 0x17) == 0x02 and not syn["payload"]
        ):
            continue
        sport = syn["sport"]
        for j in range(i + 1, len(packets)):
            synack = packets[j]
            if not (
                synack["src"] == SERVER and synack["dst"] == guest and
                synack["dst_mac"] == MAC and synack["src_mac"] != MAC and
                syn["dst_mac"] == synack["src_mac"] and
                synack["sport"] == PORT and synack["dport"] == sport and
                (synack["flags"] & 0x17) == 0x12 and
                not synack["payload"] and synack["ack"] == (syn["seq"] + 1) & 0xFFFFFFFF
            ):
                continue
            peer_mac = synack["src_mac"]
            for k in range(j + 1, len(packets)):
                ack = packets[k]
                if not (
                    ack["src"] == guest and ack["dst"] == SERVER and
                    ack["src_mac"] == MAC and ack["dst_mac"] == peer_mac and
                    ack["sport"] == sport and
                    ack["dport"] == PORT and (ack["flags"] & 0x17) == 0x10 and
                    ack["seq"] == (syn["seq"] + 1) & 0xFFFFFFFF and
                    ack["ack"] == (synack["seq"] + 1) & 0xFFFFFFFF and
                    not ack["payload"]
                ):
                    continue
                for l in range(k + 1, len(packets)):
                    request = packets[l]
                    if not (
                        request["src"] == guest and request["dst"] == SERVER and
                        request["src_mac"] == MAC and request["dst_mac"] == peer_mac and
                        request["sport"] == sport and
                        request["dport"] == PORT and request["payload"] == REQUEST and
                        (request["flags"] & 0x17) == 0x10 and
                        request["seq"] == (syn["seq"] + 1) & 0xFFFFFFFF and
                        request["ack"] == (synack["seq"] + 1) & 0xFFFFFFFF
                    ):
                        continue
                    for response in packets[l + 1:]:
                        if (
                            response["src"] == SERVER and response["dst"] == guest and
                            response["dst_mac"] == MAC and response["src_mac"] == peer_mac and
                            response["sport"] == PORT and response["dport"] == sport and
                            response["payload"] == RESPONSE and
                            (response["flags"] & 0x17) == 0x10 and
                            response["seq"] == (synack["seq"] + 1) & 0xFFFFFFFF and
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
    header[10:12] = internet_checksum(header).to_bytes(2, "big")
    pseudo = src + dst + b"\x00\x06" + (len(tcp) + len(payload)).to_bytes(2, "big")
    tcp[16:18] = internet_checksum(pseudo + tcp + payload).to_bytes(2, "big")
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
        "[NET3095-N1] PASS READY_LINK=1",
        "[NET3095-N1] DRIVER_BOUND=PASS node=pci:0000:00:02.0 resources=8 irq_sources=1 handler_faults=0 isr_executions=unknown", "[NET3095-N1] TX_QUEUED=yes",
        "[NET3095-N1] DHCP=NOT_ATTEMPTED IP=UNASSIGNED",
        START, "[NET3095-N2] DHCP=PASS ip=10.0.2.15 mask=255.255.255.0 gateway=10.0.2.2",
        PING_START, PING_PASS, TCP_START, TCP_PASS,
    ])
    assert "PASS" in validate(serial, synthetic_capture(frames))
    # Synthetic packet bit flips must not be ignored or rescued by later
    # valid frames with the same sequence/port tuple.
    for offset in (22, 24, 50, len(frames[-1]) - 1):
        corrupted = bytearray(frames[-1])
        corrupted[offset] ^= 1
        try:
            validate(serial, synthetic_capture(frames + [bytes(corrupted)]))
        except ValueError:
            pass
        else:
            raise AssertionError("accepted corrupted TCP/IP checksum or payload")
    torn = serial.replace(
        PING_PASS,
        "[NET3095-N3] ICMP=PASS from=10.[LAPIC] Timer tick 5 RSP=FFFF80007F651C90\n"
        "[SCHED] Tick 5 enabled=1\n0.2.2",
    )
    assert "PASS" in validate(torn, synthetic_capture(frames))
    # Reproduces an actual 2026-11-01 N6 QEMU serial race: the stock
    # scheduler inserted a tick in the middle of TCP_ATTEMPT=START even
    # though TCP echo, six HTTP exchanges and the PCAP were all valid.
    torn_tcp = serial.replace(
        TCP_START, "[NET3095-N4] TCP[SCHED] Tick 6 enabled=1\n_ATTEMPT=START")
    assert "PASS" in validate(torn_tcp, synthetic_capture(frames))
    for marker in (TCP_START, TCP_PASS):
        for offset in range(len("[NET3095-N4"), len(marker)):
            torn_anywhere = (marker[:offset] +
                             "[SCHED] Tick 6 enabled=1\n" + marker[offset:])
            assert "PASS" in validate(
                serial.replace(marker, torn_anywhere),
                synthetic_capture(frames))
    # The repair must not manufacture missing markers, accept arbitrary
    # noise, or silently collapse a duplicated start marker.
    for bad in (
        serial.replace(TCP_START, "[NET3095-N4] TCP[NOISE] Tick 6 enabled=1\n_ATTEMPT=START"),
        serial.replace(TCP_START, "[NET3095-N4] TCP[SCHED] Tick 6 enabled=0\n_ATTEMPT=START"),
        serial + "\n[NET3095-N4] TCP[SCHED] Tick 6 enabled=1\n_ATTEMPT=START",
        serial.replace(TCP_PASS, "[NET3095-N4] TCP=PASS from=10[SCHED] Tick 6 enabled=0\n.0.2.2 port=18095 bytes=21"),
        serial.replace(TCP_PASS, "[NET3095-N4] TCP=PASS from=10[SCHED] Tick 6 enabled=1\n.0.2.3 port=18095 bytes=21"),
        serial.replace(TCP_PASS, "[NET3095-N4] TCP=PASS from=10[SCHED] Tick BAD enabled=1\n.0.2.2 port=18095 bytes=21"),
    ):
        try:
            validate(bad, synthetic_capture(frames))
        except ValueError:
            continue
        raise AssertionError("accepted malformed or duplicated torn TCP start marker")
    # The handshake must bind the subsequent echo to the same L2 peer,
    # not merely to a matching IPv4 address, port and TCP sequence.
    wrong_mac = bytes.fromhex("020000000001")
    def corrupt_mac(frame: bytes, offset: int) -> bytes:
        return frame[:offset] + wrong_mac + frame[offset + 6:]

    negatives = [
        (serial, frames[:7] + [corrupt_mac(frames[7], 0)] + frames[8:]),
        (serial, frames[:7] + [fake_tcp(False, 0x06, 1000, 0)] + frames[8:]),  # SYN|RST
        (serial, frames[:8] + [fake_tcp(True, 0x16, 5000, 1001)] + frames[9:]),  # SYN|ACK|RST
        (serial, frames[:8] + [fake_tcp(True, 0x12, 5000, 1001, b"BAD")] + frames[9:]),
        (serial, frames[:8] + [corrupt_mac(frames[8], 6)] + frames[9:]),
        (serial, frames[:9] + [corrupt_mac(frames[9], 0)] + frames[10:]),
        (serial, frames[:10] + [corrupt_mac(frames[10], 0)] + frames[11:]),
        (serial, frames[:11] + [corrupt_mac(frames[11], 6)]),
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
        (serial, frames[:10] + [fake_tcp(False, 0x18, 1001, 5001, b"BAD"), frames[-1]]),
        # TCP payloads must belong to the exact established sequence space.
        (serial, frames[:10] + [fake_tcp(False, 0x18, 1002, 5001, REQUEST), frames[-1]]),
        (serial, frames[:10] + [fake_tcp(False, 0x18, 1001, 5002, REQUEST), frames[-1]]),
        (serial, frames[:11] + [fake_tcp(True, 0x18, 5002, 1001 + len(REQUEST), RESPONSE)]),
        (serial, frames[:9] + [fake_tcp(False, 0x10, 1002, 5001)] + frames[10:]),
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
