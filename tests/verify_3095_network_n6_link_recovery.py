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
SECOND_IDENT = 0x3099
SECOND_ARMED = "[NET3095-N6] LINK2_ARMED=YES"
SECOND_DOWN = "[NET3095-N6] LINK2_DOWN_OBSERVED=YES"
SECOND_UP = "[NET3095-N6] LINK2_UP_OBSERVED=YES"
SECOND_START = "[NET3095-N6] LINK2_RECOVERY_ICMP_ATTEMPT=START"
SECOND_PASS = "[NET3095-N6] LINK2_RECOVERY_ICMP=PASS from=10.0.2.2 seq=2"
SECOND_RTT = re.compile(r"\[NET3095-N6\] LINK2_RECOVERY_ICMP_RTT seq=2 elapsed_ms=([0-9]+)")
SECOND_RTT_PREFIX = "[NET3095-N6] LINK2_RECOVERY_ICMP_RTT"


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


def check_second_markers(serial: str) -> None:
    lines = [line.strip() for line in serial.replace("\r", "").splitlines()]
    required = (PASS, SECOND_ARMED, SECOND_DOWN, SECOND_UP, SECOND_START, SECOND_PASS)
    indices = []
    for marker in required:
        if lines.count(marker) != 1:
            raise ValueError("missing or duplicated second link marker: " + marker)
        indices.append(lines.index(marker))
    if indices != sorted(set(indices)):
        raise ValueError("second link-cycle markers out of order")
    samples = [(i, SECOND_RTT.fullmatch(line)) for i, line in enumerate(lines)
               if line.startswith(SECOND_RTT_PREFIX)]
    if len(samples) != 1 or samples[0][1] is None:
        raise ValueError("missing, malformed or duplicated second recovery RTT")
    if not indices[-2] < samples[0][0] < indices[-1]:
        raise ValueError("second recovery RTT outside ICMP attempt")
    if int(samples[0][1].group(1)) > 5000:
        raise ValueError("second recovery RTT exceeds receive timeout")


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


def check_second_link_pair(frames: list[bytes], guest: bytes) -> None:
    first = [(i, p) for i, f in enumerate(frames)
             if (p := final_echo(f, ident_expected=IDENT))]
    second = [(i, p) for i, f in enumerate(frames)
              if (p := final_echo(f, ident_expected=SECOND_IDENT, seq_expected=2))]
    if len(first) != 2 or len(second) != 2:
        raise ValueError("expected one independent ICMP pair per link cycle")
    (request_index, req), (reply_index, reply) = second
    if not first[-1][0] < request_index < reply_index:
        raise ValueError("second recovery ICMP was not after first recovery")
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
        raise ValueError("second recovery ICMP MAC, IP or payload mismatch")


def validate(serial: str, pcap: bytes, cycles: int = 1) -> str:
    validate_final(serial, pcap)
    check_markers(serial)
    leases = [m for line in serial.replace("\r", "").splitlines()
              if (m := LEASE.fullmatch(line.strip()))]
    if len(leases) != 1:
        raise ValueError("missing unique DHCP lease")
    guest = bytes(int(part) for part in leases[0].group(1).split("."))
    check_link_pair(list(pcap_frames(pcap)), guest)
    if cycles == 2:
        check_second_markers(serial)
        check_second_link_pair(list(pcap_frames(pcap)), guest)
    elif cycles != 1:
        raise ValueError("link cycle count must be 1 or 2")
    return ("[NET3095-N6-LINK-RECOVERY-GATE] PASS QMP-driven guest link down/up "
            "cycles=" + str(cycles) +
            " with independent post-up ICMP RX/TX in real QEMU PCAP; ISR UNPROVEN")


def fake_recovery(reply: bool, guest: bytes, ident: int = IDENT, sequence: int = 1) -> bytes:
    frame = bytearray(fake_final(reply, guest))
    offset = 14 + 20
    struct.pack_into(">HH", frame, offset + 4, ident, sequence)
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
    second_request = fake_recovery(False, guest, SECOND_IDENT, 2)
    second_reply = fake_recovery(True, guest, SECOND_IDENT, 2)
    second_frames = frames + [second_request, second_reply]
    check_second_link_pair(second_frames, guest)
    second_rtt = "[NET3095-N6] LINK2_RECOVERY_ICMP_RTT seq=2 elapsed_ms=21"
    second_serial = "\n".join((serial, SECOND_ARMED, SECOND_DOWN, SECOND_UP,
                                SECOND_START, second_rtt, SECOND_PASS))
    check_second_markers(second_serial)
    for invalid in (
        second_serial.replace(SECOND_DOWN, ""),
        second_serial.replace(SECOND_UP, ""),
        second_serial.replace(SECOND_DOWN + "\n" + SECOND_UP,
                              SECOND_UP + "\n" + SECOND_DOWN),
        second_serial + "\n" + SECOND_PASS,
        second_serial.replace(second_rtt, ""),
        second_serial.replace("elapsed_ms=21", "elapsed_ms=5001"),
        second_serial.replace("elapsed_ms=21", "elapsed_ms=bad"),
        second_serial.replace(SECOND_START + "\n" + second_rtt,
                              second_rtt + "\n" + SECOND_START),
    ):
        try:
            check_second_markers(invalid)
        except ValueError:
            continue
        raise AssertionError("accepted invalid second link-cycle serial markers")
    for invalid in (
        second_frames[:-1], second_frames + [second_reply],
        frames[:3] + [second_request, second_reply, request, reply],
        frames + [second_reply, second_request],
        frames + [second_request, second_reply[:-1] + bytes([second_reply[-1] ^ 1])],
    ):
        try:
            check_second_link_pair(invalid, guest)
        except ValueError:
            continue
        raise AssertionError("accepted invalid second link-cycle wire evidence")
    for invalid in (
        serial.replace(DOWN, ""), serial.replace(UP, ""),
        serial.replace(DOWN + "\n" + UP, UP + "\n" + DOWN),
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
    parser.add_argument("--cycles", type=int, choices=(1, 2), default=1)
    args = parser.parse_args()
    if args.self_test:
        self_test()
    else:
        if args.serial is None or args.pcap is None:
            parser.error("real QEMU serial and PCAP required")
        try:
            print(validate(args.serial.read_text(errors="replace"), args.pcap.read_bytes(), args.cycles))
        except (OSError, ValueError) as exc:
            parser.exit(1, f"[NET3095-N6-LINK-RECOVERY-GATE][FAIL] {exc}\n")
