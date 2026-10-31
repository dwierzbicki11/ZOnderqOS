#!/usr/bin/env python3
"""Fail-closed serial gate for isolated RTL810xE negative QEMU boots.

The unrelated QEMU RTL8139 can trigger a scheduler tick *inside* the
serial diagnostic marker. Only that observed, exact tick splice is
normalized; a missing or malformed decline is never treated as PASS.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import re

PREFIX = "[NET3095-RTL810XE]"
BOOT = PREFIX + " BOOT read-only physical probe"
DECLINE = (PREFIX + "[FAIL] no unique valid MMIO snapshot; "
           "offers=0 samples=0 reason=no matching PCI probe offer")
TICK_SPLICE = re.compile(r"(?<=\[NET)\[SCHED\] Tick [0-9]+ enabled=1\n"
                         r"(?=3095-RTL810XE\]\[FAIL\])")
HEARTBEAT = re.compile(r"^\[NET3095-RTL810XE\] HEARTBEAT=([0-9]+)$", re.MULTILINE)
PCI_FUNCTIONS = re.compile(r"^\[Drivers\] platform:pci@cf8 PciHostDriver: ([0-9]+) functions on 1 buses$", re.MULTILINE)
FATAL = re.compile(r"kernel panic|fatal: exception|page fault|unhandled exception|"
                   r"exception\s+14\b|#PF\b|CR2\s*=|general protection fault|"
                   r"double fault|triple fault|stack overflow", re.IGNORECASE)


def verify(serial: str, mode: str) -> str:
    if mode not in ("none", "rtl8139"):
        raise ValueError("unsupported NIC mode")
    raw = serial.replace("\r", "")
    # Do not remove arbitrary trace lines or reconstruct arbitrary fragments.
    # A scheduler tick between '[NET' and '3095-RTL...' was observed in
    # the real RTL8139 QEMU artifact, run 41215345464.
    cleaned = TICK_SPLICE.sub("", raw)
    lines = cleaned.splitlines()
    if lines.count(BOOT) != 1 or lines.count(DECLINE) != 1:
        raise ValueError("missing/duplicate RTL810xE boot or exact decline marker")
    if any((PREFIX + "[FAIL]" in line and line != DECLINE) for line in lines):
        raise ValueError("unexpected RTL810xE failure marker")
    if FATAL.search(raw) or "MMIO_SNAPSHOT=READ" in raw or "SNAPSHOT=PASS" in raw:
        raise ValueError("guest fault or false physical MMIO proof")
    if "E1000EDriver: mac" in raw or "RTL810xEReadOnlyProbe -> bound" in raw:
        raise ValueError("unexpected network driver binding")
    functions = [int(x) for x in PCI_FUNCTIONS.findall(cleaned)]
    expected = 5 if mode == "none" else 6
    if functions != [expected]:
        raise ValueError("unexpected PCI function count for QEMU NIC mode")
    # The q35 RTL8139 is an unrelated function and must remain unbound.
    if mode == "rtl8139" and lines.count("[Drivers] pci:0000:00:02.0 no driver") != 1:
        raise ValueError("unrelated RTL8139 PCI function not observed unbound")
    heartbeats = [(i, int(m.group(1))) for i, line in enumerate(lines)
                  if (m := HEARTBEAT.fullmatch(line))]
    if len(heartbeats) < 2 or heartbeats[0][1] != 1 or heartbeats[1][1] != 2:
        raise ValueError("missing first two guest heartbeats")
    if any(b[1] != a[1] + 1 for a, b in zip(heartbeats, heartbeats[1:])):
        raise ValueError("duplicate or skipped guest heartbeat")
    if not (lines.index(BOOT) < lines.index(DECLINE) < heartbeats[0][0]):
        raise ValueError("boot, decline and liveness out of order")
    return (f"[NET3095-RTL-NEG] PASS mode={mode} PCI functions={expected}, "
            "no RTL810xE sample, two guest heartbeats; QEMU negative only")


def self_test() -> None:
    base = ("[Drivers] platform:pci@cf8 PciHostDriver: 5 functions on 1 buses\n" +
            BOOT + "\n" + DECLINE + "\n" + PREFIX + " HEARTBEAT=1\n" +
            PREFIX + " HEARTBEAT=2\n")
    assert "PASS mode=none" in verify(base, "none")
    rtl = base.replace("5 functions", "6 functions").replace(
        BOOT, "[Drivers] pci:0000:00:02.0 no driver\n" + BOOT)
    assert "PASS mode=rtl8139" in verify(rtl, "rtl8139")
    # Exact splice from real QEMU log: tick preempts a single diagnostic write.
    spliced = rtl.replace(DECLINE, "[NET[SCHED] Tick 6 enabled=1\n" +
                          DECLINE[len("[NET"):])
    assert "PASS mode=rtl8139" in verify(spliced, "rtl8139")
    bad = [
        rtl.replace("offers=0", "offers=1"),
        rtl.replace("samples=0", "samples=1"),
        rtl.replace("HEARTBEAT=2", "HEARTBEAT=1"),
        rtl.replace("HEARTBEAT=2", "HEARTBEAT=3"),
        rtl.replace("HEARTBEAT=2\n", ""),
        rtl.replace("no driver", "offer RTL810xEReadOnlyProbe -> bound"),
        rtl.replace("6 functions", "5 functions"),
        rtl.replace(DECLINE, DECLINE + "\n" + DECLINE),
        rtl.replace(BOOT, BOOT + "\n" + BOOT),
        rtl.replace(DECLINE, "[NET[UNKNOWN] noise\n" + DECLINE[len("[NET"):]),
        rtl + "[NET3095-RTL810XE] SNAPSHOT=PASS mac=02:11:22:33:44:55\n",
        rtl + "Exception 14 CR2=0xC\n",
        rtl + "[NET3095-RTL810XE][FAIL] unexpected\n",
        rtl.replace(BOOT + "\n" + DECLINE, DECLINE + "\n" + BOOT),
    ]
    for sample in bad:
        try:
            verify(sample, "rtl8139")
        except ValueError:
            continue
        raise AssertionError("accepted invalid RTL negative QEMU serial evidence")
    print("[NET3095-RTL-NEG-CONTRACT] PASS synthetic parser cases only")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("serial", nargs="?", type=Path)
    parser.add_argument("mode", nargs="?", choices=("none", "rtl8139"))
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
    else:
        if args.serial is None or args.mode is None:
            parser.error("real QEMU serial log and mode required")
        try:
            print(verify(args.serial.read_text(errors="replace"), args.mode))
        except (OSError, ValueError) as exc:
            parser.exit(1, f"[NET3095-RTL-NEG][FAIL] {exc}\n")
