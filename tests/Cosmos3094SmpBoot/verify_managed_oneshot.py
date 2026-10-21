#!/usr/bin/env python3
"""Strict real Cosmos 3.0.94 native->C# AP checksum serial evidence checker.

Stage12 runs a bounded, allocation-free C# function on each AP in the
ISOLATED BOOT PROBE. It does NOT prove Thread.Start or OrionGC-safe SMP.
"""
from __future__ import annotations
import argparse
from pathlib import Path
import re
import verify_native_context
import verify_boot
import verify_timer_ready
import verify_periodic_timer

ROW = re.compile(r"\[SMP3094-MANAGED-ONESHOT\] CPU=(\d+) checksum=(\d+) returned-native-park=1")
PREVIOUS = "[SMP3094-STAGE10A] PASS"
PASS = "[SMP3094-STAGE12] PASS"
BOOT = "[SMP3094-PROBE] BOOT"
MASK = (1 << 64) - 1


class ManagedOneShotError(ValueError):
    pass


def checksum(cpu: int) -> int:
    value = (0xD6E8FEB86659FD93 ^ cpu) & MASK
    for round_number in range(1024):
        value ^= ((cpu + round_number) * 0x9E3779B97F4A7C15) & MASK
        value &= MASK
        value = ((value << 13) | (value >> 51)) & MASK
    return value


def verify_one_shot(text: str, cpus: int) -> str:
    if not 1 <= cpus <= 256:
        raise ManagedOneShotError("unsupported logical CPU count")
    lines = [x.strip() for x in text.replace("\r", "").splitlines()]
    marks = [[i for i, line in enumerate(lines) if line == m]
             for m in (PREVIOUS, PASS, BOOT)]
    if any(len(ids) != 1 for ids in marks):
        raise ManagedOneShotError("missing/duplicated Stage10a, Stage12 or BOOT")
    (old,), (current,), (boot,) = marks
    if not old < current < boot:
        raise ManagedOneShotError("out-of-order managed AP proof")

    seen = set()
    for pos, line in enumerate(lines):
        match = ROW.fullmatch(line)
        if match:
            cpu, result = map(int, match.groups())
            if not old < pos < current or cpu == 0 or cpu >= cpus or cpu in seen:
                raise ManagedOneShotError("invalid/duplicate managed AP ID or stage order")
            if result != checksum(cpu):
                raise ManagedOneShotError(f"invalid C# managed AP checksum on CPU {cpu}")
            seen.add(cpu)
        elif line.startswith("[SMP3094-MANAGED-ONESHOT]"):
            raise ManagedOneShotError("invalid AP managed record " + line)

    if seen != set(range(1, cpus)):
        raise ManagedOneShotError("missing managed AP execution: " + str(set(range(1, cpus)) - seen))
    return f"[SMP3094-STAGE12-QEMU] PASS cpus={cpus} boundedManagedAPs={len(seen)} returnedToNativePark=1"


def check(text: str, cpus: int) -> str:
    verify_boot.check(text, cpus)
    verify_timer_ready.check_timer(text, cpus)
    verify_periodic_timer.check_periodic(text, cpus)
    verify_native_context.validate(text, cpus)
    return verify_one_shot(text, cpus)


def self_test():
    good = "\n".join([
        PREVIOUS,
        f"[SMP3094-MANAGED-ONESHOT] CPU=1 checksum={checksum(1)} returned-native-park=1",
        f"[SMP3094-MANAGED-ONESHOT] CPU=2 checksum={checksum(2)} returned-native-park=1",
        PASS, BOOT])
    assert "PASS" in verify_one_shot(good, 3)
    assert "PASS" in verify_one_shot("\n".join([PREVIOUS, PASS, BOOT]), 1)
    bad = [
        good.replace("CPU=2", "CPU=1"),
        good.replace(f"checksum={checksum(1)}", "checksum=0"),
        good.replace("returned-native-park=1", "returned-native-park=0"),
        good.replace(PASS, ""),
        good.replace(PASS, PASS + "\n" + PASS),
        good.replace("CPU=2 checksum=", "CPU=3 checksum="),
        good.replace(PREVIOUS, PASS),
        good.replace("checksum=", "checksum=-"),
        good.replace(f"[SMP3094-MANAGED-ONESHOT] CPU=2 checksum={checksum(2)} returned-native-park=1", ""),
    ]
    for candidate in bad:
        try:
            verify_one_shot(candidate, 3)
        except ManagedOneShotError:
            continue
        raise AssertionError("accepted forged/partial managed AP proof")
    print("[SMP3094-STAGE12-PARSER] PASS: valid/invalid managed one-shot evidence")


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
            ap.error("serial log required")
        try:
            print(check(args.log.read_text(errors="replace"), args.cpus))
        except (ManagedOneShotError, verify_native_context.NativeContextError,
                verify_boot.BootProofError, verify_timer_ready.TimerReadinessError,
                verify_periodic_timer.TimerDeliveryError) as error:
            ap.exit(1, f"[SMP3094-STAGE12-QEMU][FAIL] {error}\n")
