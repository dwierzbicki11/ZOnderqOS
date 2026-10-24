#!/usr/bin/env python3
"""Validate *native discovery* from real Cosmos 3.0.95 QEMU serial output.

Passing this does not imply that any AP has started or run managed code.
"""
from __future__ import annotations
import argparse
import re
from pathlib import Path

DISCOVERY = re.compile(
    r"^\[SMP3095-STAGE1\] Limine MP cpus=(\d+) bsp_apic=(\d+) AP_NOT_STARTED$"
)
ACPI = re.compile(r"^\[ACPI\] Found (\d+) CPU\(s\)$")
PANIC = re.compile(r"kernel panic|fatal:\s*exception|triple.fault|page fault", re.I)

def validate(log: str, cpus: int) -> str:
    if not 1 <= cpus <= 256:
        raise ValueError("invalid requested CPU count")
    lines = [line.strip() for line in log.replace("\r", "").splitlines()]
    if any(PANIC.search(line) for line in lines):
        raise ValueError("kernel failure marker")
    discoveries = [x for s in lines if (x := DISCOVERY.fullmatch(s))]
    acpis = [x for s in lines if (x := ACPI.fullmatch(s))]
    if len(discoveries) != 1 or len(acpis) != 1:
        raise ValueError("missing or duplicate Limine/ACPI evidence")
    if int(discoveries[0][1]) != cpus or int(acpis[0][1]) != cpus:
        raise ValueError("mismatched CPU enumeration")
    if int(discoveries[0][2]) >= 256:
        raise ValueError("invalid BSP APIC ID")
    if not any("[Kernel] Calling Run()..." in s for s in lines):
        raise ValueError("guest did not enter kernel main loop")
    if any("[SMP3095-STAGE23] PASS" in s for s in lines):
        raise ValueError("unrelated fabricated Stage 23 evidence")
    return f"[SMP3095-STAGE1-GATE] PASS discovery cpus={cpus}; AP_NOT_STARTED"

def selftest() -> None:
    good = "\n".join((
        "[ACPI] Found 8 CPU(s)",
        "[SMP3095-STAGE1] Limine MP cpus=8 bsp_apic=0 AP_NOT_STARTED",
        "[Kernel] Calling Run()...",
    ))
    assert "PASS discovery" in validate(good, 8)
    bad = [
        good.replace("cpus=8", "cpus=4"),
        good.replace("Found 8", "Found 4"),
        good.replace("bsp_apic=0", "bsp_apic=256"),
        good.replace(" AP_NOT_STARTED", " AP_ONLINE"),
        good.replace("[Kernel] Calling Run()...", ""),
        good.replace("Limine MP", "UNAVAILABLE"),
        good + "\nFatal: exception 14",
        good + "\nKernel Panic",
        good + "\n" + good.splitlines()[1],
        good + "\n[SMP3095-STAGE23] PASS cpus=8 rounds=1000",
    ]
    for case in bad:
        try:
            validate(case, 8)
        except ValueError:
            continue
        raise AssertionError(f"forged native topology accepted:\n{case}")
    print("[SMP3095-STAGE1-CONTRACT] PASS synthetic negative checks; NOT an AP execution proof")

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("log", type=Path, nargs="?")
    parser.add_argument("--cpus", type=int, default=8)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        selftest()
    elif args.log:
        try:
            print(validate(args.log.read_text(errors="replace"), args.cpus))
        except ValueError as e:
            parser.exit(1, f"[SMP3095-STAGE1-GATE][FAIL] {e}\n")
    else:
        parser.error("real serial log required unless --self-test")
