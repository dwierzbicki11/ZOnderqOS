#!/usr/bin/env python3
"""Fail-closed TCP reconnect proof after opt-in E1000E QMP link recovery.

Host QMP acceptance and guest serial markers are not sufficient. The PCAP must
contain a NEW handshake, exact distinct TX/RX bytes and correct TCP sequence
numbers after each independent post-link ICMP reply. QEMU only; no ISR claim.
"""
from __future__ import annotations

import argparse
from pathlib import Path

from verify_3095_network_n1 import pcap_frames
from verify_3095_network_n2 import LEASE, MAC, SERVER, synthetic_capture
from verify_3095_network_n4 import PORT, fake_tcp, tcp_frame
from verify_3095_network_n6_final_icmp import final_echo
from verify_3095_network_n6_link_recovery import (
    IDENT, SECOND_IDENT, PASS, SECOND_PASS, fake_recovery,
    validate as validate_link,
)


def reconnect_request(cycle: int) -> bytes:
    return f"ZONDERQ3095-TCP-RECONNECT-{cycle}\n".encode("ascii")


def reconnect_response(cycle: int) -> bytes:
    return f"ZONDERQ3095-TCP-RESTORED-{cycle}\n".encode("ascii")


def check_serial_and_host(serial: str, host: str, cycles: int) -> None:
    if cycles not in (1, 2):
        raise ValueError("invalid reconnect cycle count")
    lines = [line.strip() for line in serial.replace("\r", "").splitlines()]
    host_lines = [line.strip() for line in host.replace("\r", "").splitlines()]
    if any("[NET3095-N6][FAIL]" in line for line in lines) or (
        any("[NET3095-N4-HOST][FAIL]" in line for line in host_lines)
    ):
        raise ValueError("guest or host reported TCP recovery failure")
    if host_lines.count("[NET3095-N4-HOST] PASS request/reply") != 1:
        raise ValueError("initial TCP host exchange missing or duplicated")
    previous = -1
    for cycle in range(1, cycles + 1):
        icmp = PASS if cycle == 1 else SECOND_PASS
        start = f"[NET3095-N6] LINK_TCP_ATTEMPT=START cycle={cycle}"
        done = f"[NET3095-N6] LINK_TCP=PASS cycle={cycle} bytes={len(reconnect_response(cycle))}"
        host_done = f"[NET3095-N4-HOST] RECONNECT_PASS cycle={cycle}"
        if lines.count(icmp) != 1 or lines.count(start) != 1 or lines.count(done) != 1:
            raise ValueError(f"missing/duplicate guest TCP recovery cycle={cycle}")
        if host_lines.count(host_done) != 1:
            raise ValueError(f"missing/duplicate host TCP recovery cycle={cycle}")
        if not previous < lines.index(icmp) < lines.index(start) < lines.index(done):
            raise ValueError(f"TCP recovery serial ordering invalid cycle={cycle}")
        previous = lines.index(done)
    if cycles == 1 and any("LINK_TCP_ATTEMPT=START cycle=2" in line for line in lines):
        raise ValueError("unexpected second TCP recovery cycle")


def check_tcp_wire(frames: list[bytes], guest: bytes, cycles: int) -> None:
    if cycles not in (1, 2):
        raise ValueError("invalid reconnect cycle count")
    packets = [(i, p) for i, frame in enumerate(frames)
               if (p := tcp_frame(frame)) and (p["sport"] == PORT or p["dport"] == PORT)]
    for cycle in range(1, cycles + 1):
        ident = IDENT if cycle == 1 else SECOND_IDENT
        echo = [(i, p) for i, frame in enumerate(frames)
                if (p := final_echo(frame, ident_expected=ident,
                                   seq_expected=cycle))]
        if len(echo) != 2 or echo[0][1]["type"] != 8 or echo[1][1]["type"] != 0:
            raise ValueError(f"missing recovery ICMP wire pair cycle={cycle}")
        lower = echo[1][0]
        upper = len(frames)
        if cycle == 1 and cycles == 2:
            next_echo = [(i, p) for i, frame in enumerate(frames)
                         if (p := final_echo(frame, ident_expected=SECOND_IDENT,
                                            seq_expected=2))]
            if len(next_echo) != 2:
                raise ValueError("missing second ICMP request")
            upper = next_echo[0][0]
        request = reconnect_request(cycle)
        response = reconnect_response(cycle)
        tx = [(i, p) for i, p in packets if p["payload"] == request]
        rx = [(i, p) for i, p in packets if p["payload"] == response]
        if len(tx) != 1 or len(rx) != 1:
            raise ValueError(f"missing/duplicate distinct TCP TX/RX cycle={cycle}")
        req_i, req = tx[0]
        res_i, res = rx[0]
        if not lower < req_i < res_i < upper:
            raise ValueError(f"TCP data not after matching link recovery cycle={cycle}")
        if not (req["src"] == guest and req["dst"] == SERVER and
                req["src_mac"] == MAC and req["dport"] == PORT and
                res["src"] == SERVER and res["dst"] == guest and
                res["dst_mac"] == MAC and res["sport"] == PORT and
                res["dport"] == req["sport"] and res["src_mac"] == req["dst_mac"] and
                res["src_mac"] != MAC and not (res["src_mac"][0] & 1) and
                (req["flags"] & 0x17) == 0x10 and
                (res["flags"] & 0x17) == 0x10):
            raise ValueError(f"TCP reconnect endpoints/MAC/flags mismatch cycle={cycle}")
        handshakes = 0
        for syn_i, syn in packets:
            if not (lower < syn_i < req_i and syn["src"] == guest and
                    syn["dst"] == SERVER and syn["src_mac"] == MAC and
                    syn["dst_mac"] == res["src_mac"] and
                    syn["sport"] == req["sport"] and syn["dport"] == PORT and
                    (syn["flags"] & 0x17) == 0x02 and not syn["payload"]):
                continue
            for synack_i, synack in packets:
                if not (syn_i < synack_i < req_i and
                        synack["src"] == SERVER and synack["dst"] == guest and
                        synack["src_mac"] == res["src_mac"] and synack["dst_mac"] == MAC and
                        synack["sport"] == PORT and synack["dport"] == req["sport"] and
                        (synack["flags"] & 0x17) == 0x12 and not synack["payload"] and
                        synack["ack"] == (syn["seq"] + 1) & 0xFFFFFFFF):
                    continue
                for ack_i, ack in packets:
                    if not (synack_i < ack_i < req_i and
                            ack["src"] == guest and ack["dst"] == SERVER and
                            ack["src_mac"] == MAC and ack["dst_mac"] == res["src_mac"] and
                            ack["sport"] == req["sport"] and ack["dport"] == PORT and
                            (ack["flags"] & 0x17) == 0x10 and not ack["payload"] and
                            ack["seq"] == (syn["seq"] + 1) & 0xFFFFFFFF and
                            ack["ack"] == (synack["seq"] + 1) & 0xFFFFFFFF and
                            req["seq"] == ack["seq"] and req["ack"] == ack["ack"] and
                            res["seq"] == (synack["seq"] + 1) & 0xFFFFFFFF and
                            res["ack"] == (req["seq"] + len(request)) & 0xFFFFFFFF):
                        continue
                    handshakes += 1
        if handshakes != 1:
            raise ValueError(f"missing/ambiguous fresh TCP handshake cycle={cycle}")


def validate(serial: str, pcap: bytes, host: str, cycles: int) -> str:
    validate_link(serial, pcap, cycles)
    check_serial_and_host(serial, host, cycles)
    leases = [m for line in serial.replace("\r", "").splitlines()
              if (m := LEASE.fullmatch(line.strip()))]
    if len(leases) != 1:
        raise ValueError("missing unique guest DHCP lease")
    guest = bytes(int(part) for part in leases[0].group(1).split("."))
    check_tcp_wire(list(pcap_frames(pcap)), guest, cycles)
    return ("[NET3095-N6-TCP-RECOVERY-GATE] PASS cycles=" + str(cycles) +
            " fresh TCP handshakes and distinct request/reply bytes in QEMU PCAP; ISR UNPROVEN")


def self_test() -> None:
    guest = bytes((10, 0, 2, 15))
    frames = []
    serial_lines = []
    host_lines = ["[NET3095-N4-HOST] PASS request/reply"]
    for cycle in (1, 2):
        ident = IDENT if cycle == 1 else SECOND_IDENT
        frames.extend((fake_recovery(False, guest, ident, cycle),
                       fake_recovery(True, guest, ident, cycle)))
        seq, peer_seq, port = 1000 * cycle, 5000 * cycle, 49152 + cycle
        req, res = reconnect_request(cycle), reconnect_response(cycle)
        frames.extend((
            fake_tcp(False, 0x02, seq, 0, sport=port),
            fake_tcp(True, 0x12, peer_seq, seq + 1, sport=port),
            fake_tcp(False, 0x10, seq + 1, peer_seq + 1, sport=port),
            fake_tcp(False, 0x18, seq + 1, peer_seq + 1, req, sport=port),
            fake_tcp(True, 0x18, peer_seq + 1, seq + 1 + len(req), res, sport=port),
        ))
        serial_lines.extend((
            PASS if cycle == 1 else SECOND_PASS,
            f"[NET3095-N6] LINK_TCP_ATTEMPT=START cycle={cycle}",
            f"[NET3095-N6] LINK_TCP=PASS cycle={cycle} bytes={len(res)}",
        ))
        host_lines.append(f"[NET3095-N4-HOST] RECONNECT_PASS cycle={cycle}")
    serial, host = "\n".join(serial_lines), "\n".join(host_lines)
    check_serial_and_host(serial, host, 2)
    check_tcp_wire(frames, guest, 2)
    check_serial_and_host("\n".join(serial_lines[:3]), "\n".join(host_lines[:2]), 1)
    check_tcp_wire(frames[:7], guest, 1)
    assert len(list(pcap_frames(synthetic_capture(frames)))) == len(frames)
    bad_frames = (
        frames[:-1], frames + [frames[-1]],
        frames[:2] + frames[3:],
        frames[:3] + frames[4:],
        frames[:4] + frames[5:],
        frames[:5] + frames[6:],
        frames[:6] + frames[7:],
        frames[:7] + frames[8:],
        frames[:7] + frames[8:9] + frames[7:8] + frames[9:],
        frames[:-1] + [frames[-1][:-1] + bytes([frames[-1][-1] ^ 1])],
    )
    for invalid in bad_frames:
        try:
            check_tcp_wire(invalid, guest, 2)
        except ValueError:
            continue
        raise AssertionError("accepted missing/reordered/duplicate/corrupt TCP proof")
    for invalid_serial, invalid_host in (
        (serial.replace("LINK_TCP=PASS cycle=1", "LINK_TCP=FAIL cycle=1"), host),
        (serial + "\n" + serial_lines[-1], host),
        (serial, host.replace("RECONNECT_PASS cycle=2", "RECONNECT_FAIL cycle=2")),
        (serial, host + "\n" + host_lines[-1]),
        (serial.replace(serial_lines[1] + "\n" + serial_lines[2],
                        serial_lines[2] + "\n" + serial_lines[1]), host),
    ):
        try:
            check_serial_and_host(invalid_serial, invalid_host, 2)
        except ValueError:
            continue
        raise AssertionError("accepted invalid TCP reconnect serial/host markers")
    print("[NET3095-N6-TCP-RECOVERY-CONTRACT] PASS synthetic TCP/ICMP/serial negative cases only")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("serial", nargs="?", type=Path)
    parser.add_argument("pcap", nargs="?", type=Path)
    parser.add_argument("host", nargs="?", type=Path)
    parser.add_argument("--cycles", type=int, choices=(1, 2), default=1)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
    else:
        if args.serial is None or args.pcap is None or args.host is None:
            parser.error("real QEMU serial, filter-dump PCAP and host evidence required")
        try:
            print(validate(args.serial.read_text(errors="replace"),
                           args.pcap.read_bytes(),
                           args.host.read_text(errors="replace"), args.cycles))
        except (OSError, ValueError) as exc:
            parser.exit(1, f"[NET3095-N6-TCP-RECOVERY-GATE][FAIL] {exc}\n")
