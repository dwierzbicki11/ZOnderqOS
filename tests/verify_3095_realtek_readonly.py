#!/usr/bin/env python3
"""Validate a REAL laptop serial log from the opt-in RTL810xE MMIO probe.

A passing log establishes a seven-byte register snapshot reported by the
DriverKit probe, not TX/RX, DMA, IRQ, DHCP or network functionality.
--self-test only checks parser logic using synthetic strings.
"""
from __future__ import annotations
import argparse
from pathlib import Path
import re

PREFIX = "[NET3095-RTL810XE]"
MAC = r"([0-9A-Fa-f]{2}(?::[0-9A-Fa-f]{2}){5})"
DRIVER = re.compile(
    re.escape(PREFIX) + r" MMIO_SNAPSHOT=READ mac=" + MAC +
    r" link=(up|down) speed=(0|10|100) duplex=(full|half)")
KERNEL = re.compile(
    re.escape(PREFIX) + r" SNAPSHOT=PASS mac=" + MAC +
    r" link=(up|down) speed=(0|10|100)")
HEARTBEAT = re.compile(re.escape(PREFIX) + r" HEARTBEAT=(\d+)")


def validate(log: str) -> str:
    lines = log.replace("\r", "").splitlines()
    if any(PREFIX + "[FAIL]" in line for line in lines):
        raise ValueError("guest explicitly rejected the MMIO snapshot")
    driver = [(i, m) for i, line in enumerate(lines)
              if (m := DRIVER.search(line))]
    kernel = [(i, m) for i, line in enumerate(lines)
              if (m := KERNEL.search(line))]
    boots = [i for i, line in enumerate(lines)
             if line.strip() == PREFIX + " BOOT read-only physical probe"]
    heartbeats = [(i, int(m.group(1))) for i, line in enumerate(lines)
                  if (m := HEARTBEAT.search(line))]
    if len(driver) != 1 or len(kernel) != 1 or len(boots) != 1:
        raise ValueError("expected exactly one driver sample, kernel sample and boot")
    if not (driver[0][0] < boots[0] < kernel[0][0]):
        raise ValueError("driver/boot/kernel markers out of order")
    d, k = driver[0][1], kernel[0][1]
    if (d.group(1).upper(), d.group(2), d.group(3)) != (
        k.group(1).upper(), k.group(2), k.group(3)):
        raise ValueError("driver and kernel snapshots disagree")
    mac_bytes = bytes.fromhex(d.group(1).replace(":", ""))
    if mac_bytes[0] & 1 or not any(mac_bytes):
        raise ValueError("invalid unicast station address")
    if d.group(2) == "down" and (d.group(3) != "0" or d.group(4) != "half"):
        raise ValueError("link-down snapshot invented speed or duplex")
    if d.group(2) == "up" and d.group(3) == "0":
        raise ValueError("link-up snapshot has no speed")
    if len(heartbeats) < 2 or heartbeats[0][0] <= kernel[0][0]:
        raise ValueError("insufficient post-sample guest liveness")
    if heartbeats[0][1] < 1 or any(b != a + 1 for (_, a), (_, b)
                                    in zip(heartbeats, heartbeats[1:])):
        raise ValueError("non-monotonic heartbeat sequence")
    return (f"{PREFIX}-LOG-GATE PASS physical serial report "
            f"mac={d.group(1).upper()} link={d.group(2)} speed={d.group(3)} "
            "(no independent MMIO trace, no TX/RX/DMA/IRQ proof)")


def self_test() -> None:
    sample = (
        "[Drivers] pci:0000:02:00.2 RTL810xEReadOnlyProbe: " +
        PREFIX + " MMIO_SNAPSHOT=READ mac=02:11:22:33:44:55 link=up speed=100 "
        "duplex=full (read-only, no TX/RX/DMA/IRQ)\n" +
        PREFIX + " BOOT read-only physical probe\n" +
        PREFIX + " SNAPSHOT=PASS mac=02:11:22:33:44:55 link=up speed=100 "
        "(register read only; TX/RX/DMA/IRQ not tested)\n" +
        PREFIX + " HEARTBEAT=1\n" + PREFIX + " HEARTBEAT=2\n"
    )
    assert "PASS" in validate(sample)
    negatives = [
        sample.replace("MMIO_SNAPSHOT=READ", "MMIO_SNAPSHOT=FAKE"),
        sample.replace("SNAPSHOT=PASS", "SNAPSHOT=FAKE"),
        sample.replace("link=up speed=100 (register", "link=down speed=0 (register"),
        sample.replace("HEARTBEAT=2", "HEARTBEAT=4"),
        sample.replace("HEARTBEAT=2", "HEARTBEAT=1"),
        sample.replace("HEARTBEAT=2\n", ""),
        sample.replace("02:11:22:33:44:55", "FF:FF:FF:FF:FF:FF"),
        sample + PREFIX + "[FAIL] unexpected\n",
        sample + sample,
    ]
    for bad in negatives:
        try:
            validate(bad)
        except ValueError:
            continue
        raise AssertionError("accepted invalid synthetic RTL810xE serial proof")
    print(f"{PREFIX}-LOG-CONTRACT PASS synthetic parser negative cases only")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("serial_log", nargs="?", type=Path)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
    else:
        if args.serial_log is None:
            parser.error("real laptop serial log required")
        try:
            print(validate(args.serial_log.read_text(errors="replace")))
        except (OSError, ValueError) as exc:
            parser.exit(1, f"{PREFIX}-LOG-GATE FAIL {exc}\n")
