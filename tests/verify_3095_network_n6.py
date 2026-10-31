#!/usr/bin/env python3
"""N6 HTTP/1.0 proof: real QEMU TCP stream, guest serial and host evidence.

Synthetic fixtures exercise only the fail-closed validator. A network PASS
requires an actual QEMU filter-dump PCAP and the preceding N1-N5 gates.
"""
from __future__ import annotations

import argparse
from pathlib import Path

from verify_3095_network_n1 import pcap_frames
from verify_3095_network_n2 import (
    LEASE, MAC, SERVER, START, fake_dhcp, synthetic_capture,
)
from verify_3095_network_n3 import PING_START, PING_PASS, fake_icmp
from verify_3095_network_n4 import (
    TCP_START, TCP_PASS, REQUEST as ECHO_REQUEST,
    RESPONSE as ECHO_RESPONSE, fake_tcp, tcp_frame,
)
from verify_3095_network_n5 import (
    DNS_START, DNS_PASS, fake_arp, fake_dns, validate as validate_n5,
)
from network_3095_http_host import PORT, REQUEST, RESPONSE

HTTP_START = "[NET3095-N6] HTTP_ATTEMPT=START"
HTTP_PASS = f"[NET3095-N6] HTTP=PASS from=10.0.2.2 port={PORT} bytes={len(RESPONSE)}"
HTTP_REPEAT_PASS = f"[NET3095-N6] HTTP_REPEAT=PASS from=10.0.2.2 port={PORT} bytes={len(RESPONSE)}"
GUEST = bytes([10, 0, 2, 15])


def stream_payload(packets, first: int, guest: bytes, client_port: int,
                   direction: str, seq: int, ack: int, expected: bytes,
                   server_mac: bytes, min_segments: int = 1):
    """Match contiguous TCP payload bytes; permit small multi-segment responses.

    No packet with an unrelated port, peer MAC, TCP sequence or acknowledgment can
    satisfy the expected application payload. Pure ACKs may be interleaved.
    N6 additionally requires at least three distinct accepted server payload
    segments; the fixture sends a 4 KiB body in bounded 1024-byte writes.
    """
    collected = bytearray()
    segments = 0
    for i in range(first, len(packets)):
        p = packets[i]
        client = direction == "client"
        if not (
            p["src"] == (guest if client else SERVER) and
            p["dst"] == (SERVER if client else guest) and
            p["src_mac"] == (MAC if client else server_mac) and
            p["dst_mac"] == (server_mac if client else MAC) and
            p["sport"] == (client_port if client else PORT) and
            p["dport"] == (PORT if client else client_port) and
            (p["flags"] & 0x14) == 0x10 and p["payload"]
        ):
            continue
        if p["seq"] != (seq + len(collected)) & 0xFFFFFFFF or p["ack"] != ack:
            continue
        payload = p["payload"]
        if not expected.startswith(bytes(collected) + payload):
            continue
        collected.extend(payload)
        segments += 1
        if bytes(collected) == expected:
            return i + 1 if segments >= min_segments else None
    return None


def server_fin_acked(packets, first: int, guest: bytes,
                     client_port: int, client_seq: int, server_seq: int,
                     server_mac: bytes) -> bool:
    """Require the peer's FIN and the guest's matching final ACK.

    The server sends FIN after its complete HTTP response. This verifies
    the guest consumes TCP teardown, not merely the HTTP payload.
    """
    client_end = (client_seq + len(REQUEST)) & 0xFFFFFFFF
    server_end = (server_seq + len(RESPONSE)) & 0xFFFFFFFF
    for i in range(first, len(packets)):
        fin = packets[i]
        if not (
            fin["src"] == SERVER and fin["dst"] == guest and
            fin["src_mac"] == server_mac and fin["dst_mac"] == MAC and
            fin["sport"] == PORT and fin["dport"] == client_port and
            (fin["flags"] & 0x17) == 0x11 and
            fin["seq"] == server_end and fin["ack"] == client_end and
            not fin["payload"]
        ):
            continue
        for ack_index in range(i + 1, len(packets)):
            ack = packets[ack_index]
            if (
                ack["src"] == guest and ack["dst"] == SERVER and
                ack["src_mac"] == MAC and ack["dst_mac"] == server_mac and
                ack["sport"] == client_port and
                ack["dport"] == PORT and (ack["flags"] & 0x17) == 0x10 and
                ack["seq"] == client_end and
                ack["ack"] == (server_end + 1) & 0xFFFFFFFF and
                not ack["payload"]
            ):
                return ack_index
    return None


def find_http_transaction(packets, guest: bytes, first: int, excluded_port: int | None):
    """Require an entire HTTP stream and peer FIN/ACK for one TCP connection."""
    for i in range(first, len(packets)):
        syn = packets[i]
        if syn["sport"] == excluded_port:
            continue
        if not (
            syn["src"] == guest and syn["dst"] == SERVER and
            syn["src_mac"] == MAC and syn["sport"] != 0 and
            syn["dport"] == PORT and (syn["flags"] & 0x17) == 0x02 and
            not syn["payload"]
        ):
            continue
        client_port = syn["sport"]
        client_seq = (syn["seq"] + 1) & 0xFFFFFFFF
        for j in range(i + 1, len(packets)):
            synack = packets[j]
            if not (
                synack["src"] == SERVER and synack["dst"] == guest and
                synack["src_mac"] != MAC and synack["dst_mac"] == MAC and
                syn["dst_mac"] == synack["src_mac"] and
                synack["sport"] == PORT and synack["dport"] == client_port and
                (synack["flags"] & 0x17) == 0x12 and
                synack["ack"] == client_seq and not synack["payload"]
            ):
                continue
            server_seq = (synack["seq"] + 1) & 0xFFFFFFFF
            server_mac = synack["src_mac"]
            for k in range(j + 1, len(packets)):
                ack = packets[k]
                if not (
                    ack["src"] == guest and ack["dst"] == SERVER and
                    ack["src_mac"] == MAC and ack["dst_mac"] == server_mac and
                    ack["sport"] == client_port and
                    ack["dport"] == PORT and (ack["flags"] & 0x17) == 0x10 and
                    ack["seq"] == client_seq and ack["ack"] == server_seq and
                    not ack["payload"]
                ):
                    continue
                end_request = stream_payload(
                    packets, k + 1, guest, client_port, "client",
                    client_seq, server_seq, REQUEST, server_mac)
                if end_request is None:
                    continue
                end_response = stream_payload(
                    packets, end_request, guest, client_port, "server",
                    server_seq, (client_seq + len(REQUEST)) & 0xFFFFFFFF,
                    RESPONSE, server_mac, min_segments=3)
                if end_response is not None:
                    fin_ack_index = server_fin_acked(
                        packets, end_response, guest, client_port,
                        client_seq, server_seq, server_mac)
                    if fin_ack_index is not None:
                        return client_port, fin_ack_index
    return None



def validate(serial: str, pcap: bytes) -> str:
    validate_n5(serial, pcap)
    lines = [line.strip() for line in serial.replace("\r", "").splitlines()]
    if any("[NET3095-N6][FAIL]" in line for line in lines):
        raise ValueError("guest HTTP probe explicitly failed")
    if (lines.count(HTTP_START) != 1 or lines.count(HTTP_PASS) != 1 or
            lines.count(HTTP_REPEAT_PASS) != 1):
        raise ValueError("missing or duplicate HTTP markers")
    if not (lines.index(DNS_PASS) < lines.index(HTTP_START) < lines.index(HTTP_PASS) < lines.index(HTTP_REPEAT_PASS)):
        raise ValueError("DNS/HTTP markers out of order")
    leases = [m for line in lines if (m := LEASE.fullmatch(line))]
    if len(leases) != 1:
        raise ValueError("missing unique guest lease")
    guest = bytes(int(part) for part in leases[0].group(1).split("."))
    packets = [p for frame in pcap_frames(pcap) if (p := tcp_frame(frame))]
    first = find_http_transaction(packets, guest, 0, None)
    if first is None:
        raise ValueError("first HTTP TCP exchange incomplete")
    second = find_http_transaction(packets, guest, first[1] + 1, first[0])
    if second is None:
        raise ValueError("second independent HTTP TCP exchange incomplete")
    return "[NET3095-N6-GATE] PASS two real QEMU HTTP/1.0 TCP exchanges with RX/TX and FIN/ACK teardown"


def fake_http(reply: bool, flags: int, seq: int, ack: int,
              payload: bytes = b"", port: int = PORT, client_port: int = 49153) -> bytes:
    return fake_tcp(reply, flags, seq, ack, payload,
                    sport=client_port, dport=port)


def self_test() -> None:
    raw = bytes([255] * 6) + MAC + b"\x88\xb5" + b"ZONDERQ3095TX"
    raw += bytes(60 - len(raw))
    base = [
        raw, fake_dhcp(1, 1, MAC, GUEST), fake_dhcp(2, 1, MAC, GUEST),
        fake_dhcp(3, 2, MAC, GUEST, GUEST), fake_dhcp(5, 2, MAC, GUEST),
        fake_icmp(False, MAC, GUEST), fake_icmp(True, MAC, GUEST),
        fake_tcp(False, 0x02, 1000, 0),
        fake_tcp(True, 0x12, 5000, 1001),
        fake_tcp(False, 0x10, 1001, 5001),
        fake_tcp(False, 0x18, 1001, 5001, ECHO_REQUEST),
        fake_tcp(True, 0x18, 5001, 1001 + len(ECHO_REQUEST), ECHO_RESPONSE),
        fake_arp(False), fake_arp(True), fake_dns(False), fake_dns(True),
    ]
    handshake = [
        fake_http(False, 0x02, 10000, 0),
        fake_http(True, 0x12, 20000, 10001),
        fake_http(False, 0x10, 10001, 20001),
    ]
    request = [fake_http(False, 0x18, 10001, 20001, REQUEST)]
    split = RESPONSE.index(b"\r\n\r\n") + 4
    # Keep synthetic packets below Ethernet MTU; test real sequence-space
    # reassembly, not an impossible single 4 KiB IP packet.
    cuts = [0, split]
    cuts.extend(range(split + 1024, len(RESPONSE), 1024))
    cuts.append(len(RESPONSE))
    response = [
        fake_http(True, 0x18, 20001 + start, 10001 + len(REQUEST),
                  RESPONSE[start:end])
        for start, end in zip(cuts, cuts[1:])
    ]
    single_response = [fake_http(True, 0x18, 20001, 10001 + len(REQUEST), RESPONSE)]
    two_response = [
        fake_http(True, 0x18, 20001, 10001 + len(REQUEST), RESPONSE[:split]),
        fake_http(True, 0x18, 20001 + split, 10001 + len(REQUEST), RESPONSE[split:]),
    ]
    teardown = [
        fake_http(True, 0x11, 20001 + len(RESPONSE), 10001 + len(REQUEST)),
        fake_http(False, 0x10, 10001 + len(REQUEST), 20002 + len(RESPONSE)),
    ]
    second_handshake = [
        fake_http(False, 0x02, 30000, 0, client_port=49154),
        fake_http(True, 0x12, 40000, 30001, client_port=49154),
        fake_http(False, 0x10, 30001, 40001, client_port=49154),
    ]
    second_request = [fake_http(False, 0x18, 30001, 40001, REQUEST, client_port=49154)]
    second_response = [
        fake_http(True, 0x18, 40001 + start, 30001 + len(REQUEST),
                  RESPONSE[start:end], client_port=49154)
        for start, end in zip(cuts, cuts[1:])
    ]
    second_teardown = [
        fake_http(True, 0x11, 40001 + len(RESPONSE), 30001 + len(REQUEST), client_port=49154),
        fake_http(False, 0x10, 30001 + len(REQUEST), 40002 + len(RESPONSE), client_port=49154),
    ]
    second_frames = second_handshake + second_request + second_response + second_teardown
    serial = "\n".join([
        "[NET3095-N1] BOOT", "[NET3095-N1] discovered=1",
        "[NET3095-N1] cpu=bsp adapter=0 name=e1000e mac=52:54:00:12:34:56 ready=yes link=up",
        "[NET3095-N1] PASS READY_LINK=1", "[NET3095-N1] TX_QUEUED=yes",
        "[NET3095-N1] DHCP=NOT_ATTEMPTED IP=UNASSIGNED",
        START, "[NET3095-N2] DHCP=PASS ip=10.0.2.15 mask=255.255.255.0 gateway=10.0.2.2",
        PING_START, PING_PASS, TCP_START, TCP_PASS,
        DNS_START, DNS_PASS, HTTP_START, HTTP_PASS, HTTP_REPEAT_PASS,
    ])
    frames = base + handshake + request + response + teardown + second_frames
    assert "PASS" in validate(serial, synthetic_capture(frames))
    # A complete one- or two-packet response cannot satisfy the large RX
    # segmentation proof, even when the payload bytes are otherwise valid.
    for insufficient in (single_response, two_response):
        try:
            validate(serial, synthetic_capture(base + handshake + request + insufficient + teardown + second_frames))
        except ValueError:
            pass
        else:
            raise AssertionError("accepted insufficient HTTP segmentation")
    half = len(REQUEST) // 2
    split_request = base + handshake + [
        fake_http(False, 0x18, 10001, 20001, REQUEST[:half]),
        fake_http(False, 0x18, 10001 + half, 20001, REQUEST[half:]),
    ] + response + teardown + second_frames
    assert "PASS" in validate(serial, synthetic_capture(split_request))
    # A 4 KiB response with different 1 KiB blocks catches duplicated,
    # missing, corrupted and reordered DMA/TCP data, not merely byte count.
    corrupted = list(response)
    damaged = bytearray(RESPONSE[cuts[2]:cuts[3]])
    damaged[0] = ord("Z") if damaged[0] != ord("Z") else ord("Y")
    corrupted[2] = fake_http(
        True, 0x18, 20001 + cuts[2], 10001 + len(REQUEST), bytes(damaged))
    # A valid IP/TCP tuple is insufficient if frames do not belong to the
    # Ethernet peer that answered the SYN. Verify both data and FIN/ACK L2.
    wrong_mac = bytes.fromhex("020000000001")
    def corrupt_mac(frame: bytes, offset: int) -> bytes:
        return frame[:offset] + wrong_mac + frame[offset + 6:]

    negatives = [
        (serial, base + [corrupt_mac(handshake[0], 0)] + handshake[1:] + request + response + teardown),
        (serial, base + handshake[:1] + [corrupt_mac(handshake[1], 6)] + handshake[2:] + request + response + teardown),
        (serial, base + handshake[:2] + [corrupt_mac(handshake[2], 0)] + request + response + teardown),
        (serial, base + handshake + [corrupt_mac(request[0], 0)] + response + teardown),
        (serial, base + handshake + request + [corrupt_mac(response[0], 6)] + response[1:] + teardown),
        (serial, base + handshake + request + response + [corrupt_mac(teardown[0], 6), teardown[1]]),
        (serial, base + handshake + request + response + [teardown[0], corrupt_mac(teardown[1], 0)]),
        (serial, base + handshake + request + corrupted + teardown),
        (serial, base + handshake + request + response[:2] + response[3:] + teardown),
        (serial, base + handshake + request + response[:1] + response[2:3] + response[1:2] + response[3:] + teardown),
        (serial.replace(HTTP_START, ""), frames),
        (serial.replace(HTTP_PASS, ""), frames),
        (serial + "\n" + HTTP_PASS, frames),
        (serial + "\n[NET3095-N6][FAIL] bad", frames),
        (serial.replace(HTTP_START, "").replace(DNS_PASS, HTTP_START + "\n" + DNS_PASS), frames),
        (serial, base + handshake + request),
        (serial, base + handshake + request + response),  # no TCP teardown
        (serial, base + handshake + request + response[:1] + teardown),  # truncated RX
        (serial, base + handshake + request + response + teardown[:1]),  # FIN without guest ACK
        (serial, base + handshake + request + response + [
            fake_http(True, 0x11, 20002 + len(RESPONSE), 10001 + len(REQUEST)),
            teardown[1],
        ]),
        (serial, base + handshake + request + response + [
            teardown[0],
            fake_http(False, 0x10, 10001 + len(REQUEST), 20001 + len(RESPONSE)),
        ]),
        (serial, base + handshake[:1] + handshake[2:] + request + response),
        (serial, base + handshake + [fake_http(False, 0x18, 10002, 20001, REQUEST)] + response),
        (serial, base + handshake + request + [fake_http(True, 0x18, 20002, 10001 + len(REQUEST), RESPONSE)]),
        (serial, base + handshake + request + [fake_http(True, 0x18, 20001, 10002 + len(REQUEST), RESPONSE)]),
        (serial, base + handshake + request + [
            fake_http(True, 0x18, 20001, 10001 + len(REQUEST), RESPONSE[:split].replace(b"200 OK", b"404 No")),
            response[1],
        ] + teardown),
        (serial, base + handshake + request + [fake_http(True, 0x18, 20001, 10001 + len(REQUEST), RESPONSE, port=18097)]),
        (serial, base + handshake + [fake_http(False, 0x18, 10001, 20001, REQUEST.replace(b"/health", b"/healthz"))] + response),
    ]
    negatives.extend([
        (serial, base + handshake + request + response + teardown),  # second connection absent
        (serial.replace(HTTP_REPEAT_PASS, ""), frames),
        (serial + "\n" + HTTP_REPEAT_PASS, frames),
        (serial.replace(HTTP_REPEAT_PASS, "").replace(HTTP_PASS, HTTP_REPEAT_PASS), frames),
    ])
    for bad_serial, bad_frames in negatives:
        try:
            validate(bad_serial, synthetic_capture(bad_frames + second_frames))
        except ValueError:
            continue
        raise AssertionError("accepted invalid HTTP RX/TX proof")
    print("[NET3095-N6-CONTRACT] PASS synthetic HTTP stream and negative cases")


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
            parser.exit(1, f"[NET3095-N6-GATE][FAIL] {exc}\n")
