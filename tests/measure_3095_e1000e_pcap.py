#!/usr/bin/env python3
"""QEMU-only Ethernet timing telemetry. Never an IRQ/ISR or physical NIC proof.

Run only after the independent fail-closed N1-N6 PCAP validators. Measures
capture-side HTTP response span and ICMP request/reply deltas, not guest ISR
latency, DMA ownership, or network throughput.
"""
from __future__ import annotations

import argparse
from collections import defaultdict
from pathlib import Path
import re
import struct

STATS = re.compile(r'^\[NET3095-N6\] HTTP_RX_STATS attempt=(\d+) reads=(\d+) max_chunk=(\d+) bytes=(\d+)$')
TIMING = re.compile(r'^\[NET3095-N6\] HTTP_RX_TIMING attempt=(\d+) first_byte_ms=(\d+) total_ms=(\d+) \(socket Read; ISR unproven\)$')


def records(data: bytes):
    if len(data) < 24:
        raise ValueError('missing PCAP header')
    magic = data[:4]
    if magic in (b'\xd4\xc3\xb2\xa1', b'\x4d\x3c\xb2\xa1'):
        endian = '<'
    elif magic in (b'\xa1\xb2\xc3\xd4', b'\xa1\xb2\x3c\x4d'):
        endian = '>'
    else:
        raise ValueError('invalid PCAP magic')
    nanos = magic in (b'\x4d\x3c\xb2\xa1', b'\xa1\xb2\x3c\x4d')
    major, minor, _tz, _sig, snaplen, linktype = struct.unpack_from(endian+'HHIIII', data, 4)
    if (major, minor, linktype) != (2, 4, 1) or snaplen < 60:
        raise ValueError('expected full Ethernet PCAP v2.4')
    pos = 24
    while pos < len(data):
        if len(data) - pos < 16:
            raise ValueError('truncated PCAP record header')
        sec, sub, captured, original = struct.unpack_from(endian+'IIII', data, pos)
        pos += 16
        if captured < 14 or captured > snaplen or captured != original or pos+captured > len(data):
            raise ValueError('truncated or invalid PCAP record')
        if sub >= (1_000_000_000 if nanos else 1_000_000):
            raise ValueError('invalid PCAP timestamp fraction')
        frame = data[pos:pos+captured]
        pos += captured
        yield sec + sub/(1_000_000_000 if nanos else 1_000_000), frame


def decode(time: float, frame: bytes):
    if len(frame) < 34 or frame[12:14] != b'\x08\x00' or frame[14] >> 4 != 4:
        return None
    ihl = (frame[14] & 15)*4
    if ihl < 20 or len(frame) < 14+ihl:
        return None
    length = struct.unpack_from('!H', frame, 16)[0]
    if length < ihl or 14+length > len(frame):
        return None
    proto = frame[23]
    start = 14+ihl
    src, dst = frame[26:30], frame[30:34]
    if proto == 1 and length >= ihl+8:
        return ('icmp', time, src, dst, frame[start],
                struct.unpack_from('!HH', frame, start+4))
    if proto == 6 and length >= ihl+20:
        sport, dport, seq = struct.unpack_from('!HHI', frame, start)
        tcp_hlen = (frame[start+12] >> 4)*4
        if tcp_hlen < 20 or length < ihl+tcp_hlen:
            return None
        return ('tcp', time, src, dst, sport, dport, seq, length-ihl-tcp_hlen)
    return None


def burst_capture_stats(pcap: bytes) -> tuple[int, float]:
    """QEMU PCAP paced TX telemetry; not a peak NIC throughput benchmark."""
    tag = b'ZONDERQ3095-BURST-'
    lengths = (60, 128, 512, 1024, 1514)
    observed = [(stamp, frame) for stamp, frame in records(pcap)
                if frame[12:14] == bytes((0x88, 0xB5)) and
                frame[14:14 + len(tag)] == tag]
    if len(observed) != 128:
        raise ValueError(f'expected 128 raw TX frames, found {len(observed)}')
    for sequence, (_, frame) in enumerate(observed):
        # Require the on-wire sequence, not just a plausible length. Otherwise
        # reordered/duplicated frames of the same size can appear valid.
        expected_prefix = tag + f'{sequence:03d}'.encode('ascii')
        if frame[14:14 + len(expected_prefix)] != expected_prefix:
            raise ValueError(f'raw TX sequence mismatch at {sequence}')
        if len(frame) != lengths[sequence % len(lengths)]:
            raise ValueError(f'raw TX frame length mismatch at {sequence}')
    span_ms = (observed[-1][0] - observed[0][0]) * 1000
    if span_ms <= 0:
        raise ValueError('raw TX PCAP timestamps not increasing')
    return sum(len(frame) for _, frame in observed), round(span_ms, 3)


def measure(pcap: bytes, serial: str):
    http = defaultdict(list)
    icmp_requests = {}
    icmp_rtts = []
    for time, frame in records(pcap):
        packet = decode(time, frame)
        if packet is None:
            continue
        if packet[0] == 'tcp':
            _, _, src, dst, sport, dport, seq, size = packet
            if sport == 18096 and size > 0 and src == b'\x0a\x00\x02\x02' and dst == b'\x0a\x00\x02\x0f':
                http[dport].append((time, seq, size))
        elif packet[0] == 'icmp':
            _, _, src, dst, kind, key = packet
            if kind == 8 and src == b'\x0a\x00\x02\x0f' and dst == b'\x0a\x00\x02\x02':
                icmp_requests[key] = time
            elif kind == 0 and src == b'\x0a\x00\x02\x02' and dst == b'\x0a\x00\x02\x0f' and key in icmp_requests:
                icmp_rtts.append((key, round((time-icmp_requests.pop(key))*1000, 3)))
    if len(http) != 6:
        raise ValueError(f'expected six HTTP response flows, found {len(http)}')
    lines = [line.strip() for line in serial.replace('\r', '').splitlines()]
    stats = [m for line in lines if (m := STATS.fullmatch(line))]
    timings = [m for line in lines if (m := TIMING.fullmatch(line))]
    if len(stats) != 6 or (timings and len(timings) != 6):
        raise ValueError('missing or malformed guest HTTP RX telemetry')
    for attempt, (port, segments) in enumerate(sorted(http.items(), key=lambda item: min(x[0] for x in item[1])), 1):
        unique = {(seq, size) for _, seq, size in segments}
        if sum(size for _, size in unique) != 4202:
            raise ValueError(f'HTTP response {attempt} has incomplete/duplicate payload')
        span_ms = round((max(t for t, _, _ in segments)-min(t for t, _, _ in segments))*1000, 2)
        m = stats[attempt-1]
        if int(m.group(1)) != attempt or int(m.group(4)) != 4202:
            raise ValueError('guest RX statistics do not match PCAP flow count')
        guest = ''
        if timings:
            t = timings[attempt-1]
            if int(t.group(1)) != attempt:
                raise ValueError('guest RX timing attempt order invalid')
            guest = f' guest_first_read_ms={t.group(2)} guest_total_read_ms={t.group(3)}'
        print(f'[NET3095-E1000E-METRICS] HTTP attempt={attempt} port={port} '
              f'wire_response_span_ms={span_ms} unique_segments={len(unique)} '
              f'guest_reads={m.group(2)}{guest} ISR=UNPROVEN')
    print('[NET3095-E1000E-METRICS] ICMP wire_RTT_ms=' +
          ','.join(f'{ident:04x}:{seq}:{rtt}' for (ident, seq), rtt in icmp_rtts) +
          ' ISR=UNPROVEN')
    raw_bytes, raw_span_ms = burst_capture_stats(pcap)
    paced_rate_mbps = round(raw_bytes * 8 / (raw_span_ms * 1000), 3)
    print(f'[NET3095-E1000E-METRICS] paced_raw_TX_frames=128 '
          f'bytes={raw_bytes} capture_span_ms={raw_span_ms} '
          f'capture_rate_Mbps={paced_rate_mbps} (5ms pacing; NOT peak NIC throughput) '
          'ISR=UNPROVEN')
    return len(http), len(icmp_rtts)


def self_test():
    frame = b'\xff'*6 + b'\x52\x54\x00\x12\x34\x56' + b'\x08\x00'
    ip = bytearray(20)
    ip[0] = 0x45
    ip[9] = 6
    ip[12:16] = b'\x0a\x00\x02\x02'
    ip[16:20] = b'\x0a\x00\x02\x0f'
    tcp = bytearray(20)
    struct.pack_into('!HHI', tcp, 0, 18096, 49153, 42)
    tcp[12] = 0x50
    payload = b'x'*20
    struct.pack_into('!H', ip, 2, len(ip)+len(tcp)+len(payload))
    packet = frame + ip + tcp + payload
    header = struct.pack('<IHHIIII', 0xa1b2c3d4, 2, 4, 0, 0, 65535, 1)
    rec = struct.pack('<IIII', 1, 1000, len(packet), len(packet)) + packet
    assert list(records(header+rec))[0][1] == packet
    assert decode(1.001, packet)[-1] == 20
    for bad in (b'', header+rec[:-1], header+rec+b'\x00', header[:4]+b'\x00'*20+rec):
        try:
            list(records(bad))
        except ValueError:
            continue
        raise AssertionError('accepted malformed PCAP')
    # Synthetic PCAP with 5ms spacing. This is a parser test, not guest data.
    prefix = bytes((255,)) * 6 + bytes((0x52, 0x54, 0, 0x12, 0x34, 0x56, 0x88, 0xB5))
    tag = b'ZONDERQ3095-BURST-'
    lengths = (60, 128, 512, 1024, 1514)
    def capture(indices, step_us=5000):
        output = bytearray(header)
        for position, index in enumerate(indices):
            raw = prefix + tag + f'{index:03d}'.encode('ascii')
            frame = raw + bytes(lengths[index % len(lengths)] - len(raw))
            output += struct.pack('<IIII', 1, position * step_us,
                                  len(frame), len(frame)) + frame
        return bytes(output)
    total, span = burst_capture_stats(capture(range(128)))
    assert total == 81650 and span == 635.0
    for broken in (capture(range(127)), capture(range(129)),
                   capture(range(128), step_us=0),
                   capture([0, 2, 1] + list(range(3, 128))),
                   capture([5, 1, 2, 3, 4, 0] + list(range(6, 128))),
                   capture([0, 1, 2, 3, 4, 0] + list(range(6, 128)))):
        try:
            burst_capture_stats(broken)
        except ValueError:
            continue
        raise AssertionError('accepted invalid raw TX timing/length capture')
    print('[NET3095-E1000E-METRICS-CONTRACT] PASS parser synthetic cases; no QEMU proof')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('serial', type=Path, nargs='?')
    parser.add_argument('pcap', type=Path, nargs='?')
    parser.add_argument('--self-test', action='store_true')
    args = parser.parse_args()
    if args.self_test:
        self_test()
    else:
        if args.serial is None or args.pcap is None:
            parser.error('real QEMU serial and PCAP required')
        try:
            measure(args.pcap.read_bytes(), args.serial.read_text(errors='replace'))
        except (OSError, ValueError) as exc:
            parser.exit(1, f'[NET3095-E1000E-METRICS][FAIL] {exc}\n')
