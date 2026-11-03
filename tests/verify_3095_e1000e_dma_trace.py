#!/usr/bin/env python3
"""QEMU-side E1000E descriptor DMA trace gate for the isolated N6 profile.

The independent PCAP/serial/host gates establish network packet exchange.
These device-side trace events add evidence that QEMU fetched TX descriptors
and wrote RX packet buffers/descriptors into guest memory. They do NOT prove
that the guest consumed a descriptor, that a physical NIC works, or that an
interrupt service routine executed. Never use this on production GUI boots.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import re

PREFIX = r"(?:[0-9]+@[0-9]+(?:\.[0-9]+)?:)?"
HEX = r"(?:0x)?[0-9a-fA-F]+"
TX = re.compile(r"^" + PREFIX + r"e1000e_tx_descr (0x[0-9a-fA-F]+) : (" + HEX + r") (" + HEX + r")$")
RX_BUFFER = re.compile(
    r"^" + PREFIX + r"e1000e_rx_desc_buff_write buffer #(\d+), addr: 0x([0-9a-fA-F]+), "
    r"offset: (\d+), from: (?:0x[0-9a-fA-F]+|\(nil\)), length: (\d+)$"
)
RX_COMMIT = re.compile(
    r"^" + PREFIX + r"e1000e_rx_written_to_guest "
    # QEMU's trace format changed from an ICR-cause summary to a queue
    # index. Both represent the same RX guest-memory completion event.
    r"Received packet written to guest \((?:ICR causes \d+|queue [01])\)$"
)
TERMINATED = re.compile(r"^qemu-system-[^:]+: terminating on signal ")


def count_dma(stderr: str) -> tuple[int, int, int, int]:
    """Return TX descriptor reads, RX buffer writes, RX bytes, RX commits."""
    tx = writes = rx_bytes = commits = 0
    pending_rx_writes = 0
    for line in stderr.replace("\r", "").splitlines():
        line = line.strip()
        if TERMINATED.match(line):
            break
        match = TX.fullmatch(line)
        if match:
            # A null descriptor buffer address is not packet DMA evidence.
            if 0 < int(match.group(1), 16) <= 0xFFFFFFFFFFFFFFFF:
                tx += 1
            continue
        match = RX_BUFFER.fullmatch(line)
        if match:
            index, address, offset, length = (int(part, 16 if n == 1 else 10)
                                               for n, part in enumerate(match.groups()))
            # QEMU packet-split descriptor slots are 0..3; ignore zero-length
            # writes and null guest physical addresses.
            if (index <= 3 and address != 0 and length > 0
                    and address + offset + length - 1 <= 0xFFFFFFFFFFFFFFFF):
                writes += 1
                pending_rx_writes += 1
                rx_bytes += length
            continue
        match = RX_COMMIT.fullmatch(line)
        if match:
            # The ICR cause can be zero when interrupts are masked or
            # coalesced; the event itself reports a completed guest write.
            # IRQ notification is validated independently. A commit
            # without an earlier guest-buffer write is not RX DMA proof.
            if pending_rx_writes:
                commits += 1
                pending_rx_writes = 0
    return tx, writes, rx_bytes, commits


def report(stderr: str, min_tx: int = 128, min_rx_commits: int = 1) -> str:
    if min_tx < 1:
        raise ValueError("minimum TX descriptor count must be positive")
    if min_rx_commits < 1:
        raise ValueError("minimum RX commit count must be positive")
    tx, writes, rx_bytes, commits = count_dma(stderr)
    if tx < min_tx:
        raise ValueError(f"QEMU TX descriptor reads {tx} < {min_tx}")
    if not writes or not rx_bytes or not commits:
        raise ValueError("missing QEMU RX guest-buffer DMA write or RX commit")
    if commits < min_rx_commits:
        raise ValueError(f"QEMU RX guest commits {commits} < {min_rx_commits}")
    return (f"[NET3095-E1000E-DMA-TRACE] PASS tx_desc={tx} "
            f"rx_buffer_writes={writes} rx_bytes={rx_bytes} rx_commits={commits}; "
            "QEMU descriptor/buffer evidence only, guest ISR UNPROVEN")


def self_test() -> None:
    tx = "7@100.123:e1000e_tx_descr 0x7f001000 : 1000 1\n"
    write = ("7@100.124:e1000e_rx_desc_buff_write buffer #0, "
             "addr: 0x7f002000, offset: 0, from: 0x1234, length: 64\n")
    commit = ("7@100.125:e1000e_rx_written_to_guest "
              "Received packet written to guest (ICR causes 128)\n")
    trace = tx * 128 + write + commit
    assert count_dma(trace) == (128, 1, 64, 1)
    assert "PASS tx_desc=128" in report(trace)
    assert "PASS tx_desc=128" in report(trace, min_tx=128)
    # N6 performs DHCP, ICMP, DNS and repeated TCP/HTTP receives. One
    # RX completion could come from DHCP alone; the real N6 QEMU gate
    # therefore requires multiple independent RX guest-memory commits.
    assert "rx_commits=8" in report(trace * 8, min_rx_commits=8)
    try:
        report(trace * 7, min_rx_commits=8)
    except ValueError as exc:
        assert "RX guest commits 7 < 8" in str(exc)
    else:
        raise AssertionError("accepted insufficient N6 RX DMA completions")
    # Eight RX buffer writes can belong to a single packet/commit. Do not
    # mistake descriptor writes for eight independent completed RX packets.
    grouped = tx * 128 + write * 8 + commit
    assert count_dma(grouped) == (128, 8, 512, 1)
    try:
        report(grouped, min_rx_commits=8)
    except ValueError as exc:
        assert "RX guest commits 1 < 8" in str(exc)
    else:
        raise AssertionError("counted RX buffer writes as independent RX commits")
    # An orphan completion cannot be used to inflate the RX packet count.
    assert count_dma(tx * 128 + write + commit * 8) == (128, 1, 64, 1)
    try:
        report(tx * 128 + write + commit * 8, min_rx_commits=8)
    except ValueError as exc:
        assert "RX guest commits 1 < 8" in str(exc)
    else:
        raise AssertionError("counted orphan RX completions as completed packets")
    assert count_dma(tx * 128 + write + commit.replace("causes 128", "causes 0")) == (128, 1, 64, 1)
    assert count_dma(tx * 128 + write + commit.replace(
        "(ICR causes 128)", "(queue 0)")) == (128, 1, 64, 1)
    assert count_dma(tx * 128 + write + commit.replace(
        "(ICR causes 128)", "(queue 1)")) == (128, 1, 64, 1)
    assert count_dma(tx * 128 + commit + write) == (128, 1, 64, 0)
    # QEMU prints descriptor buffer addresses in hexadecimal. Reject values
    # outside the unsigned 64-bit DMA address space rather than counting
    # an impossible device fetch as real TX evidence.
    oversized_tx = tx.replace("0x7f001000", "0x10000000000000000")
    assert count_dma(oversized_tx * 128 + write + commit) == (0, 1, 64, 1)
    try:
        report(oversized_tx * 128 + write + commit)
    except ValueError as exc:
        assert "TX descriptor reads 0 < 128" in str(exc)
    else:
        raise AssertionError("accepted out-of-range TX descriptor DMA address")
    # A malformed trace must not claim DMA beyond the 64-bit guest
    # physical address space. Offset and length are decimal trace fields.
    for malformed in (
        write.replace("addr: 0x7f002000", "addr: 0xfffffffffffffff0"),
        write.replace("offset: 0", "offset: 18446744073709551616"),
        write.replace("length: 64", "length: 18446744073709551616"),
    ):
        assert count_dma(tx * 128 + malformed + commit) == (128, 0, 0, 0)
        try:
            report(tx * 128 + malformed + commit)
        except ValueError:
            pass
        else:
            raise AssertionError("accepted out-of-range RX guest DMA address")
    for invalid in (
        "", tx * 127 + write + commit, tx * 128 + commit,
        tx * 128 + write, tx * 128 + write.replace("length: 64", "length: 0") + commit,
        tx * 128 + write.replace("addr: 0x7f002000", "addr: 0x0") + commit,
        tx * 128 + write + commit.replace("written_to_guest", "not_written_to_guest"),
        tx.replace("0x7f001000", "0x0") * 128 + write + commit,
        "spoof " + tx * 128 + write + commit,
        tx * 128 + commit + write,
        tx * 128 + write + commit.replace("(ICR causes 128)", "(queue 2)"),
        "qemu-system-x86_64: terminating on signal 15 from pid 1 (bash)\n" + trace,
    ):
        try:
            report(invalid)
        except ValueError:
            pass
        else:
            raise AssertionError("accepted missing/spoofed QEMU DMA evidence")
    # Do not count events after the emulator has terminated.
    assert count_dma(trace + "qemu-system-x86_64: terminating on signal 15\n" + trace) == (128, 1, 64, 1)
    assert count_dma("unrelated e1000e_tx_descr 0x1 : 1 1\n") == (0, 0, 0, 0)
    for min_tx, min_rx in ((0, 1), (128, 0), (128, -1)):
        try:
            report(trace, min_tx=min_tx, min_rx_commits=min_rx)
        except ValueError:
            pass
        else:
            raise AssertionError("accepted invalid minimum DMA count")
    print("[NET3095-E1000E-DMA-CONTRACT] PASS synthetic trace parser tests only")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("stderr", nargs="?", type=Path)
    parser.add_argument("--min-tx", type=int, default=128)
    parser.add_argument("--min-rx-commits", type=int, default=1)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
    else:
        if args.stderr is None:
            parser.error("real QEMU stderr trace required")
        try:
            print(report(args.stderr.read_text(errors="replace"),
                         args.min_tx, args.min_rx_commits))
        except (OSError, ValueError) as exc:
            parser.exit(1, f"[NET3095-E1000E-DMA-TRACE][FAIL] {exc}\n")
