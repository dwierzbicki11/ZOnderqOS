#!/usr/bin/env python3
"""N5 DNS proof: correlate QEMU UDP/53 request and A reply with guest serial.

Synthetic fixtures test the parser only. Only the real QEMU filter-dump PCAP,
guest serial output, and the already verified N1-N4 gates establish a result.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import struct

from verify_3095_network_n1 import pcap_frames
from verify_3095_network_n2 import (
    LEASE, MAC, SERVER, START, fake_dhcp, synthetic_capture,
)
from verify_3095_network_n3 import PING_START, PING_PASS, fake_icmp
from verify_3095_network_n4 import (
    TCP_START, TCP_PASS, REQUEST, RESPONSE, PORT, fake_tcp,
    validate as validate_n4,
)

DNS_SERVER = bytes([10, 0, 2, 3])
LOOPBACK = bytes([127, 0, 0, 1])
DNS_START = "[NET3095-N5] DNS_ATTEMPT=START"
DNS_PASS = "[NET3095-N5] DNS=PASS host=localhost ip=127.0.0.1 server=10.0.2.3"


def udp_frame(frame: bytes):
    """Extract a complete unfragmented IPv4 UDP payload from Ethernet."""
    if len(frame) < 14 + 20 + 8 or frame[12:14] != b"\x08\x00":
        return None
    if frame[14] >> 4 != 4 or frame[23] != 17:
        return None
    ihl = (frame[14] & 15) * 4
    if ihl < 20 or len(frame) < 14 + ihl + 8:
        return None
    length = int.from_bytes(frame[16:18], "big")
    if (int.from_bytes(frame[20:22], "big") & 0x3FFF) or length < ihl + 8:
        return None
    if len(frame) < 14 + length:
        return None
    off = 14 + ihl
    sport, dport, udp_len, _checksum = struct.unpack_from(">HHHH", frame, off)
    if udp_len < 8 + 12 or udp_len > length - ihl:
        return None
    return {
        "src": frame[26:30], "dst": frame[30:34],
        "src_mac": frame[6:12], "dst_mac": frame[:6],
        "sport": sport, "dport": dport,
        "data": frame[off + 8:off + udp_len],
    }


def dns_name(data: bytes, pos: int):
    """Read a bounded DNS name, including compressed answer-name pointers."""
    labels = []
    end = None
    visited = set()
    for _ in range(64):
        if pos >= len(data) or pos in visited:
            raise ValueError("truncated or cyclic DNS name")
        visited.add(pos)
        n = data[pos]
        if (n & 0xC0) == 0xC0:
            if pos + 1 >= len(data):
                raise ValueError("truncated DNS pointer")
            pointer = ((n & 0x3F) << 8) | data[pos + 1]
            if end is None:
                end = pos + 2
            pos = pointer
            continue
        if n & 0xC0:
            raise ValueError("invalid DNS label type")
        pos += 1
        if n == 0:
            return ".".join(labels), (end if end is not None else pos)
        if n > 63 or pos + n > len(data):
            raise ValueError("invalid DNS label")
        labels.append(data[pos:pos + n].decode("ascii").lower())
        pos += n
    raise ValueError("DNS name exceeds bounds")


def dns_packet(data: bytes):
    if len(data) < 12:
        return None
    txid, flags, qd, an, _ns, _ar = struct.unpack_from(">HHHHHH", data)
    if qd != 1 or an > 64:
        return None
    try:
        name, pos = dns_name(data, 12)
        if pos + 4 > len(data):
            return None
        qtype, qclass = struct.unpack_from(">HH", data, pos)
        pos += 4
        if name != "localhost" or qtype != 1 or qclass != 1:
            return None
        answers = []
        for _ in range(an):
            answer_name, pos = dns_name(data, pos)
            if pos + 10 > len(data):
                return None
            typ, cls, _ttl, rdlen = struct.unpack_from(">HHIH", data, pos)
            pos += 10
            if pos + rdlen > len(data):
                return None
            if answer_name == name and typ == 1 and cls == 1 and rdlen == 4:
                answers.append(data[pos:pos + 4])
            pos += rdlen
    except (ValueError, UnicodeDecodeError, struct.error):
        return None
    return {
        "id": txid, "reply": bool(flags & 0x8000),
        "rcode": flags & 0xF, "answers": answers,
    }


def arp_frame(frame: bytes):
    """Decode an Ethernet/IPv4 ARP frame without trusting padding."""
    if len(frame) < 42 or frame[12:14] != b"\\x08\\x06":
        return None
    htype, ptype, hlen, plen, opcode = struct.unpack_from(">HHBBH", frame, 14)
    if (htype, ptype, hlen, plen) != (1, 0x0800, 6, 4) or opcode not in (1, 2):
        return None
    return {
        "op": opcode, "src_mac": frame[6:12], "dst_mac": frame[:6],
        "sha": frame[22:28], "spa": frame[28:32],
        "tha": frame[32:38], "tpa": frame[38:42],
    }


def verify_arp_for_dns(frames: list[bytes], guest: bytes) -> int:
    """Require a real guest ARP request and matching DNS-proxy ARP reply.

    Returns the reply's capture index; the DNS exchange must follow it.
    Synthetic fixtures test the parser, not real NIC operation.
    """
    broadcast = b"\\xff" * 6
    zero_mac = b"\\x00" * 6
    for i, frame in enumerate(frames):
        req = arp_frame(frame)
        if req is None or not (
            req["op"] == 1 and req["src_mac"] == MAC and
            req["dst_mac"] == broadcast and req["sha"] == MAC and
            req["spa"] == guest and req["tha"] == zero_mac and
            req["tpa"] == DNS_SERVER
        ):
            continue
        for j in range(i + 1, len(frames)):
            reply = arp_frame(frames[j])
            if reply is None:
                continue
            mac = reply["sha"]
            if (
                reply["op"] == 2 and reply["dst_mac"] == MAC and
                reply["src_mac"] == mac and mac != MAC and
                mac != broadcast and mac != zero_mac and (mac[0] & 1) == 0 and
                reply["spa"] == DNS_SERVER and reply["tha"] == MAC and
                reply["tpa"] == guest
            ):
                return j
    raise ValueError("no correlated QEMU ARP request/reply for DNS proxy")


def validate(serial: str, pcap: bytes) -> str:
    validate_n4(serial, pcap)
    lines = [line.strip() for line in serial.replace("\r", "").splitlines()]
    if any("[NET3095-N5][FAIL]" in line for line in lines):
        raise ValueError("guest DNS probe explicitly failed")
    if lines.count(DNS_START) != 1 or lines.count(DNS_PASS) != 1:
        raise ValueError("missing or duplicate DNS markers")
    if not (lines.index(TCP_PASS) < lines.index(DNS_START) < lines.index(DNS_PASS)):
        raise ValueError("DNS/TCP markers out of order")
    leases = [m for line in lines if (m := LEASE.fullmatch(line))]
    if len(leases) != 1:
        raise ValueError("missing unique guest lease")
    guest = bytes(int(part) for part in leases[0].group(1).split("."))
    frames = list(pcap_frames(pcap))
    arp_reply_index = verify_arp_for_dns(frames, guest)
    packets = []
    for index, frame in enumerate(frames):
        if index <= arp_reply_index:
            continue
        udp = udp_frame(frame)
        if udp is not None:
            dns = dns_packet(udp["data"])
            if dns is not None:
                packets.append((udp, dns))
    for i, (req, query) in enumerate(packets):
        if not (
            req["src"] == guest and req["dst"] == DNS_SERVER and
            req["src_mac"] == MAC and req["dport"] == 53 and
            req["sport"] != 0 and not query["reply"] and
            query["rcode"] == 0 and not query["answers"]
        ):
            continue
        for reply, answer in packets[i + 1:]:
            if (
                reply["src"] == DNS_SERVER and reply["dst"] == guest and
                reply["dst_mac"] == MAC and reply["src_mac"] != MAC and
                reply["sport"] == 53 and reply["dport"] == req["sport"] and
                answer["reply"] and answer["rcode"] == 0 and
                answer["id"] == query["id"] and LOOPBACK in answer["answers"]
            ):
                return "[NET3095-N5-GATE] PASS real QEMU ARP resolution and DNS A localhost=127.0.0.1 via 10.0.2.3"
    raise ValueError("no correlated QEMU DNS query/answer RX/TX proof")


def fake_arp(reply: bool, sender_ip: bytes | None = None,
             target_ip: bytes | None = None, sender_mac: bytes | None = None,
             target_mac: bytes | None = None) -> bytes:
    guest = bytes([10, 0, 2, 15])
    proxy_mac = bytes.fromhex("52550a000203")
    sha = sender_mac if sender_mac is not None else (proxy_mac if reply else MAC)
    tha = target_mac if target_mac is not None else (MAC if reply else bytes(6))
    spa = sender_ip if sender_ip is not None else (DNS_SERVER if reply else guest)
    tpa = target_ip if target_ip is not None else (guest if reply else DNS_SERVER)
    dst = MAC if reply else bytes([255] * 6)
    packet = (dst + sha + b"\\x08\\x06" +
              struct.pack(">HHBBH", 1, 0x0800, 6, 4, 2 if reply else 1) +
              sha + spa + tha + tpa)
    return packet + bytes(max(0, 60 - len(packet)))


def fake_dns(reply: bool, txid: int = 0x3095, host: bytes = b"localhost",
             answer: bytes = LOOPBACK, sport: int = 53,
             dest_mac: bytes = MAC) -> bytes:
    qname = bytes([len(host)]) + host + b"\x00"
    question = qname + struct.pack(">HH", 1, 1)
    dns = struct.pack(">HHHHHH", txid, 0x8180 if reply else 0x0100,
                      1, 1 if reply else 0, 0, 0) + question
    if reply:
        dns += b"\xc0\x0c" + struct.pack(">HHIH", 1, 1, 60, len(answer)) + answer
    udp = struct.pack(">HHHH", sport if reply else 53095,
                      53095 if reply else 53, len(dns) + 8, 0) + dns
    ip = bytearray(20)
    ip[0] = 0x45
    ip[2:4] = (len(udp) + 20).to_bytes(2, "big")
    ip[8:10] = bytes([64, 17])
    ip[12:16] = DNS_SERVER if reply else bytes([10, 0, 2, 15])
    ip[16:20] = bytes([10, 0, 2, 15]) if reply else DNS_SERVER
    gateway_mac = bytes.fromhex("525400123499")
    eth = ((dest_mac if reply else gateway_mac) +
           (gateway_mac if reply else MAC) + b"\x08\x00")
    return eth + ip + udp


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
        fake_arp(False), fake_arp(True), fake_dns(False), fake_dns(True),
    ]
    serial = "\n".join([
        "[NET3095-N1] BOOT", "[NET3095-N1] discovered=1",
        "[NET3095-N1] cpu=bsp adapter=0 name=e1000e mac=52:54:00:12:34:56 ready=yes link=up",
        "[NET3095-N1] PASS READY_LINK=1", "[NET3095-N1] TX_QUEUED=yes",
        "[NET3095-N1] DHCP=NOT_ATTEMPTED IP=UNASSIGNED",
        START, "[NET3095-N2] DHCP=PASS ip=10.0.2.15 mask=255.255.255.0 gateway=10.0.2.2",
        PING_START, PING_PASS, TCP_START, TCP_PASS, DNS_START, DNS_PASS,
    ])
    assert "PASS" in validate(serial, synthetic_capture(frames))
    bad = [
        (serial.replace(DNS_START, ""), frames),
        (serial.replace(DNS_PASS, ""), frames),
        (serial + "\n" + DNS_PASS, frames),
        (serial + "\n[NET3095-N5][FAIL] timeout", frames),
        (serial.replace(DNS_START, "").replace(TCP_PASS, DNS_START + "\n" + TCP_PASS), frames),
        (serial, frames[:-4] + frames[-2:]),  # missing both ARP frames
        (serial, frames[:-3] + frames[-2:]),  # missing ARP reply
        (serial, frames[:-4] + [fake_arp(True), fake_arp(False)] + frames[-2:]),
        (serial, frames[:-3] + [fake_arp(True, sender_ip=bytes([10, 0, 2, 4]))] + frames[-2:]),
        (serial, frames[:-3] + [fake_arp(True, target_ip=bytes([10, 0, 2, 16]))] + frames[-2:]),
        (serial, frames[:-3] + [fake_arp(True, sender_mac=b"\\x01\\x02\\x03\\x04\\x05\\x06")] + frames[-2:]),
        (serial, frames[:-1]),
        (serial, frames[:-2] + [frames[-1], frames[-2]]),
        (serial, frames[:-1] + [fake_dns(True, txid=0x3096)]),
        (serial, frames[:-1] + [fake_dns(True, answer=bytes([127, 0, 0, 2]))]),
        (serial, frames[:-1] + [fake_dns(True, host=b"wronghost")]),
        (serial, frames[:-1] + [fake_dns(True, sport=54)]),
        (serial, frames[:-1] + [fake_dns(True, dest_mac=b"\x02\x00\x00\x00\x00\x01")]),
        (serial, frames[:12] + [frames[-1]]),
    ]
    for text, packets in bad:
        try:
            validate(text, synthetic_capture(packets))
        except ValueError:
            continue
        raise AssertionError("accepted invalid DNS RX/TX proof")
    try:
        validate(serial, synthetic_capture(frames)[:-1])
    except ValueError:
        pass
    else:
        raise AssertionError("accepted truncated PCAP")
    print("[NET3095-N5-CONTRACT] PASS synthetic DNS positive + negative cases")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("serial_log", nargs="?", type=Path)
    ap.add_argument("pcap", nargs="?", type=Path)
    ap.add_argument("--self-test", action="store_true")
    args = ap.parse_args()
    if args.self_test:
        self_test()
    else:
        if args.serial_log is None or args.pcap is None:
            ap.error("real QEMU serial log AND PCAP required")
        try:
            print(validate(args.serial_log.read_text(errors="replace"), args.pcap.read_bytes()))
        except (ValueError, OSError) as exc:
            ap.exit(1, f"[NET3095-N5-GATE][FAIL] {exc}\n")
