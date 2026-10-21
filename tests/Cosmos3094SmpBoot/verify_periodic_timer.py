#!/usr/bin/env python3
"""Strict Stage 9b *real periodic AP LAPIC IRQ* QEMU serial verifier.

This proof is native only. It must never count as managed Thread/OrionGC SMP.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import re

import verify_boot
import verify_timer_ready

RECORD = re.compile(r"\[SMP3094-TIMER-B\] CPU=(\d+) ticks=(\d+) stopped=1 lvt=(\d+)")
PASS = "[SMP3094-STAGE9B] PASS"
STAGE9A = "[SMP3094-STAGE9A] PASS"
BOOT = "[SMP3094-PROBE] BOOT"
STOPPED_LVT = 0x100F3  # F3 masked, periodic bit cleared.


class TimerDeliveryError(ValueError):
    pass


def check_periodic(text: str, cpus: int) -> str:
    if not 1 <= cpus <= 256:
        raise TimerDeliveryError("invalid logical CPU count")
    lines = [line.strip() for line in text.replace("\r", "").splitlines()]
    a = [i for i, line in enumerate(lines) if line == STAGE9A]
    b = [i for i, line in enumerate(lines) if line == PASS]
    boot = [i for i, line in enumerate(lines) if line == BOOT]
    if len(a) != 1 or len(b) != 1 or len(boot) != 1:
        raise TimerDeliveryError("Stage9a, Stage9b and BOOT must occur exactly once")
    if not a[0] < b[0] < boot[0]:
        raise TimerDeliveryError("incorrect stage order")

    cpu_ticks = {}
    cpu_stop_lvt = {}
    for i, line in enumerate(lines):
        match = RECORD.fullmatch(line)
        if match:
            cpu, ticks, lvt = (int(x) for x in match.groups())
            if cpu <= 0 or cpu >= cpus or cpu in cpu_ticks:
                raise TimerDeliveryError("duplicate or wrong logical AP ID")
            if not a[0] < i < b[0]:
                raise TimerDeliveryError("timer proof record outside Stage9b")
            if ticks < 3 or ticks > 10_000:
                raise TimerDeliveryError("AP did not execute bounded real F3 IRQs")
            if lvt != STOPPED_LVT:
                raise TimerDeliveryError("AP LAPIC timer still armed/periodic")
            cpu_ticks[cpu] = ticks
            cpu_stop_lvt[cpu] = lvt
        elif line.startswith("[SMP3094-TIMER-B]"):
            raise TimerDeliveryError("unexpected timer proof text: " + line)
    if set(cpu_ticks) != set(range(1, cpus)):
        raise TimerDeliveryError(f"missing AP timer evidence: {sorted(cpu_ticks)}")
    return f"[SMP3094-STAGE9B-QEMU] PASS cpus={cpus} nativeApTimerIrqs={len(cpu_ticks)} timerStopped=1"


def check(text: str, cpus: int) -> str:
    verify_boot.check(text, cpus)
    verify_timer_ready.check_timer(text, cpus)
    return check_periodic(text, cpus)


def self_test() -> None:
    good = "\n".join([
        STAGE9A,
        "[SMP3094-TIMER-B] CPU=1 ticks=3 stopped=1 lvt=65779",
        "[SMP3094-TIMER-B] CPU=2 ticks=5 stopped=1 lvt=65779",
        PASS, BOOT,
    ])
    assert "PASS" in check_periodic(good, 3)
    bad = [
        good.replace("CPU=2", "CPU=1"),
        good.replace("ticks=3", "ticks=2"),
        good.replace("lvt=65779", "lvt=243"),
        good.replace("stopped=1", "stopped=0"),
        good.replace("CPU=2 ticks=5 stopped=1 lvt=65779", ""),
        good.replace(PASS, PASS + "\n" + PASS),
        good.replace(PASS, ""),
        good.replace(STAGE9A, ""),
        good.replace("ticks=5", "ticks=-5"),
        good.replace("CPU=2", "CPU=3"),
        good.replace("ticks=3", "ticks=10001"),
        good.replace("[SMP3094-TIMER-B] CPU=2", "[SMP3094-TIMER-B] FAIL CPU=2"),
    ]
    for candidate in bad:
        try:
            check_periodic(candidate, 3)
        except TimerDeliveryError:
            continue
        raise AssertionError("invalid Stage9b evidence accepted: " + candidate)
    print("[SMP3094-STAGE9B-PARSER] PASS positive and forged/invalid cases")


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
            ap.error("serial log is required")
        try:
            print(check(args.log.read_text(errors="replace"), args.cpus))
        except (TimerDeliveryError,
                verify_boot.BootProofError,
                verify_timer_ready.TimerReadinessError) as error:
            ap.exit(1, f"[SMP3094-STAGE9B-QEMU][FAIL] {error}\n")
