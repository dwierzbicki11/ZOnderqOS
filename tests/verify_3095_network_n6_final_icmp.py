#!/usr/bin/env python3
"""N6 final RX liveness: ICMP echo AFTER six real QEMU HTTP FIN/ACK exchanges.

A synthetic self-test checks only this validator. Actual network proof requires
the real QEMU guest serial and filter-dump PCAP plus all preceding N1-N6 gates.
This does NOT establish guest ISR execution or physical RTL810xE support.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import struct

from verify_3095_network_n1 import pcap_frames
from verify_3095_network_n2 import LEASE, MAC, SERVER, synthetic_capture
from verify_3095_network_n3 import fake_icmp, internet_checksum
from verify_3095_network_n4 import tcp_frame
from verify_3095_network_n6 import find_http_transaction
from verify_3095_network_n6_post_http import (
    REPEATS, check_link_markers, validate as validate_post_http,
)

START = "[NET3095-N6] FINAL_ICMP_ATTEMPT=START"
PASS = "[NET3095-N6] FINAL_ICMP=PASS from=10.0.2.2 seq=1"
IDENT = 0x3097
SEQ = 1


def final_echo(frame: bytes):
    """Parse complete unfragmented IPv4 echo with a valid wire checksum."""
    if len(frame) < 42 or frame[12:14] != b"\x08\x00":
        return None
    if frame[14] >> 4 != 4 or frame[23] != 1:
        return None
    ihl = (frame[14] & 15) * 4
    if ihl < 20 or len(frame) < 14 + ihl + 8:
        return None
    length = int.from_bytes(frame[16:18], "big")
    if (int.from_bytes(frame[20:22], "big") & 0x3FFF or
            length < ihl + 8 or len(frame) < 14 + length):
        return None
    offset = 14 + ihl
    typ, code, _checksum, ident, seq = struct.unpack_from(">BBHHH", frame, offset)
    if ident != IDENT:
        return None
    if typ not in (0, 8) or code != 0 or seq != SEQ:
        raise ValueError("unexpected final ICMP type/code/sequence")
    if (frame[22] == 0 or
            internet_checksum(frame[14:14 + ihl]) != 0 or
            internet_checksum(frame[offset:14 + length]) != 0):
        raise ValueError("final ICMP has invalid IPv4/ICMP checksum or TTL")
    return {
        "type": typ, "src": frame[26:30], "dst": frame[30:34],
        "src_mac": frame[6:12], "dst_mac": frame[:6],
        "payload": frame[offset + 8:14 + length],
    }


def check_final_pair(frames: list[bytes], after_index: int, guest: bytes) -> None:
    events = [(i, packet) for i, frame in enumerate(frames)
              if (packet := final_echo(frame))]
    if len(events) != 2:
        raise ValueError("expected exactly one final ICMP request/reply pair")
    (request_index, request), (reply_index, reply) = events
    if not after_index < request_index < reply_index:
        raise ValueError("final ICMP was not after the sixth HTTP FIN/ACK")
    if not (
        request["type"] == 8 and request["src"] == guest and
        request["dst"] == SERVER and request["src_mac"] == MAC and
        request["dst_mac"] != MAC and request["dst_mac"] != b"\x00" * 6 and
        not (request["dst_mac"][0] & 1) and
        reply["type"] == 0 and reply["src"] == SERVER and
        reply["dst"] == guest and reply["dst_mac"] == MAC and
        reply["src_mac"] == request["dst_mac"] and
        len(request["payload"]) == 32 and
        request["payload"] == reply["payload"]
    ):
        raise ValueError("final ICMP peer, MAC or echoed payload mismatch")


def check_final_markers(serial: str) -> None:
    lines = [line.strip() for line in serial.replace("\r", "").splitlines()]
    if any("[NET3095-N6][FAIL]" in line for line in lines):
        raise ValueError("guest reported N6 failure")
    if lines.count(START) != 1 or lines.count(PASS) != 1:
        raise ValueError("missing or duplicated final ICMP serial markers")
    links = [i for i, line in enumerate(lines)
             if line.startswith("[NET3095-N6] POST_STRESS_LINK=")]
    if len(links) != 1:
        raise ValueError("missing unique post-stress link marker")
    if not lines.index(REPEATS[-1]) < lines.index(START) < lines.index(PASS) < links[0]:
        raise ValueError("final ICMP markers are not after HTTP and before link health")


def last_http_fin_ack(frames: list[bytes], guest: bytes) -> int:
    entries = [(i, packet) for i, frame in enumerate(frames)
               if (packet := tcp_frame(frame))]
    packets = [packet for _, packet in entries]
    cursor = 0
    used_ports = set()
    last_frame = -1
    for attempt in range(1, 7):
        exchange = find_http_transaction(packets, guest, cursor, None, attempt=attempt)
        if exchange is None:
            raise ValueError(f"missing HTTP session {attempt} before final ICMP")
        port, finish = exchange
        if port in used_ports:
            raise ValueError("HTTP TCP source port reused")
        used_ports.add(port)
        last_frame = entries[finish][0]
        cursor = finish + 1
    return last_frame


def validate(serial: str, pcap: bytes) -> str:
    validate_post_http(serial, pcap)
    check_link_markers(serial)
    check_final_markers(serial)
    leases = [m for line in serial.replace("\r", "").splitlines()
              if (m := LEASE.fullmatch(line.strip()))]
    if len(leases) != 1:
        raise ValueError("missing unique DHCP lease")
    guest = bytes(int(part) for part in leases[0].group(1).split("."))
    frames = list(pcap_frames(pcap))
    check_final_pair(frames, last_http_fin_ack(frames, guest), guest)
    return ("[NET3095-N6-FINAL-ICMP-GATE] PASS real QEMU ICMP RX/TX after "
            "six HTTP FIN/ACK exchanges; guest ISR execution UNPROVEN")


def fake_final(reply: bool, guest: bytes) -> bytes:
    frame = bytearray(fake_icmp(reply, MAC, guest))
    offset = 14 + 20
    struct.pack_into(">H", frame, offset + 4, IDENT)
    frame[offset + 2:offset + 4] = b"\x00\x00"
    frame[offset + 2:offset + 4] = internet_checksum(frame[offset:]).to_bytes(2, "big")
    return bytes(frame)


def self_test() -> None:
    guest = bytes([10, 0, 2, 15])
    request, reply = fake_final(False, guest), fake_final(True, guest)
    frames = [bytes(60), request, reply]
    check_final_pair(frames, 0, guest)
    assert len(list(pcap_frames(synthetic_capture(frames)))) == 3
    serial = "\n".join((
        REPEATS[-1], START, PASS,
        "[NET3095-N6] POST_STRESS_LINK=PASS mac=52:54:00:12:34:56",
    ))
    check_final_markers(serial)
    bad_serial = (
        serial.replace(START, ""),
        serial.replace(PASS, ""),
        serial + "\n" + PASS,
        serial.replace(START, PASS).replace(PASS + "\n" + PASS, START + "\n" + PASS),
        serial.replace(REPEATS[-1], ""),
        serial.replace("POST_STRESS_LINK=PASS", "POST_STRESS_LINK=FAIL") +
        "\n[NET3095-N6][FAIL] bad",
    )
    for bad in bad_serial:
        try:
            check_final_markers(bad)
        except ValueError:
            continue
        raise AssertionError("accepted invalid final ICMP serial evidence")
    wrong_mac = bytes.fromhex("020000000001")
    bad_cases = (
        [bytes(60), request],
        [bytes(60), reply],
        [bytes(60), reply, request],
        [bytes(60), request, reply, reply],
        [request, reply],
        [bytes(60), request, reply[:40]],
        [bytes(60), request[:6] + wrong_mac + request[12:], reply],
        [bytes(60), request, reply[:6] + wrong_mac + reply[12:]],
        [bytes(60), request, reply[:22] + b"\x00" + reply[23:]],
        [bytes(60), request, reply[:-1] + bytes([reply[-1] ^ 1])],
        [bytes(60), request, fake_final(True, bytes([10, 0, 2, 16]))],
    )
    for invalid in bad_cases:
        try:
            check_final_pair(invalid, 0, guest)
        except ValueError:
            continue
        raise AssertionError("accepted invalid final ICMP wire evidence")
    print("[NET3095-N6-FINAL-ICMP-CONTRACT] PASS synthetic serial/ICMP positive and negative cases")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("serial", nargs="?", type=Path)
    parser.add_argument("pcap", nargs="?", type=Path)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
    else:
        if args.serial is None or args.pcap is None:
            parser.error("real QEMU guest serial AND filter-dump PCAP required")
        try:
            print(validate(args.serial.read_text(errors="replace"), args.pcap.read_bytes()))
        except (ValueError, OSError) as exc:
            parser.exit(1, f"[NET3095-N6-FINAL-ICMP-GATE][FAIL] {exc}\n")
