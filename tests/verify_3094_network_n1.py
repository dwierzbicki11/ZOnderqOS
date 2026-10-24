#!/usr/bin/env python3
"""Strict serial-log acceptance: real E1000E NIC/link boot, not a build-only PASS.

Self-tests only validate the parser; passing an actual QEMU serial log is
required before N1 may be reported as runtime-proven.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import re

BOOT = "[NET3094-N1] BOOT"
PASS = "[NET3094-N1] PASS READY_LINK="
END = "[NET3094-N1] DHCP=NOT_ATTEMPTED IP=UNASSIGNED"
DEVICE = re.compile(
    r"^\[NET3094-N1\] cpu=bsp adapter=(\d+) name=e1000e "
    r"mac=([0-9a-fA-F]{2}(?::[0-9a-fA-F]{2}){5}) "
    r"ready=yes link=up$"
)
DISCOVER = re.compile(r"^\[NET3094-N1\] discovered=(\d+)$")
ACCEPT = re.compile(r"^\[NET3094-N1\] PASS READY_LINK=(\d+)$")


def validate(data: str) -> str:
    lines = [s.strip() for s in data.replace("\r", "").splitlines()]
    if any(re.search(r"kernel panic|fatal:\s*exception|page fault", line, re.I)
           or "[NET3094-N1][FAIL]" in line
           or "[SMP3094-MANAGED-STAGE23] PASS" in line for line in lines):
        raise ValueError("kernel panic, N1 failure or unrelated SMP proof found")
    if lines.count(BOOT) != 1 or lines.count(END) != 1:
        raise ValueError("missing or repeated N1 boot/end marker")
    discovered = [DISCOVER.fullmatch(s) for s in lines]
    discovered = [m for m in discovered if m]
    accepted = [ACCEPT.fullmatch(s) for s in lines]
    accepted = [m for m in accepted if m]
    if len(discovered) != 1 or len(accepted) != 1:
        raise ValueError("expected exactly one inventory count and one PASS")
    n = int(discovered[0].group(1))
    ready_links = int(accepted[0].group(1))
    if not 1 <= n <= 8 or ready_links < 1 or ready_links > n:
        raise ValueError("invalid NIC count or ready-link count")
    evidence = [DEVICE.fullmatch(s) for s in lines]
    evidence = [m for m in evidence if m]
    if len(evidence) != 1 or int(evidence[0].group(1)) >= n:
        raise ValueError("missing or duplicated actual e1000e ready/link evidence")
    mac = evidence[0].group(2).lower()
    if mac in ("00:00:00:00:00:00", "ff:ff:ff:ff:ff:ff") or (int(mac[:2], 16) & 1):
        raise ValueError("null/broadcast MAC address is invalid")
    # Reject fabricated PASS before device discovery, and success before
    # reporting that no DHCP/address configuration has taken place.
    idx_boot = lines.index(BOOT)
    idx_count = lines.index(discovered[0].group(0))
    idx_dev = lines.index(evidence[0].group(0))
    idx_pass = lines.index(accepted[0].group(0))
    idx_end = lines.index(END)
    if not (idx_boot < idx_count < idx_dev < idx_pass < idx_end):
        raise ValueError("invalid boot/inventory/link/PASS ordering")
    return f"[NET3094-N1-GATE] PASS e1000e mac={mac} nics={n}"


def self_test() -> None:
    valid = "\n".join([
        BOOT,
        "[NET3094-N1] discovered=1",
        "[NET3094-N1] cpu=bsp adapter=0 name=e1000e mac=52:54:00:12:34:56 ready=yes link=up",
        "[NET3094-N1] PASS READY_LINK=1",
        END,
    ])
    assert validate(valid).startswith("[NET3094-N1-GATE] PASS")
    bad = [
        valid.replace(BOOT, ""),
        valid + "\n" + BOOT,
        valid.replace("discovered=1", "discovered=0"),
        valid.replace("name=e1000e", "name=rtl810xe"),
        valid.replace("mac=52:54:00:12:34:56", "mac=00:00:00:00:00:00"),
        valid.replace("link=up", "link=down"),
        valid.replace("ready=yes", "ready=no"),
        valid.replace("READY_LINK=1", "READY_LINK=0"),
        valid.replace(END, ""),
        valid + "\n[NET3094-N1][FAIL] unexpected",
        valid + "\nKERNEL PANIC",
        valid + "\nKernel Panic: unhandled fault",
        valid + "\nFatal: exception 14 (cr2 0xC)",
        valid + "\npage fault while idle",
        valid.replace("mac=52:54:00:12:34:56", "mac=53:54:00:12:34:56"),
        valid.replace("[NET3094-N1] discovered=1\n", ""),
        valid.replace("[NET3094-N1] PASS READY_LINK=1\n", ""),
        valid.replace("discovered=1\n", "discovered=1\n" + END + "\n"),
        valid + "\n" + "[NET3094-N1] PASS READY_LINK=1",
    ]
    for sample in bad:
        try:
            validate(sample)
        except ValueError:
            pass
        else:
            raise AssertionError("Accepted forged N1 hardware evidence:\n" + sample)
    print("[NET3094-N1-CONTRACT] PASS synthetic positive + negative scenarios")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("log", type=Path, nargs="?")
    ap.add_argument("--self-test", action="store_true")
    args = ap.parse_args()
    if args.self_test:
        self_test()
    else:
        if args.log is None:
            ap.error("real guest serial log required")
        try:
            print(validate(args.log.read_text(errors="replace")))
        except ValueError as exc:
            ap.exit(1, f"[NET3094-N1-GATE][FAIL] {exc}\n")
