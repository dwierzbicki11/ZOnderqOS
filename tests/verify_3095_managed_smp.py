#!/usr/bin/env python3
"""Fail-closed evidence gate for actual managed SMP on Cosmos 3.0.95.

Only explicit per-CPU execution reports from a dedicated test image count.
ACPI enumeration, scheduler idle slots, and timer ticks do NOT prove SMP.
"""
from __future__ import annotations
import argparse
from pathlib import Path
import re

PREFIX = "[SMP3095-STAGE23]"
WORKER = re.compile(r"^\[SMP3095-STAGE23\] WORKER cpu=(\d+) rounds=(\d+) checksum=([0-9A-Fa-f]{8,16})$")
START = re.compile(r"^\[SMP3095-STAGE23\] START cpus=(\d+) rounds=(\d+)$")
END = re.compile(r"^\[SMP3095-STAGE23\] PASS cpus=(\d+) rounds=(\d+)$")
FAILURE = re.compile(r"panic|fatal:\s*exception|page fault|triple fault|\[SMP3095-STAGE23\] FAIL", re.I)

def verify(log: str, cpus: int) -> str:
    if cpus not in (1, 2, 4, 8, 16, 32, 56):
        raise ValueError("unsupported CPU matrix size")
    lines = [line.strip() for line in log.splitlines()]
    if any(FAILURE.search(line) for line in lines):
        raise ValueError("kernel or SMP failure marker")
    starts = [(i, START.fullmatch(s)) for i, s in enumerate(lines) if START.fullmatch(s)]
    ends = [(i, END.fullmatch(s)) for i, s in enumerate(lines) if END.fullmatch(s)]
    if len(starts) != 1 or len(ends) != 1:
        raise ValueError("expected one START and one PASS")
    first, start = starts[0]
    last, end = ends[0]
    n, rounds = int(start[1]), int(start[2])
    if n != cpus or rounds < 1000 or rounds > 10000000 or end[1] != start[1] or end[2] != start[2]:
        raise ValueError("unexpected topology or work count")
    observations = [(i, WORKER.fullmatch(s)) for i, s in enumerate(lines) if WORKER.fullmatch(s)]
    if len(observations) != cpus or not first < last:
        raise ValueError("worker evidence incomplete or duplicated")
    ids = set()
    for position, match in observations:
        cpu = int(match[1])
        if not first < position < last or cpu in ids or cpu >= cpus or int(match[2]) != rounds:
            raise ValueError("worker identity, order or iteration mismatch")
        if int(match[3], 16) == 0:
            raise ValueError("invalid zero checksum")
        ids.add(cpu)
    if ids != set(range(cpus)):
        raise ValueError("not all logical CPUs executed work")
    return f"{PREFIX} VERIFIED cpus={cpus} rounds={rounds}"

def selftest() -> None:
    valid = "\n".join([
        f"{PREFIX} START cpus=2 rounds=1000",
        f"{PREFIX} WORKER cpu=0 rounds=1000 checksum=00000001",
        f"{PREFIX} WORKER cpu=1 rounds=1000 checksum=00000002",
        f"{PREFIX} PASS cpus=2 rounds=1000",
    ])
    assert "VERIFIED" in verify(valid, 2)
    invalid = [
        valid.replace("cpu=1", "cpu=0"),
        valid.replace("cpu=1", "cpu=2"),
        valid.replace("rounds=1000 checksum=00000002", "rounds=999 checksum=00000002"),
        valid.replace("checksum=00000002", "checksum=00000000"),
        valid.replace(" WORKER cpu=1 rounds=1000 checksum=00000002", ""),
        valid + "\nFatal: exception 14",
        valid + "\nKERNEL PANIC",
        valid + "\n" + valid.splitlines()[1],
        valid.replace("PASS cpus=2", "PASS cpus=8"),
    ]
    for sample in invalid:
        try:
            verify(sample, 2)
        except ValueError:
            continue
        raise AssertionError("invalid evidence accepted")
    print("[SMP3095-CONTRACT] PASS synthetic positive/negative gates (not kernel proof)")

if __name__ == "__main__":
    p = argparse.ArgumentParser()
    p.add_argument("log", nargs="?", type=Path)
    p.add_argument("--cpus", type=int, default=8)
    p.add_argument("--self-test", action="store_true")
    a = p.parse_args()
    if a.self_test:
        selftest()
    elif a.log:
        try:
            print(verify(a.log.read_text(errors="replace"), a.cpus))
        except ValueError as e:
            p.exit(1, f"[SMP3095-STAGE23] REJECTED: {e}\n")
    else:
        p.error("pass a real QEMU serial log or --self-test")
