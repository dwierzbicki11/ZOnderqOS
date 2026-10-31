#!/usr/bin/env python3
"""N6-only sustained raw E1000E TX proof from QEMU PCAP, not serial alone.

The guest queues 128 paced, uniquely tagged Ethernet frames after two HTTP
sessions. The PCAP must contain every frame in order. This establishes real
QEMU TX and repeated descriptor submission, NOT guest IRQ ISR execution,
physical RTL810xE operation, or production networking.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import struct

from verify_3095_network_n1 import BROADCAST, ETHER_TYPE, pcap_frames
from verify_3095_network_n2 import LEASE, MAC, SERVER, synthetic_capture
from verify_3095_network_n3 import internet_checksum
from verify_3095_network_n4 import tcp_frame
from verify_3095_network_n6 import (
    HTTP_REPEAT_PASS, find_http_transaction, validate as validate_n6,
)

COUNT = 128
START = "[NET3095-N6] TX_BURST_ATTEMPT=START count=128"
QUEUED = "[NET3095-N6] TX_BURST=QUEUED count=128"
TAG = b"ZONDERQ3095-BURST-"
POST_START = "[NET3095-N6] POST_BURST_ICMP_ATTEMPT=START"
POST_PASS = "[NET3095-N6] POST_BURST_ICMP=PASS from=10.0.2.2"


def check_markers(serial: str) -> None:
    lines = [line.strip() for line in serial.replace("\r", "").splitlines()]
    if any("[NET3095-N6][FAIL]" in line for line in lines):
        raise ValueError("guest reported TX burst, HTTP or post-burst ICMP failure")
    markers = (HTTP_REPEAT_PASS, START, QUEUED, POST_START, POST_PASS)
    if any(lines.count(marker) != 1 for marker in markers):
        raise ValueError("missing or duplicate N6 burst/ICMP markers")
    if [lines.index(marker) for marker in markers] != sorted(lines.index(marker) for marker in markers):
        raise ValueError("N6 TX burst or post-burst ICMP markers out of order")

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


def check_frames(pcap: bytes, after_index: int = -1) -> int:
    """Require exactly 128 unmodified, in-order guest TX frames after HTTP.

    Subsequence matching could silently ignore duplicates, reordered frames,
    wrong source MAC, nonzero padding or an unexpected extra submission.
    """
    frames = list(pcap_frames(pcap))
    seen = 0
    last_index = -1
    for index, frame in enumerate(frames):
        if len(frame) < 14:
            continue
        payload = frame[14:]
        # EtherType 0x88B5 is reserved for this isolated diagnostic.
        # Previously a forged/malformed extra burst could be hidden by
        # corrupting one tag byte: the validator silently skipped it and
        # accepted the other 128 valid frames. Fail closed instead.
        if frame[12:14] != ETHER_TYPE and not payload.startswith(TAG):
            continue
        if frame[12:14] == ETHER_TYPE and payload.startswith(b"ZONDERQ3095TX"):
            # The initial N1 raw TX is expected, but an altered copy is not.
            if (frame[:6] != BROADCAST or frame[6:12] != MAC or
                    len(frame) != 60 or any(payload[len(b"ZONDERQ3095TX"):])):
                raise ValueError("malformed initial N1 raw Ethernet frame")
            continue
        if index <= after_index:
            raise ValueError("E1000E TX burst preceded the completed HTTP exchange")
        if seen >= COUNT:
            raise ValueError("unexpected extra E1000E TX burst frame")
        expected = (BROADCAST + MAC + ETHER_TYPE + TAG +
                    f"{seen:03d}".encode("ascii"))
        if (len(frame) != 60 or not frame.startswith(expected) or
                any(frame[len(expected):])):
            raise ValueError(
                f"missing, reordered, malformed or forged E1000E TX burst frame {seen:03d}")
        seen += 1
        last_index = index
    if seen != COUNT:
        raise ValueError(f"incomplete E1000E TX burst: {seen}/{COUNT} frames")
    return last_index


def post_burst_icmp(frame: bytes):
    """Decode N6 echo packets; reject bad wire checksums, not just wrong IDs."""
    if (len(frame) < 14 + 20 + 8 or frame[12:14] != b"\x08\x00" or
            frame[14] >> 4 != 4 or frame[23] != 1):
        return None
    ihl = (frame[14] & 15) * 4
    if ihl < 20 or len(frame) < 14 + ihl + 8:
        return None
    length = int.from_bytes(frame[16:18], "big")
    if (int.from_bytes(frame[20:22], "big") & 0x3FFF or
            length < ihl + 8 or len(frame) < 14 + length):
        return None
    typ, code, _checksum, ident, seq = struct.unpack_from(">BBHHH", frame, 14 + ihl)
    if typ not in (0, 8) or code != 0 or ident != 0x3096 or seq != 2:
        return None
    # Real QEMU N6 capture has valid IPv4 and ICMP checksums. An echo
    # with a matching ID but damaged wire bytes must fail closed even
    # when an additional valid echo pair appears later in the capture.
    if (frame[22] == 0 or
            internet_checksum(frame[14:14 + ihl]) != 0 or
            internet_checksum(frame[14 + ihl:14 + length]) != 0):
        raise ValueError("invalid post-burst IPv4/ICMP checksum or TTL")
    return {
        "type": typ, "src": frame[26:30], "dst": frame[30:34],
        "src_mac": frame[6:12], "dst_mac": frame[:6],
        "payload": frame[14 + ihl + 8:14 + length],
    }


def check_post_burst_icmp(frames: list[bytes], after_index: int, guest: bytes) -> None:
    """Require one correlated request and inbound reply AFTER the final TX frame."""
    events = [(i, p) for i, frame in enumerate(frames)
              if (p := post_burst_icmp(frame))]
    if len(events) != 2:
        raise ValueError("expected exactly one post-burst ICMP request and reply")
    (request_index, request), (reply_index, reply) = events
    if not (after_index < request_index < reply_index):
        raise ValueError("post-burst ICMP not after completed TX ring stress")
    if not (
        request["type"] == 8 and request["src"] == guest and
        request["dst"] == SERVER and request["src_mac"] == MAC and
        request["dst_mac"] == reply["src_mac"] and
        reply["type"] == 0 and reply["src"] == SERVER and
        reply["dst"] == guest and reply["dst_mac"] == MAC and
        reply["src_mac"] != MAC and reply["src_mac"] != b"\x00" * 6 and
        not (reply["src_mac"][0] & 1) and
        len(request["payload"]) == 32 and
        request["payload"] == reply["payload"]
    ):
        raise ValueError("post-burst ICMP Ethernet/IP peer mismatch")

def validate(serial: str, pcap: bytes) -> str:
    # N6 first proves two complete TCP HTTP sessions on real QEMU frames.
    validate_n6(serial, pcap)
    check_markers(serial)
    frames = list(pcap_frames(pcap))
    last_burst = check_frames(pcap, after_index=last_http_frame(serial, frames))
    leases = [m for line in serial.replace("\r", "").splitlines()
              if (m := LEASE.fullmatch(line.strip()))]
    if len(leases) != 1:
        raise ValueError("missing unique guest lease for post-burst ICMP")
    guest = bytes(int(part) for part in leases[0].group(1).split("."))
    check_post_burst_icmp(frames, last_burst, guest)
    return ("[NET3095-N6-BURST-GATE] PASS 128 exact ordered QEMU PCAP raw TX "
            "frames followed by correlated ICMP RX/TX; DMA ISR execution UNPROVEN")

def self_test() -> None:
    serial = "\n".join([HTTP_REPEAT_PASS, START, QUEUED, POST_START, POST_PASS])
    frames = []
    for sequence in range(COUNT):
        raw = BROADCAST + MAC + ETHER_TYPE + TAG + f"{sequence:03d}".encode("ascii")
        frames.append(raw + bytes(60 - len(raw)))
    check_markers(serial)
    assert check_frames(synthetic_capture(frames)) == COUNT - 1
    # The same capture also contains the earlier N1 broadcast frame and
    # unrelated IPv4/ARP traffic; neither may count as a burst submission.
    n1_payload = b"ZONDERQ3095TX"
    n1_frame = (BROADCAST + MAC + ETHER_TYPE + n1_payload +
                bytes(60 - 14 - len(n1_payload)))
    unrelated = BROADCAST + MAC + b"\x08\x06" + bytes(46)
    assert check_frames(synthetic_capture([n1_frame, unrelated] + frames)) == COUNT + 1
    for malformed_n1 in (
        n1_frame[:6] + bytes.fromhex("020000000099") + n1_frame[12:],
        n1_frame[:-1] + b"X",
    ):
        try:
            check_frames(synthetic_capture([malformed_n1] + frames))
        except ValueError:
            continue
        raise AssertionError("accepted malformed N1 frame alongside valid burst")

    # Synthetic ICMP fixture: use the N3 frame builder, changing only the
    # distinct N6 ID. No fixture is claimed to be real guest RX proof.
    from verify_3095_network_n3 import fake_icmp
    guest = bytes([10, 0, 2, 15])
    def echo_fixture(is_reply: bool) -> bytes:
        packet = bytearray(fake_icmp(is_reply, MAC, guest, seq=2))
        # A real Cosmos SendEcho transmits a 32-byte challenge payload.
        # Build valid IPv4 and ICMP checksums rather than accepting
        # synthetic zero-checksum headers as wire evidence.
        packet[16:18] = (60).to_bytes(2, "big")
        packet[38:40] = (0x3096).to_bytes(2, "big")
        packet[24:26] = b"\x00\x00"
        packet[36:38] = b"\x00\x00"
        packet[24:26] = internet_checksum(packet[14:34]).to_bytes(2, "big")
        packet[36:38] = internet_checksum(packet[34:]).to_bytes(2, "big")
        return bytes(packet)

    request, reply = echo_fixture(False), echo_fixture(True)
    assert internet_checksum(request[14:34]) == 0
    assert internet_checksum(reply[34:]) == 0
    check_post_burst_icmp(frames + [request, reply], COUNT - 1, guest)
    for invalid in (
        [request], [reply, request], [reply], [request, reply, reply],
        [request, request, reply],
        [request, reply[:-2] + b"\x00\x01"],
        [request, reply[:6] + MAC + reply[12:]],
        [request[:6] + b"\x00" * 6 + request[12:], reply],
        [request, reply[:24] + bytes([reply[24] ^ 1]) + reply[25:]],
        [request, reply[:36] + bytes([reply[36] ^ 1]) + reply[37:]],
        [request, reply[:45] + bytes([reply[45] ^ 1]) + reply[46:]],
        [request, reply[:22] + b"\x00" + reply[23:]],
        [request, reply[:16] + (59).to_bytes(2, "big") + reply[18:]],
        [request, reply[:20] + b"\x20" + reply[21:]],
        [request, reply, reply[:36] + bytes([reply[36] ^ 1]) + reply[37:]],
    ):
        try:
            check_post_burst_icmp(frames + invalid, COUNT - 1, guest)
        except ValueError:
            continue
        raise AssertionError("accepted invalid post-burst ICMP capture")
    try:
        check_post_burst_icmp(frames + [request, reply], COUNT, guest)
    except ValueError:
        pass
    else:
        raise AssertionError("accepted ICMP before final TX frame")
    try:
        check_frames(synthetic_capture(frames), after_index=0)
    except ValueError:
        pass
    else:
        raise AssertionError("accepted burst before HTTP completion")
    bad_serial = [
        serial.replace(START, ""),
        serial.replace(QUEUED, ""),
        serial.replace(POST_START, ""),
        serial.replace(POST_PASS, ""),
        serial.replace(HTTP_REPEAT_PASS, ""),
        serial + "\n" + QUEUED,
        serial + "\n" + POST_PASS,
        serial.replace(START + "\n" + QUEUED, QUEUED + "\n" + START),
        serial.replace(POST_START + "\n" + POST_PASS, POST_PASS + "\n" + POST_START),
        serial + "\n[NET3095-N6][FAIL] queue rejected",
    ]
    for item in bad_serial:
        try:
            check_markers(item)
        except ValueError:
            continue
        raise AssertionError("accepted invalid N6 burst/ICMP serial evidence")
    wrong_mac = bytes.fromhex("020000000099")
    negative_frames = [
        frames[:-1],
        frames[:12] + frames[13:],
        frames + [frames[-1]],
        frames[:8] + [frames[8], frames[8]] + frames[9:],
        frames[:12] + [frames[13], frames[12]] + frames[14:],
        frames[:8] + [frames[8][:-1] + b"X"] + frames[9:],
        frames[:8] + [frames[8] + b"\x00"] + frames[9:],
        frames[:8] + [frames[8][:6] + wrong_mac + frames[8][12:]] + frames[9:],
        frames[:8] + [frames[8][:12] + b"\x08\x00" + frames[8][14:]] + frames[9:],
        frames[:8] + [frames[8][:14] + TAG + b"999" +
                       frames[8][14 + len(TAG) + 3:]] + frames[9:],
        # Reject decimal rollover confusion between 099 and 100.
        frames[:100] + [frames[100][:14] + TAG + b"099" +
                        frames[100][14 + len(TAG) + 3:]] + frames[101:],
    ]
    for items in negative_frames:
        try:
            check_frames(synthetic_capture(items))
        except ValueError:
            continue
        raise AssertionError("accepted missing, duplicate, reordered or forged raw TX burst frame")
    # Extra damaged frames must fail even when all 128 genuine frames are
    # present. A subsequence-only validator used to silently ignore them.
    extra_bad_frames = [
        frames[8][:14] + b"ZONDERQ3095-BURST-X" + frames[8][14 + len(TAG) + 3:],
        frames[8][:12] + b"\x08\x00" + frames[8][14:],
        frames[8][:6] + wrong_mac + frames[8][12:],
        frames[8][:14] + TAG + b"999" + frames[8][14 + len(TAG) + 3:],
    ]
    for extra in extra_bad_frames:
        try:
            check_frames(synthetic_capture(frames + [extra]))
        except ValueError:
            continue
        raise AssertionError("accepted an extra malformed diagnostic Ethernet frame")
    try:
        check_frames(synthetic_capture(frames)[:-1])
    except ValueError:
        pass
    else:
        raise AssertionError("accepted truncated PCAP")
    print("[NET3095-N6-BURST-CONTRACT] PASS synthetic TX and post-burst ICMP checksum/payload cases only")

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
