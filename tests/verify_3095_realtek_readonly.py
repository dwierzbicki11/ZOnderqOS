#!/usr/bin/env python3
"""Validate a REAL laptop serial log from the opt-in RTL810xE MMIO probe.

A passing log establishes two matching eleven-byte register snapshots reported by the
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
    r" link=(up|down) speed=(0|10|100) duplex=(full|half|unknown)" +
    r" phy=0x([0-9A-F]{2}) txconfig=0x([0-9A-F]{8}) xid=0x([0-9A-F]{3})" +
    r" variant=(RTL8106e|RTL8105e|unknown) samples=2(?=\s|$)")
KERNEL = re.compile(
    re.escape(PREFIX) + r" SNAPSHOT=PASS mac=" + MAC +
    r" link=(up|down) speed=(0|10|100)" +
    r" phy=0x([0-9A-F]{2}) txconfig=0x([0-9A-F]{8}) xid=0x([0-9A-F]{3})" +
    r" variant=(RTL8106e|RTL8105e|unknown) samples=2(?=\s|$)")
HEARTBEAT = re.compile(re.escape(PREFIX) + r" HEARTBEAT=(\d+)")


def decode_xid(raw_txconfig: str) -> tuple[int, str]:
    """Linux r8169-style conservative XID classification, not a DMA approval."""
    xid = (int(raw_txconfig, 16) >> 20) & 0xFCF
    if (xid & 0x7C8) == 0x448:
        variant = "RTL8106e"
    elif (xid & 0x7CF) == 0x409 or (xid & 0x7C8) == 0x408:
        variant = "RTL8105e"
    else:
        variant = "unknown"
    return xid, variant


def validate(log: str) -> str:
    lines = log.replace("\r", "").splitlines()
    if any(PREFIX + "[FAIL]" in line for line in lines):
        raise ValueError("guest explicitly rejected the MMIO snapshot")
    # A serial log that also records a kernel fault cannot establish a
    # healthy hardware probe, even when earlier markers look successful.
    fatal = re.compile(
        r"kernel panic|fatal: exception|page fault|unhandled exception|triple fault|"
        r"exception\s+14\b|#PF\b|CR2\s*=|general protection fault|double fault|stack overflow",
        re.IGNORECASE)
    if any(fatal.search(line) for line in lines):
        raise ValueError("guest fault recorded in serial log")
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
    if (d.group(5), d.group(6), d.group(7), d.group(8)) != (
        k.group(4), k.group(5), k.group(6), k.group(7)):
        raise ValueError("driver and kernel raw PHY/TxConfig/XID/variant disagree")
    expected_xid, expected_variant = decode_xid(d.group(6))
    if int(d.group(7), 16) != expected_xid or d.group(8) != expected_variant:
        raise ValueError("TxConfig XID or chip family contradicts raw register value")
    phy = int(d.group(5), 16)
    if phy & 0x02:
        expected_speed = {0x04: "10", 0x08: "100"}.get(phy & 0x1C)
        expected_duplex = "full" if phy & 0x01 else "half"
        if expected_speed is None or (d.group(2), d.group(3), d.group(4)) != (
            "up", expected_speed, expected_duplex):
            raise ValueError("decoded link contradicts raw PHYstatus register")
    elif (d.group(2), d.group(3), d.group(4)) != ("down", "0", "unknown"):
        raise ValueError("link-down status contradicts raw PHYstatus register")
    if d.group(2) == "down" and (d.group(3) != "0" or d.group(4) != "unknown"):
        raise ValueError("link-down snapshot invented speed or duplex")
    if d.group(2) == "up" and (d.group(3) == "0" or d.group(4) == "unknown"):
        raise ValueError("link-up snapshot has no speed")
    if len(heartbeats) < 2 or heartbeats[0][0] <= kernel[0][0]:
        raise ValueError("insufficient post-sample guest liveness")
    if heartbeats[0][1] < 1 or any(b != a + 1 for (_, a), (_, b)
                                    in zip(heartbeats, heartbeats[1:])):
        raise ValueError("non-monotonic heartbeat sequence")
    return (f"{PREFIX}-LOG-GATE PASS physical serial report "
            f"mac={d.group(1).upper()} link={d.group(2)} speed={d.group(3)} "
            f"phy=0x{phy:02X} xid=0x{expected_xid:03X} variant={expected_variant} "
            "(no independent MMIO trace, no TX/RX/DMA/IRQ proof)")


def self_test() -> None:
    sample = (
        "[Drivers] pci:0000:02:00.2 RTL810xEReadOnlyProbe: " +
        PREFIX + " MMIO_SNAPSHOT=READ mac=02:11:22:33:44:55 link=up speed=100 "
        "duplex=full phy=0x0B txconfig=0x44800000 xid=0x448 variant=RTL8106e "
        "samples=2 (read-only, no TX/RX/DMA/IRQ)\n" +
        PREFIX + " BOOT read-only physical probe\n" +
        PREFIX + " SNAPSHOT=PASS mac=02:11:22:33:44:55 link=up speed=100 "
        "phy=0x0B txconfig=0x44800000 xid=0x448 variant=RTL8106e "
        "samples=2 (register read only; TX/RX/DMA/IRQ not tested)\n" +
        PREFIX + " HEARTBEAT=1\n" + PREFIX + " HEARTBEAT=2\n"
    )
    assert "PASS" in validate(sample)
    link_down = sample.replace("link=up speed=100 duplex=full",
                               "link=down speed=0 duplex=unknown")
    link_down = link_down.replace("link=up speed=100 phy=0x0B txconfig=",
                                  "link=down speed=0 phy=0x0B txconfig=")
    link_down = link_down.replace("phy=0x0B", "phy=0x08")
    assert "PASS" in validate(link_down)
    rtl8105 = sample.replace("0x44800000 xid=0x448 variant=RTL8106e",
                             "0x40900000 xid=0x409 variant=RTL8105e")
    assert "PASS" in validate(rtl8105)
    unknown = sample.replace("0x44800000 xid=0x448 variant=RTL8106e",
                             "0x12300000 xid=0x103 variant=unknown")
    assert "PASS" in validate(unknown)
    negatives = [
        sample.replace("MMIO_SNAPSHOT=READ", "MMIO_SNAPSHOT=FAKE"),
        sample.replace("0x44800000", "0x40900000", 1),
        sample.replace("phy=0x0B", "phy=0x08"),
        sample.replace("phy=0x0B", "phy=0x08", 1),
        sample.replace("xid=0x448", "xid=0x409"),
        sample.replace("variant=RTL8106e", "variant=RTL8105e"),
        sample.replace("xid=0x448", "xid=0x409", 1),
        sample.replace("variant=RTL8106e", "variant=unknown", 1),
        sample.replace("SNAPSHOT=PASS", "SNAPSHOT=FAKE"),
        sample.replace("samples=2", "samples=1", 1),
        sample.replace("samples=2", "samples=1"),
        sample.replace("link=up speed=100 phy=0x0B txconfig=", "link=down speed=0 phy=0x0B txconfig="),
        sample.replace("HEARTBEAT=2", "HEARTBEAT=4"),
        sample.replace("HEARTBEAT=2", "HEARTBEAT=1"),
        sample.replace("HEARTBEAT=2\n", ""),
        sample.replace("02:11:22:33:44:55", "FF:FF:FF:FF:FF:FF"),
        sample + PREFIX + "[FAIL] unexpected\n",
        sample + "kernel panic: synthetic fault\n",
        sample + "FATAL: Exception 14 (synthetic)\n",
        sample + "Exception 14 CR2=0xC\n",
        sample + "#PF at 0x0\n",
        sample + "General Protection Fault\n",
        sample + "unhandled exception: synthetic\n",
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
