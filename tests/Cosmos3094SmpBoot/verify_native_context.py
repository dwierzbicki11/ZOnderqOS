#!/usr/bin/env python3
"""Strict Cosmos 3.0.94 Stage10a AP-interrupted-context serial proof.

This is NATIVE context capture only. It does not prove OrionGC STW,
managed Thread.Start or managed stack roots. Fail on fabricated/partial logs.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import re
import verify_boot
import verify_timer_ready
import verify_periodic_timer

ROW = re.compile(
    r"\[SMP3094-CONTEXT\] CPU=(\d+) epoch=(\d+) rsp=(\d+) rip=(\d+) rflags=(\d+)")
STAGE9B = "[SMP3094-STAGE9B] PASS"
STAGE10A = "[SMP3094-STAGE10A] PASS"
BOOT = "[SMP3094-PROBE] BOOT"


class NativeContextError(ValueError):
    pass


def validate(text: str, cpus: int) -> str:
    if cpus < 1 or cpus > 256:
        raise NativeContextError("invalid vCPU count")
    lines = [line.strip() for line in text.replace("\r", "").splitlines()]
    a = [i for i, x in enumerate(lines) if x == STAGE9B]
    b = [i for i, x in enumerate(lines) if x == STAGE10A]
    c = [i for i, x in enumerate(lines) if x == BOOT]
    if len(a) != 1 or len(b) != 1 or len(c) != 1:
        raise NativeContextError("missing/duplicated Stage9b, Stage10a or BOOT")
    if not a[0] < b[0] < c[0]:
        raise NativeContextError("invalid Stage9b / Stage10a / BOOT order")

    seen = {}
    for index, line in enumerate(lines):
        matched = ROW.fullmatch(line)
        if matched:
            cpu, epoch, rsp, rip, flags = map(int, matched.groups())
            if cpu == 0 or cpu >= cpus or cpu in seen:
                raise NativeContextError("duplicate or invalid AP logical CPU ID")
            if not a[0] < index < b[0]:
                raise NativeContextError("context evidence outside Stage10a")
            if epoch != 4 or not rsp or (rsp & 7) or not rip or (flags & 0x202) != 0x202:
                raise NativeContextError("missing/invalid native interrupted AP frame")
            if rsp >= (1 << 64) or rip >= (1 << 64) or flags >= (1 << 64):
                raise NativeContextError("non-64-bit native saved CPU context")
            seen[cpu] = (epoch, rsp, rip, flags)
        elif line.startswith("[SMP3094-CONTEXT]"):
            raise NativeContextError("malformed native context marker: " + line)
    if set(seen) != set(range(1, cpus)):
        raise NativeContextError("incomplete actual AP context mask")
    return f"[SMP3094-STAGE10A-QEMU] PASS cpus={cpus} capturedNativeAPs={len(seen)}"


def check(text: str, cpus: int) -> str:
    verify_boot.check(text, cpus)
    verify_timer_ready.check_timer(text, cpus)
    verify_periodic_timer.check_periodic(text, cpus)
    return validate(text, cpus)


def self_test():
    good = "\n".join([
        STAGE9B,
        "[SMP3094-CONTEXT] CPU=1 epoch=4 rsp=18446744071578947584 rip=18446744071562067968 rflags=514",
        "[SMP3094-CONTEXT] CPU=2 epoch=4 rsp=18446744071578951680 rip=18446744071562067968 rflags=514",
        STAGE10A, BOOT,
    ])
    assert "PASS" in validate(good, 3)
    bad = [
        good.replace("CPU=2", "CPU=1"),
        good.replace("epoch=4", "epoch=3"),
        good.replace("rflags=514", "rflags=2"),
        good.replace("rsp=18446744071578947584", "rsp=1"),
        good.replace("rip=18446744071562067968", "rip=0"),
        good.replace(STAGE10A, ""),
        good.replace(STAGE10A, STAGE10A + "\n" + STAGE10A),
        good.replace("CPU=2 epoch=4 rsp=18446744071578951680 rip=18446744071562067968 rflags=514", ""),
        good.replace("CPU=2 epoch=4", "CPU=3 epoch=4"),
        good.replace("[SMP3094-CONTEXT] CPU=2", "[SMP3094-CONTEXT] FAIL CPU=2"),
        good.replace(STAGE9B, ""),
        good.replace(STAGE9B, STAGE10A).replace(STAGE10A + "\n" + STAGE10A, STAGE10A),
    ]
    for candidate in bad:
        try:
            validate(candidate, 3)
        except NativeContextError:
            continue
        raise AssertionError("accepted invalid native AP snapshot proof")
    print("[SMP3094-STAGE10A-PARSER] PASS positive and negative fake-context cases")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("log", type=Path, nargs="?")
    ap.add_argument("--cpus", type=int, default=4)
    ap.add_argument("--self-test", action="store_true")
    args = ap.parse_args()
    if args.self_test:
        self_test()
    else:
        if args.log is None:
            ap.error("provide QEMU serial log")
        try:
            print(check(args.log.read_text(errors="replace"), args.cpus))
        except (NativeContextError, verify_boot.BootProofError,
                verify_timer_ready.TimerReadinessError,
                verify_periodic_timer.TimerDeliveryError) as error:
            ap.exit(1, f"[SMP3094-STAGE10A-QEMU][FAIL] {error}\n")
