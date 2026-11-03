#!/usr/bin/env python3
"""QEMU-only E1000E RX proof: correlate DHCP DORA with serial lease and N1 TX.

This checks real QEMU filter-dump frames, not just a guest log saying DHCP worked.
Synthetic self-tests below are parser checks, never boot or hardware evidence.
"""
from __future__ import annotations

import ipaddress
from pathlib import Path
import argparse
import re
import struct

from verify_3095_network_n1 import validate as validate_n1, pcap_frames, synthetic_pcap

START = "[NET3095-N2] DHCP_ATTEMPT=START"
LEASE = re.compile(
    r"^\[NET3095-N2\] DHCP=PASS ip=(10\.0\.2\.[0-9]{1,3}) "
    r"mask=(255\.255\.255\.0) gateway=(10\.0\.2\.2)$"
)
MAC = bytes.fromhex("525400123456")
SERVER = bytes([10, 0, 2, 2])


def checksum16(data: bytes) -> int:
    """Internet one's-complement checksum, including an odd trailing byte."""
    if len(data) & 1:
        data += b"\x00"
    total = sum(int.from_bytes(data[i:i + 2], "big")
                for i in range(0, len(data), 2))
    while total >> 16:
        total = (total & 0xFFFF) + (total >> 16)
    return (~total) & 0xFFFF


def dhcp_frame(frame: bytes):
    """Return a DHCP record or None. Reject truncated/malformed DHCP records."""
    if len(frame) < 14 or frame[12:14] != b"\x08\x00":
        return None
    if len(frame) < 34 or frame[14] >> 4 != 4:
        return None
    ihl = (frame[14] & 15) * 4
    if ihl < 20 or len(frame) < 14 + ihl + 8 or frame[23] != 17:
        return None
    iplen = int.from_bytes(frame[16:18], "big")
    # An IPv4 fragment cannot establish a complete BOOTP/DHCP exchange.
    # Reject both MF and nonzero offsets before decoding the UDP header.
    if (int.from_bytes(frame[20:22], "big") & 0x3FFF) or iplen < ihl + 8 or len(frame) < 14 + iplen:
        return None
    udp_start = 14 + ihl
    sport, dport, udplen, udp_checksum = struct.unpack_from(">HHHH", frame, udp_start)
    if (sport, dport) not in ((68, 67), (67, 68)):
        return None
    if udplen < 8 + 240 or udplen > iplen - ihl:
        return None
    bootp = frame[udp_start + 8:udp_start + udplen]
    if len(bootp) < 240 or bootp[1:3] != b"\x01\x06":
        return None
    if bootp[236:240] != b"\x63\x82\x53\x63":
        return None
    # The candidate is a DHCP packet, not arbitrary UDP. Reject corrupted
    # IPv4/UDP wire evidence rather than skipping it and accepting a later
    # valid retransmission. IPv4 UDP checksum 0 is legal (Cosmos emits it);
    # QEMU SLIRP's nonzero reply checksums must validate in full.
    if frame[22] == 0 or checksum16(frame[14:14 + ihl]) != 0:
        raise ValueError("invalid DHCP IPv4 TTL or header checksum")
    if udplen != iplen - ihl:
        raise ValueError("invalid DHCP UDP/IP length")
    if udp_checksum:
        pseudo = frame[26:34] + b"\x00\x11" + udplen.to_bytes(2, "big")
        if checksum16(pseudo + frame[udp_start:udp_start + udplen]) != 0:
            raise ValueError("invalid DHCP UDP checksum")
    opts = {}
    at = 240
    end = False
    while at < len(bootp):
        kind = bootp[at]
        at += 1
        if kind == 255:
            end = True
            break
        if kind == 0:
            continue
        if at >= len(bootp):
            raise ValueError("truncated DHCP option length")
        length = bootp[at]
        at += 1
        if at + length > len(bootp):
            raise ValueError("truncated DHCP option value")
        if kind in opts:
            raise ValueError("duplicate DHCP option")
        opts[kind] = bootp[at:at + length]
        at += length
    if not end or len(opts.get(53, b"")) != 1:
        return None
    typ = opts[53][0]
    if typ not in (1, 2, 3, 5):
        return None
    if typ in (1, 3):
        if bootp[0] != 1 or (sport, dport) != (68, 67):
            return None
    elif bootp[0] != 2 or (sport, dport) != (67, 68):
        return None
    return {
        "type": typ, "xid": bootp[4:8], "chaddr": bootp[28:34],
        "yiaddr": bootp[16:20], "requested": opts.get(50),
        "ciaddr": bootp[12:16], "giaddr": bootp[24:28],
        "server_id": opts.get(54), "subnet": opts.get(1),
        "router": opts.get(3), "dns": opts.get(6),
        "source_mac": frame[6:12], "source_ip": frame[26:30],
        "destination_mac": frame[:6], "destination_ip": frame[30:34],
    }


def validate(serial: str, pcap: bytes) -> str:
    # Reuse the already verified N1 raw TX proof, including its strict PCAP reader.
    n1 = validate_n1(serial, pcap)
    lines = [line.strip() for line in serial.replace("\r", "").splitlines()]
    # The stock Cosmos scheduler can preempt DiagnosticLog.WriteString
    # mid-character. Preserve fail-closed DHCP/PCAP validation while
    # recovering only the observed, exact scheduler insertion pattern:
    #   "... gatewa[SCHED] Tick 6 enabled=1\\n"
    #   "y=10.0.2.2"
    # Never join arbitrary lines or remove unknown diagnostic output.
    for index in range(len(lines) - 1):
        line = lines[index]
        if not line.startswith("[NET3095-N2] DHCP=PASS ip="):
            continue
        match = re.fullmatch(
            r"(\[NET3095-N2\] DHCP=PASS ip=10\.0\.2\.[0-9]{1,3} "
            r"mask=255\.255\.255\.0 gatewa)"
            r"\[SCHED\] Tick [0-9]+ enabled=1", line)
        if match is not None and lines[index + 1] == "y=10.0.2.2":
            lines[index] = match.group(1) + "y=10.0.2.2"
            lines[index + 1] = ""
    if any("[NET3095-N2][FAIL]" in line for line in lines):
        raise ValueError("DHCP probe explicitly failed")
    lease_lines = [m for line in lines if (m := LEASE.fullmatch(line))]
    if lines.count(START) != 1 or len(lease_lines) != 1:
        raise ValueError("missing/duplicate DHCP attempt or lease marker")
    if lines.index(START) >= lines.index(lease_lines[0].group(0)):
        raise ValueError("DHCP markers out of order")
    ip = ipaddress.IPv4Address(lease_lines[0].group(1))
    # The isolated guest explicitly rejects any lease except 10.0.2.15.
    # A different, otherwise valid /24 address must not pass this independent
    # wire gate merely because the serial and PCAP agree with each other.
    if ip != ipaddress.IPv4Address("10.0.2.15"):
        raise ValueError("unexpected QEMU DHCP lease")
    mac = bytes.fromhex(n1.split("mac=")[-1].replace(":", ""))
    records = [r for frame in pcap_frames(pcap) if (r := dhcp_frame(frame))]
    broadcast = b"\xff" * 6
    zero_ip = b"\x00" * 4
    broadcast_ip = b"\xff" * 4
    subnet = bytes([255, 255, 255, 0])
    dns_proxy = bytes([10, 0, 2, 3])
    # Pair DISCOVER/OFFER and REQUEST/ACK separately: stock Cosmos v3.0.95
    # allocates a fresh BOOTP transaction ID for each outbound packet.
    for i, discover in enumerate(records):
        if (discover["type"] != 1 or discover["chaddr"] != mac or
            discover["source_mac"] != mac or discover["destination_mac"] != broadcast or
            discover["source_ip"] != zero_ip or discover["destination_ip"] != broadcast_ip or
            discover["ciaddr"] != zero_ip or discover["giaddr"] != zero_ip or
            discover["yiaddr"] != zero_ip):
            continue
        for j in range(i + 1, len(records)):
            offer = records[j]
            # The BOOTP XID/IP must also belong to a consistent unicast L2 peer.
            server_mac = offer["source_mac"]
            if (offer["type"] != 2 or offer["chaddr"] != mac or
                offer["xid"] != discover["xid"] or offer["source_ip"] != SERVER or
                offer["destination_ip"] != broadcast_ip or
                offer["ciaddr"] != zero_ip or offer["giaddr"] != zero_ip or
                offer["server_id"] != SERVER or offer["subnet"] != subnet or
                offer["router"] != SERVER or offer["dns"] != dns_proxy or
                server_mac in (mac, b"\x00" * 6, broadcast) or
                (server_mac[0] & 1) != 0 or
                offer["destination_mac"] not in (mac, broadcast)):
                continue
            for k in range(j + 1, len(records)):
                request = records[k]
                if (request["type"] != 3 or request["chaddr"] != mac or
                    request["source_mac"] != mac or
                    request["source_ip"] != zero_ip or request["destination_ip"] != broadcast_ip or
                    request["ciaddr"] != zero_ip or request["giaddr"] != zero_ip or
                    request["yiaddr"] != zero_ip or
                    request["destination_mac"] not in (broadcast, server_mac) or
                    request["requested"] != offer["yiaddr"] or
                    request["server_id"] not in (None, SERVER)):
                    continue
                for ack in records[k + 1:]:
                    if (ack["type"] == 5 and ack["chaddr"] == mac and
                        ack["xid"] == request["xid"] and ack["source_ip"] == SERVER and
                        ack["source_mac"] == server_mac and
                        ack["destination_ip"] == broadcast_ip and
                        ack["ciaddr"] == zero_ip and ack["giaddr"] == zero_ip and
                        ack["server_id"] == SERVER and ack["subnet"] == subnet and
                        ack["router"] == SERVER and ack["dns"] == dns_proxy and
                        ack["destination_mac"] in (mac, broadcast) and
                        ack["yiaddr"] == ip.packed and ack["yiaddr"] == offer["yiaddr"]):
                        return "[NET3095-N2-GATE] PASS real QEMU DHCP DISCOVER/OFFER/REQUEST/ACK + guest IPv4 lease " + str(ip)
    raise ValueError("no correlated QEMU DHCP DORA RX/TX proof")


def fake_dhcp(typ: int, xid: int, mac: bytes, ip: bytes, requested: bytes | None = None,
              source: bytes | None = None, server_id: bytes = SERVER,
              subnet: bytes = bytes([255, 255, 255, 0]),
              gateway: bytes = SERVER, dns_proxy: bytes = bytes([10, 0, 2, 3])) -> bytes:
    """Synthetic packet fixture; deliberately not a hardware proof."""
    client = typ in (1, 3)
    bootp = bytearray(240)
    bootp[0:3] = bytes([1 if client else 2, 1, 6])
    bootp[4:8] = xid.to_bytes(4, "big")
    if not client:
        bootp[16:20] = ip
    bootp[28:34] = mac
    bootp[236:240] = bytes.fromhex("63825363")
    options = bytearray([53, 1, typ])
    if not client or (typ == 3 and server_id != SERVER):
        # REQUEST normally omits option 54 in this fixture; explicitly
        # include it when testing rejection of a foreign DHCP server.
        options += bytes([54, 4]) + server_id
    if not client:
        options += bytes([1, 4]) + subnet
        options += bytes([3, 4]) + gateway
        options += bytes([6, 4]) + dns_proxy
    if requested is not None:
        options += bytes([50, 4]) + requested
    options.append(255)
    bootp += options
    sport, dport = (68, 67) if client else (67, 68)
    udp = struct.pack(">HHHH", sport, dport, len(bootp) + 8, 0) + bootp
    src = bytes([0, 0, 0, 0]) if client else SERVER
    dst = bytes([255] * 4)
    iphdr = bytearray(20)
    iphdr[0] = 0x45
    iphdr[2:4] = (20 + len(udp)).to_bytes(2, "big")
    iphdr[8:10] = bytes([64, 17])
    iphdr[12:16], iphdr[16:20] = src, dst
    iphdr[10:12] = checksum16(iphdr).to_bytes(2, "big")
    eth = (bytes([255] * 6) if client else mac) + (mac if client else bytes([0x52, 0x54, 0, 0x12, 0x34, 0x99])) + b"\x08\x00"
    return eth + iphdr + udp


def synthetic_capture(frames: list[bytes]) -> bytes:
    header = struct.pack("<IHHIIII", 0xA1B2C3D4, 2, 4, 0, 0, 65535, 1)
    return header + b"".join(struct.pack("<IIII", i + 1, 0, len(f), len(f)) + f for i, f in enumerate(frames))


def self_test() -> None:
    mac = MAC
    ip = bytes([10, 0, 2, 15])
    raw = bytes([255] * 6) + mac + b"\x88\xb5" + b"ZONDERQ3095TX"
    raw += bytes(60 - len(raw))
    frames = [raw, fake_dhcp(1, 1, mac, ip), fake_dhcp(2, 1, mac, ip),
              fake_dhcp(3, 2, mac, ip, ip), fake_dhcp(5, 2, mac, ip)]
    serial = "\n".join([
        "[NET3095-N1] BOOT", "[NET3095-N1] discovered=1",
        "[NET3095-N1] cpu=bsp adapter=0 name=e1000e mac=52:54:00:12:34:56 ready=yes link=up",
        "[NET3095-N1] PASS READY_LINK=1",
        "[NET3095-N1] PCI_IDENTITY=PASS bdf=00:02.0 vendor=8086 device=10D3 command=0x0006 (read-only; DMA/ISR unproven)",
        "[NET3095-N1] DRIVER_BOUND=PASS node=pci:0000:00:02.0 resources=8 irq_sources=1 handler_faults=0 isr_executions=unknown", "[NET3095-N1] TX_QUEUED=yes",
        "[NET3095-N1] PCI_TX_STABLE=PASS command=0x0006 (read-only; PCAP required for TX proof)",
        "[NET3095-N1] DHCP=NOT_ATTEMPTED IP=UNASSIGNED",
        START, "[NET3095-N2] DHCP=PASS ip=10.0.2.15 mask=255.255.255.0 gateway=10.0.2.2"
    ])
    assert "PASS" in validate(serial, synthetic_capture(frames))
    # Real QEMU run 41603954506: scheduler output split the lease
    # inside "gateway". The PCAP still must prove the complete DORA.
    interleaved = serial.replace(
        "gateway=10.0.2.2",
        "gatewa[SCHED] Tick 6 enabled=1\ny=10.0.2.2")
    assert "PASS" in validate(interleaved, synthetic_capture(frames))
    for corrupted in (
        interleaved.replace("enabled=1", "enabled=0"),
        interleaved.replace("y=10.0.2.2", "y=10.0.2.99"),
        interleaved.replace("Tick 6", "Tick bad"),
    ):
        try:
            validate(corrupted, synthetic_capture(frames))
        except ValueError:
            pass
        else:
            raise AssertionError("accepted unrecognized scheduler/lease interleave")
    try:
        validate(interleaved, synthetic_capture(frames[:-1]))
    except ValueError:
        pass
    else:
        raise AssertionError("accepted interleaved lease without DHCP ACK")
    # A fully self-consistent DORA for another address in the same subnet
    # must fail: the guest's fixed SLIRP profile only permits 10.0.2.15.
    alternate_ip = bytes([10, 0, 2, 16])
    alternate_frames = [raw, fake_dhcp(1, 1, mac, alternate_ip),
                        fake_dhcp(2, 1, mac, alternate_ip),
                        fake_dhcp(3, 2, mac, alternate_ip, alternate_ip),
                        fake_dhcp(5, 2, mac, alternate_ip)]
    alternate_serial = serial.replace("ip=10.0.2.15", "ip=10.0.2.16")
    try:
        validate(alternate_serial, synthetic_capture(alternate_frames))
    except ValueError:
        pass
    else:
        raise AssertionError("accepted unexpected but self-consistent QEMU DHCP lease")

    def with_udp_checksum(frame: bytes) -> bytes:
        changed = bytearray(frame)
        start = 14 + (changed[14] & 15) * 4
        length = int.from_bytes(changed[start + 4:start + 6], "big")
        pseudo = changed[26:34] + b"\x00\x11" + length.to_bytes(2, "big")
        value = checksum16(pseudo + changed[start:start + length])
        changed[start + 6:start + 8] = (value or 0xFFFF).to_bytes(2, "big")
        return bytes(changed)

    # Both IPv4-legal zero UDP checksums and validated nonzero UDP
    # checksums must pass. Real QEMU emits both forms in the same DORA.
    checksummed = [frames[0]] + [with_udp_checksum(frame) for frame in frames[1:]]
    assert "PASS" in validate(serial, synthetic_capture(checksummed))
    assert all(checksum16(frame[14:34]) == 0 for frame in frames[1:])
    for index in range(1, 5):
        changed = bytearray(checksummed[index])
        changed[22] ^= 1  # TTL changed, IP checksum no longer valid.
        try:
            validate(serial, synthetic_capture(checksummed[:index] + [bytes(changed)] +
                                               checksummed[index + 1:]))
        except ValueError:
            pass
        else:
            raise AssertionError("accepted corrupted DHCP IPv4 checksum")
        changed = bytearray(checksummed[index])
        changed[14 + 20 + 8 + 4] ^= 1  # BOOTP xid changed; UDP checksum stale.
        try:
            validate(serial, synthetic_capture(checksummed[:index] + [bytes(changed)] +
                                               checksummed[index + 1:]))
        except ValueError:
            pass
        else:
            raise AssertionError("accepted corrupted DHCP UDP checksum")
    # Reject a corrupted candidate even if a valid duplicate follows it.
    corrupted = bytearray(checksummed[2])
    corrupted[22] ^= 1
    try:
        validate(serial, synthetic_capture(checksummed[:2] +
                                           [bytes(corrupted), checksummed[2]] +
                                           checksummed[3:]))
    except ValueError:
        pass
    else:
        raise AssertionError("accepted corrupted DHCP OFFER preceding valid duplicate")
    # A malformed DHCP candidate must fail closed even when a later
    # retransmission is valid. Corrupt only option bytes, not IP/UDP headers.
    # These fixtures use legal zero UDP checksums and unchanged IP lengths.
    option_start = 14 + 20 + 8 + 240
    for malformed in ("duplicate", "truncated_length", "truncated_value"):
        changed = bytearray(frames[2])
        if malformed == "duplicate":
            # Replace the OFFER's option 1 (subnet) with a second option 53.
            changed[option_start + 3] = 53
        elif malformed == "truncated_length":
            # Replace the final END marker with an option without a length.
            changed[-1] = 54
        else:
            # The final option claims a value beyond the BOOTP payload.
            changed[-1] = 0
            changed[-2] = 54
            changed[-1] = 8
        try:
            validate(serial, synthetic_capture(frames[:2] + [bytes(changed)] + frames[2:]))
        except ValueError:
            pass
        else:
            raise AssertionError("accepted malformed DHCP OFFER before valid retry: " + malformed)

    # Keep the Ethernet payload intact while marking the IPv4 datagram as
    # fragmented: a fail-open DHCP parser must not accept these captures.
    def fragment(frame: bytes, flags_offset: int) -> bytes:
        changed = bytearray(frame)
        changed[20:22] = flags_offset.to_bytes(2, "big")
        return bytes(changed)

    negatives = [
        (serial, frames[:1] + [fragment(frames[1], 0x2000)] + frames[2:]),
        (serial, frames[:2] + [fragment(frames[2], 0x0001)] + frames[3:]),
        (serial, frames[:3] + [fragment(frames[3], 0x2000)] + frames[4:]),
        (serial, frames[:4] + [fragment(frames[4], 0x0001)]),
        (serial.replace(START, ""), frames),
        (serial.replace("DHCP=PASS", "DHCP=FAIL"), frames),
        (serial + "\n[NET3095-N2][FAIL] bad", frames),
        (serial.replace("10.0.2.15", "10.0.2.99"), frames),
        (serial, frames[:-1]),
        (serial, [frames[0], frames[1], frames[3], frames[4]]),
        (serial, [frames[0], frames[2], frames[1], frames[3], frames[4]]),
        (serial, [frames[0], frames[1], frames[2], fake_dhcp(3, 2, mac, ip, bytes([10, 0, 2, 88])), frames[4]]),
        # A DHCP REQUEST directed to a different server cannot complete
        # this SLIRP DORA, even when the requested lease matches.
        (serial, frames[:3] + [fake_dhcp(3, 2, mac, ip, ip, server_id=bytes([10, 0, 2, 99]))] + frames[4:]),
        (serial, [frames[0], frames[1], frames[2], frames[3], fake_dhcp(5, 7, mac, ip)]),
        (serial, [frames[0], frames[1], fake_dhcp(2, 9, mac, ip), frames[3], frames[4]]),
        (serial, [frames[0], frames[1], fake_dhcp(2, 1, bytes([2, 0, 0, 0, 0, 1]), ip), frames[3], frames[4]]),
        # A valid DHCP ACK address is insufficient when the server's
        # configuration options disagree with the guest's lease.
        (serial, frames[:2] + [fake_dhcp(2, 1, mac, ip, server_id=bytes([10, 0, 2, 99]))] + frames[3:]),
        (serial, frames[:2] + [fake_dhcp(2, 1, mac, ip, subnet=bytes([255, 255, 0, 0]))] + frames[3:]),
        (serial, frames[:2] + [fake_dhcp(2, 1, mac, ip, gateway=bytes([10, 0, 2, 99]))] + frames[3:]),
        (serial, frames[:2] + [fake_dhcp(2, 1, mac, ip, dns_proxy=bytes([10, 0, 2, 99]))] + frames[3:]),
        (serial, frames[:4] + [fake_dhcp(5, 2, mac, ip, server_id=bytes([10, 0, 2, 99]))]),
        (serial, frames[:4] + [fake_dhcp(5, 2, mac, ip, subnet=bytes([255, 255, 0, 0]))]),
        (serial, frames[:4] + [fake_dhcp(5, 2, mac, ip, gateway=bytes([10, 0, 2, 99]))]),
        (serial, frames[:4] + [fake_dhcp(5, 2, mac, ip, dns_proxy=bytes([10, 0, 2, 99]))]),
        # Keep valid DHCP payloads but tamper with Ethernet provenance.
        (serial, frames[:4] + [frames[4][:6] + bytes.fromhex("020000000099") + frames[4][12:]]),
        (serial, frames[:2] + [frames[2][:6] + bytes.fromhex("010000000099") + frames[2][12:]] + frames[3:]),
        (serial, frames[:2] + [bytes.fromhex("020000000088") + frames[2][6:]] + frames[3:]),
        (serial, frames[:4] + [bytes.fromhex("020000000088") + frames[4][6:]]),
        (serial, frames[:1] + [bytes.fromhex("020000000088") + frames[1][6:]] + frames[2:]),
        (serial, frames[:3] + [bytes.fromhex("020000000088") + frames[3][6:]] + frames[4:]),
    ]
    for bad_serial, bad_frames in negatives:
        try:
            validate(bad_serial, synthetic_capture(bad_frames))
        except ValueError:
            continue
        raise AssertionError("accepted invalid DHCP RX proof")
    print("[NET3095-N2-CONTRACT] PASS synthetic DORA positive + negative cases")


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
            ap.exit(1, f"[NET3095-N2-GATE][FAIL] {exc}\n")
