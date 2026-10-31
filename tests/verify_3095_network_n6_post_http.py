#!/usr/bin/env python3
"""Fail-closed QEMU N6 proof: third full TCP/HTTP RX after TX and ICMP stress.

Synthetic fixtures are validator unit tests only. Real proof requires the
same guest serial and filter-dump PCAP as the N6 QEMU smoke runner.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import re

from network_3095_http_host import PORT, REQUEST, RESPONSE
from verify_3095_network_n1 import pcap_frames
from verify_3095_network_n2 import LEASE, synthetic_capture
from verify_3095_network_n4 import tcp_frame
from verify_3095_network_n6 import (
    fake_http, find_http_transaction,
)
from verify_3095_e1000e_irq_binding import GUEST
from verify_3095_network_n6_burst import (
    POST_PASS, check_frames, check_markers, check_post_burst_icmp,
    last_http_frame, post_burst_icmp, validate as validate_burst,
)

START = "[NET3095-N6] POST_BURST_HTTP_ATTEMPT=START"
PASS = f"[NET3095-N6] POST_BURST_HTTP=PASS from=10.0.2.2 port={PORT} bytes={len(RESPONSE)}"
LINK = re.compile(r"^\[NET3095-N6\] POST_STRESS_LINK=PASS mac=([0-9a-fA-F]{2}(?::[0-9a-fA-F]{2}){5})$")


def check_link_markers(serial: str) -> None:
    lines = [line.strip() for line in serial.replace("\r", "").splitlines()]
    guest = [match for line in lines if (match := GUEST.fullmatch(line))]
    link = [match for line in lines if (match := LINK.fullmatch(line))]
    if len(guest) != 1 or len(link) != 1:
        raise ValueError("missing or duplicate guest NIC/post-stress link marker")
    if not (lines.index(PASS) < lines.index(link[0].group(0))):
        raise ValueError("post-stress link marker preceded HTTP RX completion")
    if guest[0].group(1).upper() != link[0].group(1).upper():
        raise ValueError("post-stress NIC MAC differs from original E1000E")



def check_third_http(frames: list[bytes], guest: bytes, last_icmp: int,
                     prior_ports: tuple[int, int]) -> None:
    """Require complete third session strictly after the final ICMP reply."""
    entries = [(index, packet) for index, frame in enumerate(frames)
               if (packet := tcp_frame(frame))]
    start = next((i for i, (index, _) in enumerate(entries)
                  if index > last_icmp), len(entries))
    third = find_http_transaction([packet for _, packet in entries],
                                  guest, start, None)
    if third is None:
        raise ValueError("third HTTP exchange after post-burst ICMP incomplete")
    if third[0] in prior_ports:
        raise ValueError("third HTTP TCP source port reused earlier connection")


def validate(serial: str, pcap: bytes) -> str:
    validate_burst(serial, pcap)
    lines = [line.strip() for line in serial.replace("\r", "").splitlines()]
    if any("[NET3095-N6][FAIL]" in line for line in lines):
        raise ValueError("guest reported post-burst HTTP failure")
    if lines.count(START) != 1 or lines.count(PASS) != 1:
        raise ValueError("missing or duplicate post-burst HTTP markers")
    if not (lines.index(POST_PASS) < lines.index(START) < lines.index(PASS)):
        raise ValueError("post-burst HTTP markers out of order")
    check_link_markers(serial)
    leases = [m for line in lines if (m := LEASE.fullmatch(line))]
    if len(leases) != 1:
        raise ValueError("missing unique guest lease")
    guest = bytes(int(part) for part in leases[0].group(1).split("."))
    frames = list(pcap_frames(pcap))
    last_burst = check_frames(pcap, after_index=last_http_frame(serial, frames))
    check_post_burst_icmp(frames, last_burst, guest)
    events = [(i, packet) for i, frame in enumerate(frames)
              if (packet := post_burst_icmp(frame))]
    if len(events) != 8:
        raise ValueError("missing four ICMP pairs before third HTTP")
    last_icmp = events[-1][0]
    entries = [(index, packet) for index, frame in enumerate(frames)
               if (packet := tcp_frame(frame))]
    packets = [packet for _, packet in entries]
    first = find_http_transaction(packets, guest, 0, None)
    if first is None:
        raise ValueError("missing first HTTP session")
    second = find_http_transaction(packets, guest, first[1] + 1, first[0])
    if second is None:
        raise ValueError("missing second HTTP session")
    check_third_http(frames, guest, last_icmp, (first[0], second[0]))
    return ("[NET3095-N6-POST-HTTP-GATE] PASS third independent QEMU HTTP "
            "TCP RX/FIN-ACK after 128 TX frames and four ICMP replies; ISR UNPROVEN")


def self_test() -> None:
    guest = bytes([10, 0, 2, 15])
    port = 49155
    handshake = [
        fake_http(False, 0x02, 50000, 0, client_port=port),
        fake_http(True, 0x12, 60000, 50001, client_port=port),
        fake_http(False, 0x10, 50001, 60001, client_port=port),
        fake_http(False, 0x18, 50001, 60001, REQUEST, client_port=port),
    ]
    boundary = RESPONSE.index(b"\r\n\r\n") + 4
    cuts = [0, boundary]
    cuts.extend(range(boundary + 1024, len(RESPONSE), 1024))
    cuts.append(len(RESPONSE))
    response = [
        fake_http(True, 0x18, 60001 + start, 50001 + len(REQUEST),
                  RESPONSE[start:end], client_port=port)
        for start, end in zip(cuts, cuts[1:])
    ]
    teardown = [
        fake_http(True, 0x11, 60001 + len(RESPONSE),
                  50001 + len(REQUEST), client_port=port),
        fake_http(False, 0x10, 50001 + len(REQUEST),
                  60002 + len(RESPONSE), client_port=port),
    ]
    third = handshake + response + teardown
    frames = [bytes(60)] + third
    check_third_http(frames, guest, 0, (49153, 49154))
    serial = "\n".join((
        "[NET3095-N1] cpu=bsp adapter=0 name=e1000e mac=52:54:00:12:34:56 ready=yes link=up",
        PASS,
        "[NET3095-N6] POST_STRESS_LINK=PASS mac=52:54:00:12:34:56",
    ))
    check_link_markers(serial)
    for invalid in (
        serial.replace("POST_STRESS_LINK=PASS", "POST_STRESS_LINK=FAIL"),
        serial.replace("POST_STRESS_LINK=PASS mac=52:54:00:12:34:56",
                       "POST_STRESS_LINK=PASS mac=52:54:00:12:34:57"),
        serial + "\n[NET3095-N6] POST_STRESS_LINK=PASS mac=52:54:00:12:34:56",
        "\n".join(reversed(serial.splitlines())),
    ):
        try:
            check_link_markers(invalid)
        except ValueError:
            continue
        raise AssertionError("accepted invalid post-stress E1000E link marker")
    negatives = [
        frames[:-1],
        frames[:1] + handshake + response[:2] + teardown,
        frames[:1] + handshake + response + teardown[:1],
        frames[:1] + handshake + response[1:] + teardown,
    ]
    for bad in negatives:
        try:
            check_third_http(bad, guest, 0, (49153, 49154))
        except ValueError:
            continue
        raise AssertionError("accepted missing/corrupted third HTTP session")
    for bad_index, ports in ((len(frames) - 1, (49153, 49154)),
                             (0, (49155, 49154))):
        try:
            check_third_http(frames, guest, bad_index, ports)
        except ValueError:
            continue
        raise AssertionError("accepted early or reused third HTTP session")
    print("[NET3095-N6-POST-HTTP-CONTRACT] PASS synthetic third-session cases only")


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
            parser.error("real QEMU serial and PCAP required")
        try:
            print(validate(args.serial_log.read_text(errors="replace"),
                           args.pcap.read_bytes()))
        except (OSError, ValueError) as exc:
            parser.exit(1, f"[NET3095-N6-POST-HTTP-GATE][FAIL] {exc}\n")
