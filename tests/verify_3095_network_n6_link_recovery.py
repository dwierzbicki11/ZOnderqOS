#!/usr/bin/env python3
"""N6 opt-in QMP link-flap proof: guest down/up observations and real post-up ICMP.

The QMP command acknowledgment is checked by the host harness. This verifier
requires the original N6 wire proof and a NEW ordered ICMP request/reply after
the guest observed the link returning. No physical NIC or ISR claim is made.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import re
import struct

from verify_3095_network_n1 import pcap_frames
from verify_3095_network_n2 import LEASE, MAC, SERVER, synthetic_capture
from verify_3095_network_n3 import internet_checksum
from verify_3095_network_n6_final_icmp import (
    final_echo, fake_final, validate as validate_final,
)

IDENT = 0x3098
ARMED = "[NET3095-N6] LINK_CYCLE_ARMED=YES"
DOWN = "[NET3095-N6] LINK_DOWN_OBSERVED=YES"
UP = "[NET3095-N6] LINK_UP_OBSERVED=YES"
START = "[NET3095-N6] LINK_RECOVERY_ICMP_ATTEMPT=START"
PASS = "[NET3095-N6] LINK_RECOVERY_ICMP=PASS from=10.0.2.2 seq=1"
RTT = re.compile(r"\[NET3095-N6\] LINK_RECOVERY_ICMP_RTT seq=1 elapsed_ms=([0-9]+)")
RTT_PREFIX = "[NET3095-N6] LINK_RECOVERY_ICMP_RTT"


def check_markers(serial: str) -> None:
    lines = [line.strip() for line in serial.replace("\r", "").splitlines()]
    if any("[NET3095-N6][FAIL]" in line for line in lines):
        raise ValueError("guest reported N6 failure")
    required = (ARMED, DOWN, UP, START, PASS)
    indices = []
    for marker in required:
        if lines.count(marker) != 1:
            raise ValueError("missing or duplicated link recovery marker: " + marker)
        indices.append(lines.index(marker))
    if indices != sorted(indices) or len(set(indices)) != len(indices):
        raise ValueError("link cycle markers are out of order")
    samples = [(i, RTT.fullmatch(line)) for i, line in enumerate(lines)
               if line.startswith(RTT_PREFIX)]
    if len(samples) != 1 or samples[0][1] is None:
        raise ValueError("missing, malformed or duplicated recovery RTT")
    if not indices[-2] < samples[0][0] < indices[-1]:
        raise ValueError("recovery RTT outside ICMP attempt")
    if int(samples[0][1].group(1)) > 5000:
        raise ValueError("recovery RTT exceeds receive timeout")


def check_link_pair(frames: list[bytes], guest: bytes) -> None:
    final_events = [(i, p) for i, f in enumerate(frames)
                    if (p := final_echo(f))]
    if len(final_events) != 2 or final_events[0][1]["type"] != 8 or final_events[1][1]["type"] != 0:
        raise ValueError("missing original final ICMP pair")
    recovery = [(i, p) for i, f in enumerate(frames)
                if (p := final_echo(f, ident_expected=IDENT))]
    if len(recovery) != 2:
        raise ValueError("expected exactly one recovery ICMP request/reply pair")
    (request_index, req), (reply_index, reply) = recovery
    if not final_events[-1][0] < request_index < reply_index:
        raise ValueError("recovery ICMP was not after the original N6 proof")
    if not (
        req["type"] == 8 and reply["type"] == 0 and
        req["src"] == guest and req["dst"] == SERVER and
        reply["src"] == SERVER and reply["dst"] == guest and
        req["src_mac"] == MAC and reply["dst_mac"] == MAC and
        req["dst_mac"] == reply["src_mac"] and
        req["dst_mac"] != MAC and req["dst_mac"] != b"\x00" * 6 and
        not (req["dst_mac"][0] & 1) and
        len(req["payload"]) == 32 and req["payload"] == reply["payload"]
    ):
        raise ValueError("recovery ICMP MAC, IP or payload mismatch")


def validate(serial: str, pcap: bytes) -> str:
    validate_final(serial, pcap)
    check_markers(serial)
    leases = [m for line in serial.replace("\r", "").splitlines()
              if (m := LEASE.fullmatch(line.strip()))]
    if len(leases) != 1:
        raise ValueError("missing unique DHCP lease")
    guest = bytes(int(part) for part in leases[0].group(1).split("."))
    check_link_pair(list(pcap_frames(pcap)), guest)
    return ("[NET3095-N6-LINK-RECOVERY-GATE] PASS QMP-driven guest link down/up "
            "and fresh post-up ICMP RX/TX in real QEMU PCAP; ISR UNPROVEN")


def fake_recovery(reply: bool, guest: bytes) -> bytes:
    frame = bytearray(fake_final(reply, guest))
    offset = 14 + 20
    struct.pack_into(">H", frame, offset + 4, IDENT)
    frame[offset + 2:offset + 4] = b"\x00\x00"
    frame[offset + 2:offset + 4] = internet_checksum(frame[offset:]).to_bytes(2, "big")
    return bytes(frame)


def self_test() -> None:
    guest = bytes([10, 0, 2, 15])
    final_request, final_reply = fake_final(False, guest), fake_final(True, guest)
    request, reply = fake_recovery(False, guest), fake_recovery(True, guest)
    frames = [bytes(60), final_request, final_reply, request, reply]
    check_link_pair(frames, guest)
    assert len(list(pcap_frames(synthetic_capture(frames)))) == 5
    rtt = "[NET3095-N6] LINK_RECOVERY_ICMP_RTT seq=1 elapsed_ms=19"
    serial = "\n".join((ARMED, DOWN, UP, START, rtt, PASS))
    check_markers(serial)
    for invalid in (
        serial.replace(DOWN, ""), serial.replace(UP, ""),
        serial.replace(DOWN, UP).replace(UP + "\n" + UP, DOWN + "\n" + UP),
        serial + "\n" + PASS,
        serial.replace(rtt, ""), serial + "\n" + rtt,
        serial.replace("elapsed_ms=19", "elapsed_ms=5001"),
        serial.replace("elapsed_ms=19", "elapsed_ms=bad"),
        serial.replace(START + "\n" + rtt, rtt + "\n" + START),
        serial + "\n[NET3095-N6][FAIL] unexpected",
    ):
        try:
            check_markers(invalid)
        except ValueError:
            continue
        raise AssertionError("accepted invalid link-cycle serial markers")
    for invalid in (
        frames[:-1], frames[:-2], frames + [reply],
        [bytes(60), request, reply, final_request, final_reply],
        [bytes(60), final_request, final_reply, reply, request],
        [bytes(60), final_request, final_reply, request, reply[:-1] + bytes([reply[-1] ^ 1])],
    ):
        try:
            check_link_pair(invalid, guest)
        except ValueError:
            continue
        raise AssertionError("accepted invalid link-cycle wire evidence")
    print("[NET3095-N6-LINK-RECOVERY-CONTRACT] PASS synthetic positive/negative only")


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
            parser.error("real QEMU serial and PCAP required")
        try:
            print(validate(args.serial.read_text(errors="replace"), args.pcap.read_bytes()))
        except (OSError, ValueError) as exc:
            parser.exit(1, f"[NET3095-N6-LINK-RECOVERY-GATE][FAIL] {exc}\n")
