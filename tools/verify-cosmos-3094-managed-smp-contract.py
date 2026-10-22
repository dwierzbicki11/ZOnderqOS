#!/usr/bin/env python3
"""Fail-closed gate: native AP proof is NOT production managed SMP."""
from __future__ import annotations
import argparse
import re
from pathlib import Path

NATIVE = re.compile(r"\[SMP3094-STAGE(?:[4-9](?:[ab])?)\] PASS")
MANAGED = re.compile(r"\[SMP3094-MANAGED\] cpu=(\d+) thread=(\d+) dispatch=(\d+) gc_ack=(\d+) gc_resume=(\d+) preempt=(\d+)")
FINISH = "[SMP3094-MANAGED-STAGE23] PASS"
GC = "[SMP3094-ORIONGC-STW] PASS"
TIMER = "[SMP3094-STAGE9B] PASS"


def verify(log: str, cpus: int) -> str:
    if not 1 <= cpus <= 256:
        raise ValueError("CPU count outside supported 1..256")
    lines = [line.strip() for line in log.replace("\r", "").splitlines()]
    if any("PANIC" in line.upper() or "[FAIL]" in line for line in lines):
        raise ValueError("panic or failed gate present")
    if lines.count(FINISH) != 1 or lines.count(GC) != 1 or lines.count(TIMER) != 1:
        raise ValueError("missing or duplicate managed/GC/native timer gates")
    if not lines.index(TIMER) < lines.index(GC) < lines.index(FINISH):
        raise ValueError("wrong stage ordering")
    gc_index = lines.index(GC)
    finish_index = lines.index(FINISH)
    observed = {}
    for index, line in enumerate(lines):
        m = MANAGED.fullmatch(line)
        if not m:
            if line.startswith("[SMP3094-MANAGED]"):
                raise ValueError("malformed managed CPU evidence")
            continue
        if not gc_index < index < finish_index:
            raise ValueError("managed evidence outside GC-to-Stage23 acceptance window")
        cpu, thread, dispatch, ack, resume, preempt = map(int, m.groups())
        if cpu in observed or cpu >= cpus or thread == 0 or dispatch < 2 or ack < 1 or resume < 1 or preempt < 1:
            raise ValueError(f"invalid per-CPU managed evidence: {line}")
        observed[cpu] = (thread, dispatch, ack, resume, preempt)
    if set(observed) != set(range(cpus)):
        raise ValueError(f"missing managed CPU evidence: {set(range(cpus)) - set(observed)}")
    if len({value[0] for value in observed.values()}) < cpus:
        raise ValueError("threads are not uniquely identifiable per CPU")
    return f"[SMP3094-MANAGED-GATE] PASS cpus={cpus} managed={len(observed)}"


def self_test() -> None:
    sample = "\n".join([
        TIMER, GC,
        "[SMP3094-MANAGED] cpu=0 thread=1 dispatch=2 gc_ack=1 gc_resume=1 preempt=1",
        "[SMP3094-MANAGED] cpu=1 thread=2 dispatch=3 gc_ack=2 gc_resume=2 preempt=2",
        FINISH,
    ])
    assert "PASS" in verify(sample, 2)
    bad = [
        sample.replace(GC, ""),
        sample.replace(FINISH, "[SMP3094-STAGE8] PASS"),
        sample.replace("cpu=1", "cpu=0"),
        sample.replace("preempt=2", "preempt=0"),
        sample.replace("gc_ack=2", "gc_ack=0"),
        sample.replace("thread=2", "thread=1"),
        sample.replace("dispatch=3", "dispatch=0"),
        sample + "\n" + FINISH,
        sample + "\n[PANIC] simulated OrionGC failure",
        sample.replace(GC + "\n", "").replace(TIMER + "\n", TIMER + "\n" + " [SMP3094-ORIONGC-STW] PASS" + "\n"),
        sample.replace(GC + "\n", " [SMP3094-MANAGED] cpu=0 thread=1 dispatch=2 gc_ack=1 gc_resume=1 preempt=1\n" + GC + "\n"),
        sample + "\n[SMP3094-MANAGED] cpu=2 thread=3 dispatch=2 gc_ack=1 gc_resume=1 preempt=1",
    ]
    for text in bad:
        try:
            verify(text, 2)
        except ValueError:
            continue
        raise AssertionError("accepted forged/unsafe managed SMP evidence")
    print("[SMP3094-MANAGED-CONTRACT] PASS positive and negative cases")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("log", type=Path, nargs="?")
    parser.add_argument("--cpus", type=int, default=4)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
    else:
        if not args.log:
            parser.error("serial log required")
        try:
            print(verify(args.log.read_text(errors="replace"), args.cpus))
        except ValueError as error:
            parser.exit(1, f"[SMP3094-MANAGED-GATE][FAIL] {error}\n")
