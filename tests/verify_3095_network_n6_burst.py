#!/usr/bin/env python3
"""N6-only sustained raw E1000E TX proof from QEMU PCAP, not serial alone.

The guest queues 32 paced, uniquely tagged Ethernet frames after two HTTP
sessions. The PCAP must contain every frame in order. This establishes real
QEMU TX and repeated descriptor submission, NOT guest IRQ ISR execution,
physical RTL810xE operation, or production networking.
"""
from __future__ import annotations

import argparse
from pathlib import Path

from verify_3095_network_n1 import BROADCAST, ETHER_TYPE, pcap_frames
from verify_3095_network_n2 import LEASE, MAC, synthetic_capture
from verify_3095_network_n4 import tcp_frame
from verify_3095_network_n6 import (
    HTTP_REPEAT_PASS, find_http_transaction, validate as validate_n6,
)

COUNT = 32
START = "[NET3095-N6] TX_BURST_ATTEMPT=START count=32"
QUEUED = "[NET3095-N6] TX_BURST=QUEUED count=32"
TAG = b"ZONDERQ3095-BURST-"


def check_markers(serial: str) -> None:
    lines = [line.strip() for line in serial.replace("\r", "").splitlines()]
    if any("[NET3095-N6][FAIL]" in line for line in lines):
        raise ValueError("guest reported TX burst or HTTP failure")
    if (lines.count(HTTP_REPEAT_PASS) != 1 or lines.count(START) != 1 or
            lines.count(QUEUED) != 1):
        raise ValueError("missing or duplicate N6 TX burst markers")
    if not (lines.index(HTTP_REPEAT_PASS) < lines.index(START) < lines.index(QUEUED)):
        raise ValueError("N6 TX burst markers out of order")


def last_http_frame(serial: str, frames: list[bytes]) -> int:
    """Locate the second HTTP FIN acknowledgment in the original PCAP order."""
    lines = [line.strip() for line in serial.replace("\r", "").splitlines()]
    leases = [m for line in lines if (m := LEASE.fullmatch(line))]
    if len(leases) != 1:
        raise ValueError("missing unique N6 guest lease")
    guest = bytes(int(part) for part in leases[0].group(1).split("."))
    tcp_entries = [(i, p) for i, frame in enumerate(frames)
                   if (p := tcp_frame(frame))]
    packets = [p for _, p in tcp_entries]
    first = find_http_transaction(packets, guest, 0, None)
    if first is None:
        raise ValueError("missing first HTTP exchange before raw TX burst")
    second = find_http_transaction(packets, guest, first[1] + 1, first[0])
    if second is None:
        raise ValueError("missing second HTTP exchange before raw TX burst")
    return tcp_entries[second[1]][0]


def check_frames(pcap: bytes, after_index: int = -1) -> None:
    """Require exactly 32 unmodified, in-order guest TX frames after HTTP.

    A subsequence match is insufficient: it could silently ignore a duplicate,
    reordered frame, wrong source MAC, nonzero padding or a 33rd submission.
    Scan all candidate burst frames, not only valid Ethernet headers, so a
    corrupted header cannot be hidden by a later valid retransmission.
    """
    frames = list(pcap_frames(pcap))
    seen = 0
    for index, frame in enumerate(frames):
        if len(frame) < 14 or not frame[14:].startswith(TAG):
            continue
        if index <= after_index:
            raise ValueError("E1000E TX burst preceded the completed HTTP exchange")
        if seen >= COUNT:
            raise ValueError("unexpected extra E1000E TX burst frame")
        expected = (BROADCAST + MAC + ETHER_TYPE + TAG +
                    f"{seen:02d}".encode("ascii"))
        if (len(frame) != 60 or not frame.startswith(expected) or
                any(frame[len(expected):])):
            raise ValueError(
                f"missing, reordered, malformed or forged E1000E TX burst frame {seen:02d}")
        seen += 1
    if seen != COUNT:
        raise ValueError(f"incomplete E1000E TX burst: {seen}/{COUNT} frames")


def validate(serial: str, pcap: bytes) -> str:
    # The independent N6 validator first proves both real TCP HTTP sessions.
    validate_n6(serial, pcap)
    check_markers(serial)
    # A burst preceding the HTTP FIN/ACK is not the post-RX TX stress test.
    frames = list(pcap_frames(pcap))
    check_frames(pcap, after_index=last_http_frame(serial, frames))
    return ("[NET3095-N6-BURST-GATE] PASS 32 ordered QEMU PCAP raw TX frames "
            "after two HTTP sessions; DMA ISR execution UNPROVEN")


def self_test() -> None:
    serial = "\n".join([HTTP_REPEAT_PASS, START, QUEUED])
    frames = []
    for sequence in range(COUNT):
        raw = BROADCAST + MAC + ETHER_TYPE + TAG + f"{sequence:02d}".encode("ascii")
        frames.append(raw + bytes(60 - len(raw)))
    check_markers(serial)
    check_frames(synthetic_capture(frames))
    try:
        check_frames(synthetic_capture(frames), after_index=0)
    except ValueError:
        pass
    else:
        raise AssertionError("accepted burst that began before required prior traffic")
    bad_serial = [
        serial.replace(START, ""),
        serial.replace(QUEUED, ""),
        serial.replace(HTTP_REPEAT_PASS, ""),
        serial + "\n" + QUEUED,
        serial.replace(START + "\n" + QUEUED, QUEUED + "\n" + START),
        serial + "\n[NET3095-N6][FAIL] queue rejected",
    ]
    for item in bad_serial:
        try:
            check_markers(item)
        except ValueError:
            continue
        raise AssertionError("accepted invalid N6 burst serial evidence")
    wrong_mac = bytes.fromhex("020000000099")
    negative_frames = [
        frames[:-1],
        frames[:12] + frames[13:],
        frames + [frames[-1]],  # Duplicate/33rd descriptor is not silently ignored.
        frames[:8] + [frames[8], frames[8]] + frames[9:],
        frames[:12] + [frames[13], frames[12]] + frames[14:],
        frames[:8] + [frames[8][:-1] + b"X"] + frames[9:],
        frames[:8] + [frames[8] + b"\x00"] + frames[9:],
        frames[:8] + [frames[8][:6] + wrong_mac + frames[8][12:]] + frames[9:],
        frames[:8] + [frames[8][:12] + b"\x08\x00" + frames[8][14:]] + frames[9:],
        frames[:8] + [frames[8][:14] + TAG + b"99" + frames[8][14 + len(TAG) + 2:]] + frames[9:],
    ]
    for items in negative_frames:
        try:
            check_frames(synthetic_capture(items))
        except ValueError:
            continue
        raise AssertionError("accepted missing, reordered or forged raw TX burst frame")
    try:
        check_frames(synthetic_capture(frames)[:-1])
    except ValueError:
        pass
    else:
        raise AssertionError("accepted truncated PCAP")
    print("[NET3095-N6-BURST-CONTRACT] PASS synthetic ordered TX burst/negative cases only")


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
            parser.exit(1, f"[NET3095-N6-BURST-GATE][FAIL] {exc}\n")
