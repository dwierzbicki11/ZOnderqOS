#!/usr/bin/env python3
"""Strict N1 serial + real QEMU PCAP proof. Synthetic self-tests are not boot proof."""
from __future__ import annotations

import argparse
from pathlib import Path
import re
import struct

BOOT = "[NET3095-N1] BOOT"
END = "[NET3095-N1] DHCP=NOT_ATTEMPTED IP=UNASSIGNED"
TX = "[NET3095-N1] TX_QUEUED=yes"
DEVICE = re.compile(
    r"^\[NET3095-N1\] cpu=bsp adapter=(\d+) name=e1000e "
    r"mac=([0-9a-fA-F]{2}(?::[0-9a-fA-F]{2}){5}) "
    r"ready=yes link=up$"
)
DISCOVER = re.compile(r"^\[NET3095-N1\] discovered=(\d+)$")
ACCEPT = re.compile(r"^\[NET3095-N1\] PASS READY_LINK=(\d+)$")
PAYLOAD = b"ZONDERQ3095TX"
ETHER_TYPE = b"\x88\xb5"
BROADCAST = b"\xff" * 6


def validate_serial(data: str) -> bytes:
    lines = [line.strip() for line in data.replace("\r", "").splitlines()]
    if any(re.search(r"kernel panic|fatal:\s*exception|page fault", line, re.I)
           or "[NET3095-N1][FAIL]" in line
           or "[SMP3094-MANAGED-STAGE23] PASS" in line
           for line in lines):
        raise ValueError("kernel fault, probe failure or unrelated SMP marker")
    if lines.count(BOOT) != 1 or lines.count(END) != 1 or lines.count(TX) != 1:
        raise ValueError("missing or repeated BOOT, TX or END marker")
    found = [m for line in lines if (m := DISCOVER.fullmatch(line))]
    ready = [m for line in lines if (m := ACCEPT.fullmatch(line))]
    devices = [m for line in lines if (m := DEVICE.fullmatch(line))]
    if len(found) != 1 or len(ready) != 1 or len(devices) != 1:
        raise ValueError("expected one inventory, ready count and E1000E")
    count = int(found[0].group(1))
    ready_count = int(ready[0].group(1))
    index = int(devices[0].group(1))
    if not 1 <= count <= 8 or not 1 <= ready_count <= count or index >= count:
        raise ValueError("invalid NIC count or registration index")
    mac = bytes.fromhex(devices[0].group(2).replace(":", ""))
    if mac == b"\x00" * 6 or mac == BROADCAST or (mac[0] & 1):
        raise ValueError("invalid unicast MAC")
    offsets = [lines.index(BOOT), lines.index(found[0].group(0)),
               lines.index(devices[0].group(0)), lines.index(ready[0].group(0)),
               lines.index(TX), lines.index(END)]
    if offsets != sorted(set(offsets)):
        raise ValueError("out-of-order or duplicated probe markers")
    return mac


def pcap_frames(data: bytes):
    if len(data) < 24:
        raise ValueError("missing PCAP header")
    magic = data[:4]
    if magic in (b"\xd4\xc3\xb2\xa1", b"\x4d\x3c\xb2\xa1"):
        endian = "<"
    elif magic in (b"\xa1\xb2\xc3\xd4", b"\xa1\xb2\x3c\x4d"):
        endian = ">"
    else:
        raise ValueError("invalid PCAP magic")
    major, minor, _tz, _sig, snaplen, linktype = struct.unpack(
        endian + "HHIIII", data[4:24]
    )
    if (major, minor) != (2, 4) or linktype != 1 or snaplen < 60:
        raise ValueError("expected Ethernet PCAP v2.4")
    offset = 24
    while offset < len(data):
        if len(data) - offset < 16:
            raise ValueError("truncated PCAP record header")
        _sec, _usec, captured, original = struct.unpack(
            endian + "IIII", data[offset:offset + 16]
        )
        offset += 16
        if captured > snaplen or original < captured or captured > 65535:
            raise ValueError("invalid PCAP record length")
        if offset + captured > len(data):
            raise ValueError("truncated PCAP frame")
        yield data[offset:offset + captured]
        offset += captured


def validate(serial: str, pcap: bytes) -> str:
    mac = validate_serial(serial)
    wanted = BROADCAST + mac + ETHER_TYPE + PAYLOAD
    # Consume every record before accepting: a matching first frame must not
    # hide a truncated or malformed tail later in the capture.
    frames = list(pcap_frames(pcap))
    if not any(frame.startswith(wanted) and len(frame) >= 60
               for frame in frames):
        raise ValueError("no real QEMU-captured E1000E TX frame")
    return f"[NET3095-N1-GATE] PASS E1000E_LINK_AND_RAW_TX mac={mac.hex(':')}"


def synthetic_pcap(frame: bytes) -> bytes:
    header = struct.pack("<IHHIIII", 0xA1B2C3D4, 2, 4, 0, 0, 65535, 1)
    record = struct.pack("<IIII", 1, 0, len(frame), len(frame))
    return header + record + frame


def self_test() -> None:
    serial = "\n".join([
        BOOT,
        "[NET3095-N1] discovered=1",
        "[NET3095-N1] cpu=bsp adapter=0 name=e1000e mac=52:54:00:12:34:56 ready=yes link=up",
        "[NET3095-N1] PASS READY_LINK=1",
        TX, END,
    ])
    frame = BROADCAST + bytes.fromhex("525400123456") + ETHER_TYPE + PAYLOAD
    frame += b"\x00" * (60 - len(frame))
    good = synthetic_pcap(frame)
    assert "PASS" in validate(serial, good)
    bad_serial = [
        serial.replace(BOOT, ""),
        serial + "\n" + BOOT,
        serial.replace("discovered=1", "discovered=0"),
        serial.replace("name=e1000e", "name=virtio-net"),
        serial.replace("mac=52:54:00:12:34:56", "mac=00:00:00:00:00:00"),
        serial.replace("mac=52:54:00:12:34:56", "mac=53:54:00:12:34:56"),
        serial.replace("ready=yes", "ready=no"),
        serial.replace("link=up", "link=down"),
        serial.replace("READY_LINK=1", "READY_LINK=0"),
        serial.replace(TX, "[NET3095-N1] TX_QUEUED=no"),
        serial.replace(TX + "\n", ""),
        serial.replace(END, ""),
        serial + "\nKERNEL PANIC",
        serial + "\n[NET3095-N1][FAIL] TX timeout",
        serial.replace(TX + "\n", "") + "\n" + TX,
    ]
    for sample in bad_serial:
        try:
            validate(sample, good)
        except ValueError:
            pass
        else:
            raise AssertionError("accepted invalid serial evidence")
    bad_pcap = [
        b"", good[:10], good[:-1], good[:24],
        synthetic_pcap(frame.replace(PAYLOAD, b"ZONDERQ3095RX")),
        synthetic_pcap(b"\x00" * 6 + frame[6:]),
        synthetic_pcap(frame[:6] + b"\x00" * 6 + frame[12:]),
        synthetic_pcap(frame[:12] + b"\x08\x00" + frame[14:]),
        good[:20] + b"\x00\x00\x00\x00" + good[24:],
        good + b"\x00",  # Corrupt tail after an otherwise matching frame.
    ]
    for data in bad_pcap:
        try:
            validate(serial, data)
        except ValueError:
            pass
        else:
            raise AssertionError("accepted invalid PCAP evidence")
    print("[NET3095-N1-CONTRACT] PASS synthetic serial/PCAP positive + negative cases")


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
            print(validate(args.serial_log.read_text(errors="replace"),
                           args.pcap.read_bytes()))
        except ValueError as exc:
            ap.exit(1, f"[NET3095-N1-GATE][FAIL] {exc}\n")
