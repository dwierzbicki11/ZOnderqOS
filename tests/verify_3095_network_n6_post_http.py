#!/usr/bin/env python3
"""Fail-closed QEMU N6 proof: third full TCP/HTTP RX after TX and ICMP stress.

Synthetic fixtures are validator unit tests only. Real proof requires the
same guest serial and filter-dump PCAP as the N6 QEMU smoke runner.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import re

from network_3095_http_host import PORT, REQUEST, RESPONSE, response_for_attempt
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

REPEATS = tuple(
    f"[NET3095-N6] POST_BURST_HTTP_REPEAT=PASS attempt={attempt} "
    f"from=10.0.2.2 port={PORT} bytes={len(RESPONSE)}"
    for attempt in range(4, 7)
)


def check_link_markers(serial: str) -> None:
    lines = [line.strip() for line in serial.replace("\r", "").splitlines()]
    guest = [match for line in lines if (match := GUEST.fullmatch(line))]
    link = [match for line in lines if (match := LINK.fullmatch(line))]
    if len(guest) != 1 or len(link) != 1:
        raise ValueError("missing or duplicate guest NIC/post-stress link marker")
    if any(lines.count(marker) != 1 for marker in REPEATS):
        raise ValueError("missing or duplicate post-burst repeat HTTP marker")
    markers = (PASS,) + REPEATS + (link[0].group(0),)
    if [lines.index(marker) for marker in markers] != sorted(lines.index(marker) for marker in markers):
        raise ValueError("post-stress link marker preceded complete repeated HTTP RX")
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
                                  guest, start, None, attempt=3)
    if third is None:
        raise ValueError("third HTTP exchange after post-burst ICMP incomplete")
    if third[0] in prior_ports:
        raise ValueError("third HTTP TCP source port reused earlier connection")


def check_followup_http(frames: list[bytes], guest: bytes, last_icmp: int,
                        prior_ports: tuple[int, ...]) -> None:
    """Prove four unique complete post-burst TCP streams in PCAP order.

    A guest serial PASS alone cannot satisfy this check. Each session must
    contain the exact response, TCP handshake, segmented RX and peer FIN/ACK.
    """
    entries = [(index, packet) for index, frame in enumerate(frames)
               if (packet := tcp_frame(frame))]
    packets = [packet for _, packet in entries]
    used_ports = set(prior_ports)
    last_frame = last_icmp
    for attempt in range(3, 7):
        start = next((i for i, (index, _) in enumerate(entries)
                      if index > last_frame), len(entries))
        transaction = find_http_transaction(packets, guest, start, None, attempt=attempt)
        if transaction is None:
            raise ValueError(f"post-burst HTTP session {attempt} incomplete")
        port, finish = transaction
        if port in used_ports:
            raise ValueError(f"post-burst HTTP session {attempt} reused a TCP source port")
        used_ports.add(port)
        last_frame = entries[finish][0]


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
    second = find_http_transaction(packets, guest, first[1] + 1, first[0], attempt=2)
    if second is None:
        raise ValueError("missing second HTTP session")
    check_followup_http(frames, guest, last_icmp, (first[0], second[0]))
    return ("[NET3095-N6-POST-HTTP-GATE] PASS four independent QEMU HTTP "
            "TCP RX/FIN-ACK sessions after 128 TX frames and four ICMP replies; ISR UNPROVEN")


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
                  response_for_attempt(3)[start:end], client_port=port)
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
    # Detect a stale/replayed first-session DMA payload after the TX burst.
    stale_third = ([bytes(60)] + handshake +
                   [fake_http(True, 0x18, 60001 + start,
                              50001 + len(REQUEST), RESPONSE[start:end],
                              client_port=port)
                    for start, end in zip(cuts, cuts[1:])] + teardown)
    try:
        check_third_http(stale_third, guest, 0, (49153, 49154))
    except ValueError:
        pass
    else:
        raise AssertionError("accepted stale first-session RX bytes after DMA stress")
    # Reject stale data in each body descriptor, including blocks 2-4.
    # Replayed data may affect only one segment, not the whole response.
    for block in range(1, 5):
        start, end = cuts[block], cuts[block + 1]
        stale = list(frames)
        stale[1 + len(handshake) + block] = fake_http(
            True, 0x18, 60001 + start, 50001 + len(REQUEST),
            RESPONSE[start:end], client_port=port)
        try:
            check_third_http(stale, guest, 0, (49153, 49154))
        except ValueError:
            continue
        raise AssertionError(f"accepted stale third HTTP RX block {block}")
    serial = "\n".join((
        "[NET3095-N1] cpu=bsp adapter=0 name=e1000e mac=52:54:00:12:34:56 ready=yes link=up",
        PASS,
        *REPEATS,
        "[NET3095-N6] POST_STRESS_LINK=PASS mac=52:54:00:12:34:56",
    ))
    check_link_markers(serial)
    for invalid in (
        serial.replace("POST_STRESS_LINK=PASS", "POST_STRESS_LINK=FAIL"),
        serial.replace("POST_STRESS_LINK=PASS mac=52:54:00:12:34:56",
                       "POST_STRESS_LINK=PASS mac=52:54:00:12:34:57"),
        serial + "\n[NET3095-N6] POST_STRESS_LINK=PASS mac=52:54:00:12:34:56",
        serial.replace(REPEATS[0], REPEATS[1]),
        serial.replace(REPEATS[1], ""),
        serial.replace(REPEATS[2], REPEATS[0]),
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

    # Three more complete HTTP sessions after the third, with distinct ports
    # and TCP sequence spaces. Every segment stays below the Ethernet MTU.
    extended = list(frames)
    for attempt in range(4, 7):
        port = 49152 + attempt
        client_seq = 10000 * attempt
        server_seq = 10000 * (attempt + 1)
        extended.extend([
            fake_http(False, 0x02, client_seq, 0, client_port=port),
            fake_http(True, 0x12, server_seq, client_seq + 1, client_port=port),
            fake_http(False, 0x10, client_seq + 1, server_seq + 1, client_port=port),
            fake_http(False, 0x18, client_seq + 1, server_seq + 1,
                      REQUEST, client_port=port),
        ])
        extended.extend(
            fake_http(True, 0x18, server_seq + 1 + start,
                      client_seq + 1 + len(REQUEST), response_for_attempt(attempt)[start:end],
                      client_port=port)
            for start, end in zip(cuts, cuts[1:])
        )
        extended.extend([
            fake_http(True, 0x11, server_seq + 1 + len(RESPONSE),
                      client_seq + 1 + len(REQUEST), client_port=port),
            fake_http(False, 0x10, client_seq + 1 + len(REQUEST),
                      server_seq + 2 + len(RESPONSE), client_port=port),
        ])
    check_followup_http(extended, guest, 0, (49153, 49154))
    # Every body segment in all three later sessions must reject stale
    # first-session RX data, even when TCP sequence/ACK and FIN are valid.
    session_frames = 4 + (len(cuts) - 1) + 2  # handshake/request, response, FIN/ACK
    for attempt in range(4, 7):
        client_port = 49152 + attempt
        client_seq = 10000 * attempt + 1
        server_seq = 10000 * (attempt + 1) + 1
        response_base = len(frames) + (attempt - 4) * session_frames + 4
        for block in range(1, 5):
            start, end = cuts[block], cuts[block + 1]
            stale = list(extended)
            stale[response_base + block] = fake_http(
                True, 0x18, server_seq + start,
                client_seq + len(REQUEST), RESPONSE[start:end],
                client_port=client_port)
            try:
                check_followup_http(stale, guest, 0, (49153, 49154))
            except ValueError:
                continue
            raise AssertionError(
                f"accepted stale post-burst HTTP block {block} in attempt {attempt}")
    for bad in (extended[:-1], extended[:len(frames)],
                extended[:len(frames)] + extended[len(frames) + 1:]):
        try:
            check_followup_http(bad, guest, 0, (49153, 49154))
        except ValueError:
            continue
        raise AssertionError("accepted incomplete post-burst HTTP repetitions")
    try:
        check_followup_http(extended, guest, 0, (49153, 49154, 49158))
    except ValueError:
        pass
    else:
        raise AssertionError("accepted reused post-burst TCP source port")
    print("[NET3095-N6-POST-HTTP-CONTRACT] PASS synthetic four-session cases only")


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
