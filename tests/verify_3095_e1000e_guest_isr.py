#!/usr/bin/env python3
"""Fail-closed guest-side E1000E ISR/RX counter delta validator.

This is NOT a QEMU IRQ trace validator. Only counters sampled by the guest
from the real driver handler can establish guest ISR execution. No production
networking is enabled by this tool.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import re
import tempfile
import unicodedata

BINDING = re.compile(
    r"^\[NET3095-N1\] DRIVER_BOUND=PASS node=pci/[0-9a-fA-F]{2}:[0-9a-fA-F]{2}\.[0-7] "
    r"resources=([1-9][0-9]*) irq_sources=([1-9][0-9]*) "
    r"handler_faults=0 isr_executions=unknown$"
)

MARKER = re.compile(
    r"^\[NET3095-ISR\] phase=(before|after) "
    r"interrupts=([0-9]+) rx_frames=([0-9]+) polling=(yes|no)$"
)


def validate(serial: str) -> str:
    # Preserve UART framing: a bare CR can overwrite a visible terminal line
    # and must never conceal a guest fault or fabricate a counter sample.
    if "\r" in serial.replace("\r\n", ""):
        raise ValueError("unexpected bare carriage return in ISR UART evidence")
    # Control bytes and terminal escape sequences can alter the displayed UART
    # evidence without altering the underlying parser input. Fail closed.
    if any((ord(ch) < 32 and ch not in "\r\n\t") or ord(ch) == 127 for ch in serial):
        raise ValueError("unexpected control character in ISR UART evidence")
    if "\x1b" in serial or "\ufffd" in serial:
        raise ValueError("terminal escape or undecodable UART evidence")
    # Reject every Unicode format character, not just the bidi ranges:
    # zero-width joiners and invisible directional marks can also make UART
    # evidence visually disagree with the exact bytes parsed by this gate.
    if any(unicodedata.category(ch) in ("Cf", "Zl", "Zp") or
           (unicodedata.category(ch) == "Cc" and ch not in "\r\n\t")
           for ch in serial):
        raise ValueError("invisible Unicode format, separator or control character in ISR UART evidence")
    # Driver UART evidence is ASCII by contract. Reject confusable Unicode
    # letters, full-width digits and BOM rather than normalizing untrusted logs.
    if any(ord(ch) > 127 for ch in serial):
        raise ValueError("non-ASCII character in ISR UART evidence")
    samples = {}
    phases = []
    boot_count = 0
    bound_count = 0
    for line in serial.replace("\r\n", "\n").split("\n"):
        # Require a single boot in the complete evidence stream. Reject faults
        # even before the baseline or after the final sample.
        if line == "[NET3095-N1] BOOT":
            boot_count += 1
            if boot_count != 1 or bound_count or samples:
                raise ValueError("multiple or late guest boot markers")
        # A malformed or replayed boot/binding line must not be silently
        # ignored. Otherwise counter samples could belong to another boot.
        elif "[NET3095-N1] BOOT" in line:
            raise ValueError("malformed guest boot marker")
        if "[NET3095-N1] DRIVER_BOUND=" in line and not line.startswith(
            "[NET3095-N1] DRIVER_BOUND=PASS "
        ):
            raise ValueError("invalid driver binding marker")
        if re.search(r"\[NET3095-[A-Z0-9-]+\]\[FAIL\]", line):
            raise ValueError("guest network failure in ISR evidence log")
        # A rising counter is not a success if the guest subsequently faults.
        # Inspect the entire serial stream, including lines after the final
        # sample, rather than trusting the two counter markers in isolation.
        if re.search(
            r"(?i)(?:kernel panic|unhandled exception|cpu exception|triple fault"
            r"|page fault|general protection fault|stack overflow|#pf\b|cr2\s*="
            r"|exception\s+(?:0x)?0*[0-9a-f]+\b|system\.[a-z0-9_.]*exception\b)",
            line,
        ):
            raise ValueError("guest fault in ISR evidence log")
        # Termination anywhere in the capture (including after the final
        # counter sample) invalidates the evidence; a stopped guest cannot
        # establish the required post-sample runtime stability.
        if re.search(r"qemu-system-[A-Za-z0-9_.-]+: terminating on signal ", line):
            raise ValueError("QEMU termination in guest ISR evidence")
        if line.startswith("[NET3095-N1] DRIVER_BOUND=PASS "):
            if boot_count != 1 or bound_count or samples:
                raise ValueError("duplicate or misplaced driver binding evidence")
            if BINDING.fullmatch(line) is None:
                raise ValueError("invalid driver binding diagnostic provenance")
            bound_count += 1
        if "[NET3095-ISR]" not in line:
            continue
        match = MARKER.fullmatch(line)
        if match is None:
            raise ValueError("malformed guest ISR sample")
        if boot_count != 1 or bound_count != 1:
            raise ValueError("ISR sample before unique driver binding evidence")
        phase, irq, rx, polling = match.groups()
        # Reject noncanonical/overflowing counters rather than treating
        # arbitrary decimal strings as trustworthy guest driver samples.
        # Driver counters are nonnegative machine-sized values; this gate
        # must not accept leading zeros or values outside unsigned 64-bit.
        for value in (irq, rx):
            if (len(value) > 1 and value[0] == "0") or len(value) > 20:
                raise ValueError("noncanonical guest ISR counter")
            if int(value) > 0xFFFFFFFFFFFFFFFF:
                raise ValueError("guest ISR counter exceeds uint64")
        if phase in samples:
            raise ValueError("duplicate guest ISR sample")
        samples[phase] = (int(irq), int(rx), polling)
        phases.append(phase)
    if boot_count != 1:
        raise ValueError("missing unique guest boot marker")
    if bound_count != 1:
        raise ValueError("missing unique driver binding evidence")
    if set(samples) != {"before", "after"}:
        raise ValueError("missing guest ISR before/after samples")
    if phases != ["before", "after"]:
        raise ValueError("guest ISR samples out of order")
    before, after = samples["before"], samples["after"]
    if before[2] != "no" or after[2] != "no":
        raise ValueError("polling enabled: IRQ RX not proven")
    if after[0] <= before[0]:
        raise ValueError("guest ISR count did not increase")
    if after[1] <= before[1]:
        raise ValueError("guest RX frame count did not increase")
    return (f"[NET3095-ISR] VERIFIED_GUEST_COUNTER_DELTA "
            f"interrupts={after[0] - before[0]} "
            f"rx_frames={after[1] - before[1]} polling=no "
            "(requires independently verified guest instrumentation)")


def read_uart(path: Path) -> str:
    # Path.read_text() silently translates CR and CRLF into LF.
    with path.open("r", encoding="utf-8", errors="strict", newline="") as stream:
        return stream.read()


def self_test() -> None:
    good = ("[NET3095-N1] BOOT\n"
            "[NET3095-N1] DRIVER_BOUND=PASS node=pci/00:02.0 resources=2 irq_sources=1 handler_faults=0 isr_executions=unknown\n"
            "[NET3095-ISR] phase=before interrupts=3 rx_frames=7 polling=no\n"
            "[NET3095-ISR] phase=after interrupts=5 rx_frames=9 polling=no\n")
    assert "VERIFIED_GUEST_COUNTER_DELTA" in validate(good)
    bad = (
        "",
        good.splitlines()[2],
        good.replace("[NET3095-N1] DRIVER_BOUND=PASS node=pci/00:02.0 resources=2 irq_sources=1 handler_faults=0 isr_executions=unknown\n", ""),
        good.replace("DRIVER_BOUND=PASS", "DRIVER_BOUND=FAIL"),
        good.replace("isr_executions=unknown", "isr_executions=1"),
        good.replace("irq_sources=1", "irq_registered=1"),
        good.replace("irq_sources=1", "irq_sources=bogus"),
        good.replace("irq_sources=1", "irq_sources=01"),
        good.replace("irq_sources=1", "irq_sources=0"),
        good.replace("resources=2", "resources=0"),
        good.replace("handler_faults=0", "handler_faults=1"),
        good.replace("resources=2", "resources=-1"),
        good.replace("node=pci/00:02.0", "node=pci/00:02.8"),
        good.replace("isr_executions=unknown", "isr_executions=unknown spoof=1"),
        good.replace("phase=after", "[NET3095-N1] DRIVER_BOUND=PASS node=pci/00:02.0 resources=2 irq_sources=1 handler_faults=0 isr_executions=unknown\n[NET3095-ISR] phase=after"),
        good + good.splitlines()[2] + "\n",
        good.replace("interrupts=5", "interrupts=3"),
        good.replace("rx_frames=9", "rx_frames=7"),
        good.replace("polling=no", "polling=yes", 1),
        good.replace("phase=after", "phase=after extra=1"),
        good.replace("phase=before", "phase=after"),
        "\n".join(reversed(good.splitlines())) + "\n",
        good.replace("interrupts=5", "interrupts=-1"),
        good.replace("interrupts=5", "interrupts=05"),
        good.replace("rx_frames=9", "rx_frames=09"),
        good.replace("interrupts=5", "interrupts=18446744073709551616"),
        good.replace("rx_frames=9", "rx_frames=999999999999999999999999"),
        good.replace("phase=after", "phase=after\rspoof"),
        good + "\x00hidden\n",
        good + "\x1b[2J",
        good + "\x7f",
        good + "\ufffd",
        good + "\u202e",
        good + "\u200b",
        good.replace("phase=after", "phase=\u200dafter"),
        good + "\u061c",
        good + "\u0085",  # C1 next-line control
        good + "\u009b",  # C1 control sequence introducer
        good + "\u2028",  # Unicode line separator
        good + "\u2029",  # Unicode paragraph separator
        good + "\ufeff",  # UTF-8 BOM inserted into UART stream
        good.replace("interrupts=5", "interrupts=５"),  # full-width digit
        good.replace("phase=after", "phase=аfter"),  # Cyrillic confusable
        good.replace("phase=after", "\u2066phase=after\u2069"),
        good.replace("[NET3095-N1] BOOT\n", ""),
        good + "[NET3095-N1] BOOT\n",
        good.replace("[NET3095-N1] BOOT\n", " [NET3095-N1] BOOT\n"),
        good + " [NET3095-N1] BOOT\n",
        good.replace("DRIVER_BOUND=PASS", "DRIVER_BOUND=PENDING"),
        good.replace("[NET3095-N1] DRIVER_BOUND=PASS", " [NET3095-N1] DRIVER_BOUND=PASS"),
        good.splitlines()[0] + "\n[NET3095-N1] BOOT\n" + "\n".join(good.splitlines()[1:]) + "\n",
        good.replace("[NET3095-ISR] phase=before", "[NET3095-N1][FAIL] reset\n[NET3095-ISR] phase=before"),
        good + "[NET3095-N2][FAIL] DHCP timeout\n",
        good + "KERNEL PANIC: IRQ handler failed\n",
        good.replace("phase=after", "Unhandled Exception\n[NET3095-ISR] phase=after"),
        good + "CPU EXCEPTION 14\n",
        good + "Triple fault\n",
        good + "Page fault at CR2=0xC\n",
        good + "CR2=0xC\n",
        good + "#PF at 0xC\n",
        good + "General protection fault\n",
        good + "Stack overflow\n",
        good + "Exception 14 CR2=0xC\n",
        good + "Exception 0x0E\n",
        good + "System.NullReferenceException\n",
        good.replace("[NET3095-ISR] phase=before", "qemu-system-x86_64: terminating on signal 15 from pid 1 (bash)\n[NET3095-ISR] phase=before"),
        good.replace("[NET3095-ISR] phase=after", "[2026-11-08T15:34:10+01:00] qemu-system-x86_64: terminating on signal 15 from pid 1 (bash)\n[NET3095-ISR] phase=after"),
        good.replace("[NET3095-N1] BOOT\n", "[NET3095-N6][FAIL] TCP reset\n[NET3095-N1] BOOT\n"),
        good + "qemu-system-x86_64: terminating on signal 15 from pid 1 (bash)\n",
        good + "runner: qemu-system-x86_64: terminating on signal 15 from pid 1 (bash)\n",
        good + "2026-11-11 10:29:56 qemu-system-x86_64: terminating on signal 15 from pid 1 (bash)\n",
        good + "[2026-11-08T15:34:10+01:00] qemu-system-x86_64: terminating on signal 15 from pid 1 (bash)\n",
    )
    for item in bad:
        try:
            validate(item)
        except ValueError:
            continue
        raise AssertionError(f"accepted invalid ISR evidence: {item!r}")
    # Exercise the actual CLI read path; newline translation previously
    # erased embedded CR from on-disk QEMU UART captures.
    with tempfile.TemporaryDirectory() as tmp:
        path = Path(tmp) / "guest.serial"
        path.write_bytes((good + "Kernel panic\rhidden\n").encode("utf-8"))
        try:
            validate(read_uart(path))
        except ValueError:
            pass
        else:
            raise AssertionError("accepted on-disk UART evidence with bare CR")
        path.write_bytes(good.replace("\n", "\r\n").encode("utf-8"))
        assert "VERIFIED_GUEST_COUNTER_DELTA" in validate(read_uart(path))
        # Invalid UTF-8 in a captured UART stream is a validation failure,
        # not an uncaught traceback or permission to replace corrupt bytes.
        path.write_bytes(good.encode("ascii") + b"\xff\n")
        try:
            read_uart(path)
        except UnicodeError:
            pass
        else:
            raise AssertionError("accepted non-UTF-8 UART capture")
    print("[NET3095-ISR-CONTRACT] PASS synthetic parser and raw UART file tests only")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("serial", nargs="?", type=Path)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
    elif args.serial is None:
        parser.error("real guest serial log required")
    else:
        try:
            print(validate(read_uart(args.serial)))
        except (OSError, UnicodeError, ValueError) as exc:
            parser.exit(1, f"[NET3095-ISR][FAIL] {exc}\n")
