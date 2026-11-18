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
    if len(frame) < 14 or frame[12:14] != b"\x08\x00":
        return None
    # A captured IPv4/TCP packet with a truncated fixed header is corrupt
    # evidence, not an unrelated frame that can be silently skipped.
    if len(frame) < 14 + 20:
        raise ValueError("truncated IPv4 header")
    if frame[14] >> 4 != 4:
        raise ValueError("invalid IPv4 version")
    if frame[23] != 6:
        return None
    ihl = (frame[14] & 15) * 4
    if ihl < 20 or len(frame) < 14 + ihl + 20:
        raise ValueError("truncated IPv4/TCP header")
    length = int.from_bytes(frame[16:18], "big")
    if (int.from_bytes(frame[20:22], "big") & 0xBFFF) or length < ihl + 20:
        raise ValueError("invalid IPv4/TCP length or fragmentation")
    if len(frame) < 14 + length:
        raise ValueError("truncated IPv4/TCP payload")
    offset = 14 + ihl
    thl = (frame[offset + 12] >> 4) * 4
    if thl < 20 or length < ihl + thl:
        # A malformed TCP data offset is invalid wire evidence, not a
        # non-TCP frame to silently ignore. A later valid response must
        # never rescue a damaged segment in the same capture.
        raise ValueError("invalid TCP data offset")
    # RFC 9293 reserves TCP header byte 12 bits 1..3; NS (bit 0)
    # is separate. Reject impossible reserved-bit wire evidence.
    if frame[offset + 12] & 0x0F:
        raise ValueError("TCP reserved or NS header bits set")
    # Fail closed on damaged wire data even if another matching TCP
    # exchange appears later in the capture. The QEMU 3.0.95 N6 PCAP
    # baseline has valid IPv4 and TCP checksums in both directions.
    if frame[22] == 0 or internet_checksum(frame[14:14 + ihl]) != 0:
        raise ValueError("invalid TCP IPv4 TTL or header checksum")
    segment = frame[offset:14 + length]
    pseudo = frame[26:34] + b"\x00\x06" + len(segment).to_bytes(2, "big")
    if internet_checksum(pseudo + segment) != 0:
        raise ValueError("invalid TCP segment checksum")
    # URG is forbidden for this non-urgent echo probe. A nonzero urgent
    # pointer with URG clear is also anomalous wire evidence; never let
    # a later clean echo conceal it.
    if frame[offset + 18:offset + 20] != b"\x00\x00":
        raise ValueError("unexpected N4 TCP urgent pointer")
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
    # TCP FIN after a completed echo is normal socket teardown. Earlier
    # FINs and all RSTs still invalidate the isolated exchange. Evaluate
    # packets in capture order rather than rejecting legitimate cleanup.
    completed_flows = set()
    for packet in packets:
        outbound = (packet["src"] == guest and packet["dst"] == SERVER and
                    packet["src_mac"] == MAC and packet["dport"] == PORT)
        inbound = (packet["src"] == SERVER and packet["dst"] == guest and
                   packet["dst_mac"] == MAC and packet["sport"] == PORT)
        if not (outbound or inbound):
            continue
        flags = packet["flags"]
        if flags & 0x04:
            raise ValueError("unexpected N4 TCP RST control packet")
        # Completion is per TCP 4-tuple. A valid echo on one ephemeral
        # port must not authorize FIN on an unrelated socket.
        flow_port = packet["sport"] if outbound else packet["dport"]
        if flags & 0x01:
            if (flow_port not in completed_flows or flags != 0x11
                    or packet["payload"]):
                raise ValueError("premature or malformed N4 TCP FIN")
            continue
        if (not outbound and packet["payload"] == RESPONSE and
                flags in (0x10, 0x18)):
            completed_flows.add(flow_port)
        if flags & 0x02:  # SYN, including SYN-ACK
            expected = 0x02 if outbound else 0x12
            if flags != expected or packet["payload"] or (outbound and packet["ack"] != 0):
                raise ValueError("invalid N4 TCP SYN control packet")
        # The N4 probe does not negotiate ECN, send urgent data or use
        # experimental TCP controls. Check *every* packet of this flow,
        # including empty ACKs; otherwise an invalid ACK could be ignored
        # while a later valid handshake satisfies the correlation gate.
        if flags not in (0x02, 0x12, 0x10, 0x18):
            raise ValueError("unexpected N4 TCP control flags")
        if packet["payload"]:
            # Do not let a later valid echo conceal an unexpected segment
            # on the N4 port pair. This gate expects exactly one request
            # payload shape and one response payload shape.
            expected_payload = REQUEST if outbound else RESPONSE
            if packet["payload"] != expected_payload or flags not in (0x10, 0x18):
                raise ValueError("invalid N4 TCP echo payload, flags or direction")
    # N4 is a non-ECN, non-urgent isolated TCP echo probe. Require exact
    # control flags; masked comparisons could falsely accept ECE/CWR.
    for i, syn in enumerate(packets):
        if not (
            syn["src"] == guest and syn["dst"] == SERVER and
            syn["src_mac"] == MAC and syn["dport"] == PORT and syn["sport"] != 0 and
            syn["flags"] == 0x02 and syn["ack"] == 0 and
            not syn["payload"]
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
                synack["flags"] == 0x12 and
                not synack["payload"] and synack["ack"] == (syn["seq"] + 1) & 0xFFFFFFFF
            ):
                continue
            peer_mac = synack["src_mac"]
            # Once the handshake identifies the L2 peer, every packet on
            # this isolated N4 port pair must use that same Ethernet path.
            # Otherwise a forged extra packet can be hidden behind an
            # otherwise valid exchange in the capture.
            for packet in packets:
                same_outbound = (packet["src"] == guest and packet["dst"] == SERVER and
                                 packet["sport"] == sport and packet["dport"] == PORT)
                same_inbound = (packet["src"] == SERVER and packet["dst"] == guest and
                                packet["sport"] == PORT and packet["dport"] == sport)
                if same_outbound and (packet["src_mac"] != MAC or packet["dst_mac"] != peer_mac):
                    raise ValueError("inconsistent N4 outbound Ethernet peer")
                if same_inbound and (packet["dst_mac"] != MAC or packet["src_mac"] != peer_mac):
                    raise ValueError("inconsistent N4 inbound Ethernet peer")
            for k in range(j + 1, len(packets)):
                ack = packets[k]
                if not (
                    ack["src"] == guest and ack["dst"] == SERVER and
                    ack["src_mac"] == MAC and ack["dst_mac"] == peer_mac and
                    ack["sport"] == sport and
                    ack["dport"] == PORT and ack["flags"] == 0x10 and
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
                        request["flags"] in (0x10, 0x18) and
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
                            response["flags"] in (0x10, 0x18) and
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
        "[NET3095-N1] PCI_IDENTITY=PASS bdf=00:02.0 vendor=8086 device=10D3 command=0x0006 (read-only; DMA/ISR unproven)",
        "[NET3095-N1] DRIVER_BOUND=PASS node=pci:0000:00:02.0 resources=8 irq_sources=1 handler_faults=0 isr_executions=unknown", "[NET3095-N1] TX_QUEUED=yes",
        "[NET3095-N1] PCI_TX_STABLE=PASS command=0x0006 (read-only; PCAP required for TX proof)",
        "[NET3095-N1] DHCP=NOT_ATTEMPTED IP=UNASSIGNED",
        START, "[NET3095-N2] DHCP=PASS ip=10.0.2.15 mask=255.255.255.0 gateway=10.0.2.2",
        PING_START, PING_PASS, TCP_START, TCP_PASS,
    ])
    assert "PASS" in validate(serial, synthetic_capture(frames))
    # The host closes the socket after sending the echo response. A
    # checksum-valid FIN|ACK after that response is legitimate TCP teardown.
    # A late guest FIN|ACK is also permitted; RST remains forbidden.
    teardown = frames + [
        fake_tcp(True, 0x11, 5001 + len(RESPONSE), 1001 + len(REQUEST)),
        fake_tcp(False, 0x11, 1001 + len(REQUEST), 5002 + len(RESPONSE)),
    ]
    assert "PASS" in validate(serial, synthetic_capture(teardown))
    # An echo completed on port 49152 cannot authorize a FIN on a
    # different ephemeral TCP connection to the same server port.
    unrelated_fin = frames + [
        fake_tcp(True, 0x11, 5001 + len(RESPONSE),
                 1001 + len(REQUEST), sport=49153),
    ]
    try:
        validate(serial, synthetic_capture(unrelated_fin))
    except ValueError:
        pass
    else:
        raise AssertionError("accepted FIN from unrelated TCP flow")
    # IPv4 reserved flag (0x8000) is invalid even when DF/MF/offset are clear.
    # Recompute the IP checksum so this negative case isolates flag validation.
    reserved = bytearray(frames[-1])
    reserved[20:22] = (0x8000).to_bytes(2, "big")
    reserved[24:26] = b"\x00\x00"
    reserved[24:26] = internet_checksum(reserved[14:34]).to_bytes(2, "big")
    try:
        validate(serial, synthetic_capture(frames[:-1] + [bytes(reserved)]))
    except ValueError:
        pass
    else:
        raise AssertionError("accepted TCP IPv4 reserved fragmentation flag")
    # A truncated TCP reply must not be ignored after an earlier valid
    # exchange. The declared IPv4 length is unchanged in both cases.
    for truncated in (frames[-1][:30], frames[-1][:45], frames[-1][:-1]):
        try:
            validate(serial, synthetic_capture(frames + [truncated]))
        except ValueError:
            pass
        else:
            raise AssertionError("accepted truncated IPv4/TCP wire evidence")
    # A checksum-correct but impossible total length must fail closed.
    invalid_length = bytearray(frames[-1])
    invalid_length[16:18] = (39).to_bytes(2, "big")
    invalid_length[24:26] = b"\x00\x00"
    invalid_length[24:26] = internet_checksum(invalid_length[14:34]).to_bytes(2, "big")
    try:
        validate(serial, synthetic_capture(frames + [bytes(invalid_length)]))
    except ValueError:
        pass
    else:
        raise AssertionError("accepted impossible IPv4/TCP total length")
    # A zero-TTL IPv4 packet must fail even when its IP checksum is valid.
    # This isolates the semantic TTL guard from checksum corruption.
    zero_ttl = bytearray(frames[-1])
    zero_ttl[22] = 0
    zero_ttl[24:26] = b"\x00\x00"
    zero_ttl[24:26] = internet_checksum(zero_ttl[14:34]).to_bytes(2, "big")
    try:
        validate(serial, synthetic_capture(frames + [bytes(zero_ttl)]))
    except ValueError:
        pass
    else:
        raise AssertionError("accepted checksum-correct zero-TTL TCP packet")
    # TCP data offset 4 (16 bytes) is impossible even if the segment
    # checksum is recomputed correctly; reject it as wire evidence.
    short_tcp_header = bytearray(frames[-1])
    short_tcp_header[46] = 0x40
    short_tcp_header[50:52] = b"\x00\x00"
    tcp_segment = short_tcp_header[34:]
    pseudo = short_tcp_header[26:34] + b"\x00\x06" + len(tcp_segment).to_bytes(2, "big")
    short_tcp_header[50:52] = internet_checksum(pseudo + tcp_segment).to_bytes(2, "big")
    try:
        validate(serial, synthetic_capture(frames[:-1] + [bytes(short_tcp_header)]))
    except ValueError:
        pass
    else:
        raise AssertionError("accepted checksum-correct short TCP header")
    # A malformed TCP data offset must fail closed even when a valid
    # response is already present earlier in the same capture.
    for bad_offset in (0x40, 0xF0):
        malformed = bytearray(frames[-1])
        malformed[46] = bad_offset
        malformed[50:52] = b"\x00\x00"
        segment = malformed[34:]
        pseudo = malformed[26:34] + b"\x00\x06" + len(segment).to_bytes(2, "big")
        malformed[50:52] = internet_checksum(pseudo + segment).to_bytes(2, "big")
        try:
            validate(serial, synthetic_capture(frames + [bytes(malformed)]))
        except ValueError:
            pass
        else:
            raise AssertionError("accepted malformed TCP data offset after valid response")
    # A later valid handshake/echo cannot rescue an earlier malformed
    # SYN or SYN-ACK with ECN/URG flags and otherwise valid checksums.
    for index, bad_flags in ((7, 0x42), (8, 0x52), (10, 0x38), (11, 0x58)):
        malformed = bytearray(frames[index])
        malformed[47] = bad_flags
        malformed[50:52] = b"\x00\x00"
        segment = malformed[34:]
        pseudo = malformed[26:34] + b"\x00\x06" + len(segment).to_bytes(2, "big")
        malformed[50:52] = internet_checksum(pseudo + segment).to_bytes(2, "big")
        try:
            validate(serial, synthetic_capture(frames + [bytes(malformed)]))
        except ValueError:
            pass
        else:
            raise AssertionError("accepted malformed N4 control flags after valid exchange")
    # A malformed *empty* ACK must also fail: it cannot be rescued by
    # the otherwise valid handshake and echo that precede it.
    for bad_flags in (0x30, 0x50, 0x90, 0x11):
        malformed_ack = bytearray(frames[9])
        malformed_ack[47] = bad_flags
        malformed_ack[50:52] = b"\x00\x00"
        segment = malformed_ack[34:]
        pseudo = malformed_ack[26:34] + b"\x00\x06" + len(segment).to_bytes(2, "big")
        malformed_ack[50:52] = internet_checksum(pseudo + segment).to_bytes(2, "big")
        try:
            validate(serial, synthetic_capture(frames + [bytes(malformed_ack)]))
        except ValueError:
            pass
        else:
            raise AssertionError("accepted malformed empty N4 TCP ACK")
    # The urgent pointer must be zero even when URG is clear and the
    # checksum has been recomputed to isolate this semantic guard.
    for index in (7, 9, 11):
        urgent = bytearray(frames[index])
        urgent[52:54] = b"\x00\x01"
        urgent[50:52] = b"\x00\x00"
        segment = urgent[34:]
        pseudo = urgent[26:34] + b"\x00\x06" + len(segment).to_bytes(2, "big")
        urgent[50:52] = internet_checksum(pseudo + segment).to_bytes(2, "big")
        try:
            validate(serial, synthetic_capture(frames + [bytes(urgent)]))
        except ValueError:
            pass
        else:
            raise AssertionError("accepted checksum-correct nonzero TCP urgent pointer")
    # A checksum-correct TCP segment with reserved header bits is still
    # invalid wire evidence, even when a valid response preceded it.
    reserved_tcp = bytearray(frames[-1])
    reserved_tcp[46] |= 0x02  # TCP byte 12, reserved bit 1
    reserved_tcp[50:52] = b"\x00\x00"
    tcp_segment = reserved_tcp[34:]
    pseudo = reserved_tcp[26:34] + b"\x00\x06" + len(tcp_segment).to_bytes(2, "big")
    reserved_tcp[50:52] = internet_checksum(pseudo + tcp_segment).to_bytes(2, "big")
    try:
        validate(serial, synthetic_capture(frames + [bytes(reserved_tcp)]))
    except ValueError:
        pass
    else:
        raise AssertionError("accepted checksum-correct TCP reserved header bits")
    # NS is a TCP control bit outside byte 13. This non-ECN probe must
    # reject it even if the IPv4 and TCP checksums are both valid.
    for index in (7, 11):  # outgoing SYN and incoming echo response
        ns_frame = bytearray(frames[index])
        ns_frame[46] |= 0x01
        ns_frame[50:52] = b"\x00\x00"
        ns_segment = ns_frame[34:]
        ns_pseudo = ns_frame[26:34] + b"\x00\x06" + len(ns_segment).to_bytes(2, "big")
        ns_frame[50:52] = internet_checksum(ns_pseudo + ns_segment).to_bytes(2, "big")
        try:
            validate(serial, synthetic_capture(frames + [bytes(ns_frame)]))
        except ValueError:
            pass
        else:
            raise AssertionError("accepted checksum-correct TCP NS flag")
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
        (serial, frames + [fake_tcp(False, 0x14, 1001 + len(REQUEST), 5001 + len(RESPONSE))]),  # ACK|RST after valid exchange
        (serial, frames[:11] + [fake_tcp(True, 0x11, 5001, 1001 + len(REQUEST))] + frames[11:]),  # premature FIN before echo reply
        (serial, frames[:7] + [fake_tcp(False, 0x0A, 1000, 0)] + frames[8:]),  # SYN|PSH
        (serial, frames[:7] + [fake_tcp(False, 0x22, 1000, 0)] + frames[8:]),  # SYN|URG
        (serial, frames[:7] + [fake_tcp(False, 0x42, 1000, 0)] + frames[8:]),  # SYN|ECE
        (serial, frames[:7] + [fake_tcp(False, 0x82, 1000, 0)] + frames[8:]),  # SYN|CWR
        # A bare SYN has no valid ACK field; reject a nonzero acknowledgement.
        (serial, frames[:7] + [fake_tcp(False, 0x02, 1000, 999)] + frames[8:]),
        (serial, frames[:8] + [fake_tcp(True, 0x16, 5000, 1001)] + frames[9:]),  # SYN|ACK|RST
        (serial, frames[:8] + [fake_tcp(True, 0x1A, 5000, 1001)] + frames[9:]),  # SYN|ACK|PSH
        (serial, frames[:8] + [fake_tcp(True, 0x32, 5000, 1001)] + frames[9:]),  # SYN|ACK|URG
        (serial, frames[:8] + [fake_tcp(True, 0x52, 5000, 1001)] + frames[9:]),  # SYN|ACK|ECE
        (serial, frames[:8] + [fake_tcp(True, 0x12, 5000, 1001, b"BAD")] + frames[9:]),
        (serial, frames[:8] + [corrupt_mac(frames[8], 6)] + frames[9:]),
        (serial, frames[:9] + [corrupt_mac(frames[9], 0)] + frames[10:]),
        (serial, frames[:10] + [corrupt_mac(frames[10], 0)] + frames[11:]),
        (serial, frames[:11] + [corrupt_mac(frames[11], 6)]),
        # A valid echo must not hide an extra frame with a spoofed L2 peer.
        (serial, frames + [corrupt_mac(frames[11], 6)]),
        (serial, frames + [corrupt_mac(frames[10], 0)]),
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
        # An extra checksum-valid payload after the successful echo is not
        # evidence of a clean isolated N4 flow, even when PASS was reached.
        (serial, frames + [fake_tcp(False, 0x18, 1001 + len(REQUEST),
                                   5001 + len(RESPONSE), b"BAD")]),
        (serial, frames + [fake_tcp(True, 0x18, 5001 + len(RESPONSE),
                                   1001 + len(REQUEST), b"BAD")]),
        # TCP payloads must belong to the exact established sequence space.
        (serial, frames[:10] + [fake_tcp(False, 0x18, 1002, 5001, REQUEST), frames[-1]]),
        (serial, frames[:10] + [fake_tcp(False, 0x18, 1001, 5002, REQUEST), frames[-1]]),
        (serial, frames[:11] + [fake_tcp(True, 0x18, 5002, 1001 + len(REQUEST), RESPONSE)]),
        (serial, frames[:9] + [fake_tcp(False, 0x10, 1002, 5001)] + frames[10:]),
        (serial, frames[:9] + [fake_tcp(False, 0x18, 1001, 5001)] + frames[10:]),  # handshake ACK|PSH
        (serial, frames[:9] + [fake_tcp(False, 0x50, 1001, 5001)] + frames[10:]),  # handshake ACK|ECE
        (serial, frames[:10] + [fake_tcp(False, 0x38, 1001, 5001, REQUEST)] + frames[11:]),  # data ACK|PSH|URG
        (serial, frames[:10] + [fake_tcp(False, 0x58, 1001, 5001, REQUEST)] + frames[11:]),  # data ACK|PSH|ECE
        (serial, frames[:11] + [fake_tcp(True, 0x38, 5001, 1001 + len(REQUEST), RESPONSE)]),  # response ACK|PSH|URG
        (serial, frames[:11] + [fake_tcp(True, 0x98, 5001, 1001 + len(REQUEST), RESPONSE)]),  # response ACK|PSH|CWR
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
