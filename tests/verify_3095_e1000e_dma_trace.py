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
    r"Received packet written to guest \(ICR causes (\d+)\)$"
)
TERMINATED = re.compile(r"^qemu-system-[^:]+: terminating on signal ")


def count_dma(stderr: str) -> tuple[int, int, int, int]:
    """Return TX descriptor reads, RX buffer writes, RX bytes, RX commits."""
    tx = writes = rx_bytes = commits = 0
    for line in stderr.replace("\r", "").splitlines():
        line = line.strip()
        if TERMINATED.match(line):
            break
        match = TX.fullmatch(line)
        if match:
            # A null descriptor buffer address is not packet DMA evidence.
            if int(match.group(1), 16) != 0:
                tx += 1
            continue
        match = RX_BUFFER.fullmatch(line)
        if match:
            index, address, offset, length = (int(part, 16 if n == 1 else 10)
                                               for n, part in enumerate(match.groups()))
            # QEMU packet-split descriptor slots are 0..3; ignore zero-length
            # writes and null guest physical addresses.
            if index <= 3 and address != 0 and length > 0:
                writes += 1
                rx_bytes += length
            continue
        match = RX_COMMIT.fullmatch(line)
        if match:
            # The ICR cause can be zero when interrupts are masked or
            # coalesced; the event itself reports a completed guest write.
            # IRQ notification is validated independently.
            commits += 1
    return tx, writes, rx_bytes, commits


def report(stderr: str, min_tx: int = 128) -> str:
    if min_tx < 1:
        raise ValueError("minimum TX descriptor count must be positive")
    tx, writes, rx_bytes, commits = count_dma(stderr)
    if tx < min_tx:
        raise ValueError(f"QEMU TX descriptor reads {tx} < {min_tx}")
    if not writes or not rx_bytes or not commits:
        raise ValueError("missing QEMU RX guest-buffer DMA write or RX commit")
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
    assert count_dma(tx * 128 + write + commit.replace("causes 128", "causes 0")) == (128, 1, 64, 1)
    for invalid in (
        "", tx * 127 + write + commit, tx * 128 + commit,
        tx * 128 + write, tx * 128 + write.replace("length: 64", "length: 0") + commit,
        tx * 128 + write.replace("addr: 0x7f002000", "addr: 0x0") + commit,
        tx * 128 + write + commit.replace("written_to_guest", "not_written_to_guest"),
        tx.replace("0x7f001000", "0x0") * 128 + write + commit,
        "spoof " + tx * 128 + write + commit,
        tx * 128 + write + commit.replace("written_to_guest", "not_written_to_guest"),
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
    try:
        report(trace, 0)
    except ValueError:
        pass
    else:
        raise AssertionError("accepted zero minimum TX count")
    print("[NET3095-E1000E-DMA-CONTRACT] PASS synthetic trace parser tests only")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("stderr", nargs="?", type=Path)
    parser.add_argument("--min-tx", type=int, default=128)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
    else:
        if args.stderr is None:
            parser.error("real QEMU stderr trace required")
        try:
            print(report(args.stderr.read_text(errors="replace"), args.min_tx))
        except (OSError, ValueError) as exc:
            parser.exit(1, f"[NET3095-E1000E-DMA-TRACE][FAIL] {exc}\n")
