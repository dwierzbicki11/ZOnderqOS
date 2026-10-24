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
DENSE = re.compile(r"^\[SMP3095-STAGE2\] cpus=(\d+) dense=0\.\.(\d+) AP_NOT_STARTED$")
PANIC = re.compile(r"kernel panic|fatal:\s*exception|triple.fault|page fault", re.I)

def validate(log: str, cpus: int, require_dense: bool = False) -> str:
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
    if require_dense:
        dense = [x for line in lines if (x := DENSE.fullmatch(line))]
        if len(dense) != 1 or int(dense[0][1]) != cpus or int(dense[0][2]) != cpus - 1:
            raise ValueError("missing, duplicate or malformed dense CPU topology proof")
        if any("[SMP3095-STAGE2] UNAVAILABLE" in line for line in lines):
            raise ValueError("Stage2 runtime rejected CPU topology")
    if any("[SMP3095-STAGE23] PASS" in s for s in lines):
        raise ValueError("unrelated fabricated Stage 23 evidence")
    stage = 2 if require_dense else 1
    return f"[SMP3095-STAGE{stage}-GATE] PASS discovery cpus={cpus}; AP_NOT_STARTED"

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
    dense_good = good + "\n[SMP3095-STAGE2] cpus=8 dense=0..7 AP_NOT_STARTED"
    assert "STAGE2-GATE" in validate(dense_good, 8, require_dense=True)
    for dense_bad in (
        dense_good.replace("dense=0..7", "dense=0..6"),
        dense_good.replace("STAGE2] cpus=8", "STAGE2] cpus=4"),
        dense_good + "\n[SMP3095-STAGE2] cpus=8 dense=0..7 AP_NOT_STARTED",
        dense_good + "\n[SMP3095-STAGE2] UNAVAILABLE AP_NOT_STARTED",
        good,
    ):
        try:
            validate(dense_bad, 8, require_dense=True)
        except ValueError:
            continue
        raise AssertionError(f"accepted bad Stage2 CPU mapping: {dense_bad!r}")
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
    parser.add_argument("--require-dense", action="store_true", help="require native Stage2 0..N-1 mapping proof")
    args = parser.parse_args()
    if args.self_test:
        selftest()
    elif args.log:
        try:
            print(validate(args.log.read_text(errors="replace"), args.cpus, args.require_dense))
        except ValueError as e:
            parser.exit(1, f"[SMP3095-STAGE1-GATE][FAIL] {e}\n")
    else:
        parser.error("real serial log required unless --self-test")
