#!/usr/bin/env python3
"""Cosmos v3.0.94 Stage 9a: strict native AP timer readiness serial proof.

Stage 9a only samples the MASKED AP LAPIC timer vector via native F2.
It does NOT prove periodic AP interrupts or managed SMP/OrionGC scheduling.
"""
from __future__ import annotations
import argparse
import re
from pathlib import Path

import verify_boot

BASE = re.compile(r"\[SMP3094-TIMER-BASE\] ticksPerMs=(\d+) intervalTicks=(\d+)")
AP = re.compile(r"\[SMP3094-TIMER-READY\] CPU=(\d+) masked=1 lvt=(\d+)")
PASS = "[SMP3094-STAGE9A] PASS"
STAGE8 = "[SMP3094-STAGE8] PASS"
BOOT = "[SMP3094-PROBE] BOOT"


class TimerReadinessError(ValueError):
    pass


def check_timer(text: str, cpus: int) -> str:
    if not 1 <= cpus <= 256:
        raise TimerReadinessError("logical CPU count outside 1..256")
    lines = [line.strip() for line in text.replace("\r", "").splitlines()]
    stage8 = [i for i, x in enumerate(lines) if x == STAGE8]
    stage9a = [i for i, x in enumerate(lines) if x == PASS]
    boot = [i for i, x in enumerate(lines) if x == BOOT]
    if len(stage8) != 1 or len(stage9a) != 1 or len(boot) != 1:
        raise TimerReadinessError("missing/duplicated Stage8, Stage9a or BOOT marker")
    if not stage8[0] < stage9a[0] < boot[0]:
        raise TimerReadinessError("wrong Stage8 / Stage9a / BOOT ordering")

    samples = {}
    base = []
    for i, line in enumerate(lines):
        b = BASE.fullmatch(line)
        a = AP.fullmatch(line)
        if b:
            base.append((i, int(b[1]), int(b[2])))
        elif a:
            cpu, lvt = int(a[1]), int(a[2])
            if cpu == 0 or cpu >= cpus or cpu in samples:
                raise TimerReadinessError("duplicate or out-of-range AP CPU ID")
            if (lvt & 0x10000) == 0 or (lvt & 0x20000) != 0:
                raise TimerReadinessError("unmasked/periodic AP timer: " + line)
            if not stage8[0] < i < stage9a[0]:
                raise TimerReadinessError("AP sample outside Stage 9a proof window")
            samples[cpu] = lvt
        elif line.startswith(("[SMP3094-TIMER-", "[SMP3094-STAGE9")):
            if line != PASS:
                raise TimerReadinessError("unknown or failed Stage 9a evidence: " + line)

    if len(base) != 1:
        raise TimerReadinessError("required exactly one BSP timer calibration")
    i, ticks_per_ms, interval = base[0]
    if (not stage8[0] < i < stage9a[0] or
            not 0 < ticks_per_ms <= 0xFFFFFFFF // 10 or
            interval != ticks_per_ms * 10):
        raise TimerReadinessError("invalid/overflowed BSP timer calibration")
    if set(samples) != set(range(1, cpus)):
        raise TimerReadinessError(f"AP mask incomplete: {sorted(samples)}")
    return f"[SMP3094-STAGE9A-QEMU] PASS cpus={cpus} apTimerMasked={len(samples)} calibratedTicksPerMs={ticks_per_ms}"


def check(text: str, cpus: int) -> str:
    verify_boot.check(text, cpus)  # strict Stage 4-8 proof first
    return check_timer(text, cpus)


def self_test() -> None:
    good = "\n".join([
        STAGE8,
        "[SMP3094-TIMER-BASE] ticksPerMs=10000 intervalTicks=100000",
        "[SMP3094-TIMER-READY] CPU=1 masked=1 lvt=65536",
        "[SMP3094-TIMER-READY] CPU=2 masked=1 lvt=65536",
        PASS, BOOT,
    ])
    assert "PASS" in check_timer(good, 3)
    bad = [
        good.replace("CPU=2", "CPU=1"),
        good.replace("lvt=65536", "lvt=0"),
        good.replace("intervalTicks=100000", "intervalTicks=99999"),
        good.replace(PASS, PASS + "\n" + PASS),
        good.replace("[SMP3094-TIMER-READY] CPU=2 masked=1 lvt=65536", ""),
        good.replace("CPU=1 masked=1 lvt=65536", "CPU=1 masked=1 lvt=196608"),
        good.replace("[SMP3094-TIMER-BASE]", "[SMP3094-TIMER-UNKNOWN]"),
        good.replace("CPU=2 masked=1", "CPU=3 masked=1"),
    ]
    for data in bad:
        try:
            check_timer(data, 3)
        except TimerReadinessError:
            continue
        raise AssertionError("invalid Stage9a evidence accepted: " + data)
    print("[SMP3094-STAGE9A-PARSER] PASS all positive/negative tests")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("log", type=Path, nargs="?")
    parser.add_argument("--cpus", type=int, default=4)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
    else:
        if args.log is None:
            parser.error("a serial log file is required")
        try:
            print(check(args.log.read_text(errors="replace"), args.cpus))
        except (TimerReadinessError, verify_boot.BootProofError) as e:
            parser.exit(1, f"[SMP3094-STAGE9A-QEMU][FAIL] {e}\n")
