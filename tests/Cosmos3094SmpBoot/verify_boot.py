#!/usr/bin/env python3
"""Strict serial evidence checker for real Cosmos 3.0.94 Stage 4–8 QEMU boots."""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

AP = re.compile(r"\[SMP3094\] AP READY CpuId=(\d+) LAPIC=(\d+)")
WORK = re.compile(r"\[SMP3094-WORK\] CPU=(\d+) counter=(\d+)")
IPI = re.compile(r"\[SMP3094-IPI\] CPU=(\d+) LAPIC=(\d+) count=(\d+)")
STRESS = re.compile(r"\[SMP3094-IPI-STRESS\] CPU=(\d+) LAPIC=(\d+) rounds=(\d+) count=(\d+)")
BARRIER = re.compile(
    r"\[SMP3094-BARRIER\] CPU=(\d+) LAPIC=(\d+) rounds=(\d+) enter=(\d+) exit=(\d+) hits=(\d+)")
STAGE4 = "[SMP3094-STAGE4] PASS"
STAGE5 = "[SMP3094-STAGE5] PASS"
STAGE6 = "[SMP3094-STAGE6] PASS"
STAGE7 = "[SMP3094-STAGE7] PASS"
STAGE8 = "[SMP3094-STAGE8] PASS"
BOOT = "[SMP3094-PROBE] BOOT"
FATAL = re.compile(
    r"\bpanic\b|\btriple[ -]fault\b|\bCPU\s+EXCEPTION(?!\s+handlers\s+registered)\b|"
    r"Unhandled Exception|System halted|\[SMP3094\].*(?:WARNING|failed|invalid|timeout)|"
    r"general protection fault|page fault",
    re.IGNORECASE,
)


class BootProofError(ValueError):
    pass


def check(text: str, cpus: int) -> str:
    if not 1 <= cpus <= 256:
        raise BootProofError("CPU count must be in 1..256")
    lines = [line.strip() for line in text.replace("\r", "").splitlines()]
    for line in lines:
        if FATAL.search(line):
            raise BootProofError("kernel error in serial log: " + line)

    def marker(token: str) -> int:
        hits = [idx for idx, line in enumerate(lines) if line == token]
        if len(hits) != 1:
            raise BootProofError(f"expected one {token}, observed {len(hits)}")
        return hits[0]

    stage4, stage5, stage6, stage7, stage8, boot = (
        marker(STAGE4), marker(STAGE5), marker(STAGE6), marker(STAGE7),
        marker(STAGE8), marker(BOOT))
    if not stage4 < stage5 < stage6 < stage7 < stage8 < boot:
        raise BootProofError("Stage4 / Stage5 / Stage6 / Stage7 / Stage8 / BOOT markers out of order")

    ap_ids: dict[int, int] = {}
    work_ids: dict[int, int] = {}
    ipi_ids: dict[int, tuple[int, int]] = {}
    stress_ids: dict[int, tuple[int, int, int]] = {}
    barrier_ids: dict[int, tuple[int, int, int, int, int]] = {}
    ap_pos: list[int] = []
    work_pos: list[int] = []
    ipi_pos: list[int] = []
    stress_pos: list[int] = []
    barrier_pos: list[int] = []
    for idx, line in enumerate(lines):
        a = AP.fullmatch(line)
        if a:
            cpu_id, lapic_id = map(int, a.groups())
            if cpu_id in ap_ids or cpu_id == 0 or cpu_id >= cpus:
                raise BootProofError("duplicated / invalid AP CpuId")
            if lapic_id in ap_ids.values():
                raise BootProofError("duplicated physical LAPIC ID")
            ap_ids[cpu_id] = lapic_id
            ap_pos.append(idx)
        elif line.startswith("[SMP3094] AP READY"):
            raise BootProofError("malformed AP READY line")
        w = WORK.fullmatch(line)
        if w:
            cpu_id, value = map(int, w.groups())
            if cpu_id in work_ids or cpu_id == 0 or cpu_id >= cpus:
                raise BootProofError("duplicated / invalid AP work CpuId")
            work_ids[cpu_id] = value
            work_pos.append(idx)
        elif line.startswith("[SMP3094-WORK]"):
            raise BootProofError("malformed work proof line")
        z = IPI.fullmatch(line)
        if z:
            cpu_id, lapic_id, count = map(int, z.groups())
            if cpu_id in ipi_ids or cpu_id == 0 or cpu_id >= cpus:
                raise BootProofError("duplicated / invalid AP IPI CpuId")
            ipi_ids[cpu_id] = (lapic_id, count)
            ipi_pos.append(idx)
        elif line.startswith("[SMP3094-IPI]"):
            raise BootProofError("malformed native IPI proof line")
        s = STRESS.fullmatch(line)
        if s:
            cpu_id, lapic_id, rounds, count = map(int, s.groups())
            if cpu_id in stress_ids or cpu_id == 0 or cpu_id >= cpus:
                raise BootProofError("duplicated / invalid AP IPI stress CpuId")
            stress_ids[cpu_id] = (lapic_id, rounds, count)
            stress_pos.append(idx)
        elif line.startswith("[SMP3094-IPI-STRESS]"):
            raise BootProofError("malformed IPI stress proof line")
        b = BARRIER.fullmatch(line)
        if b:
            cpu_id, lapic_id, rounds, entered, exited, hits = map(int, b.groups())
            if cpu_id in barrier_ids or cpu_id == 0 or cpu_id >= cpus:
                raise BootProofError("duplicate / invalid barrier CpuId")
            barrier_ids[cpu_id] = (lapic_id, rounds, entered, exited, hits)
            barrier_pos.append(idx)
        elif line.startswith("[SMP3094-BARRIER]"):
            raise BootProofError("malformed rendezvous proof line")

    required = set(range(1, cpus))
    if (set(ap_ids) != required or set(work_ids) != required or
            set(ipi_ids) != required or set(stress_ids) != required or
            set(barrier_ids) != required):
        raise BootProofError(
            f"wrong AP masks: READY={sorted(ap_ids)} WORK={sorted(work_ids)} IPI={sorted(ipi_ids)} STRESS={sorted(stress_ids)} BARRIER={sorted(barrier_ids)}, "
            f"expected {sorted(required)}")
    if any(ipi_ids[cpu_id] != (ap_ids[cpu_id], 1) for cpu_id in required):
        raise BootProofError("IPI LAPIC ID or acknowledgement count incorrect")
    if any(stress_ids[cpu_id] != (ap_ids[cpu_id], 32, 33) for cpu_id in required):
        raise BootProofError("IPI stress LAPIC ID / round count / final counter incorrect")
    if any(barrier_ids[cpu_id] != (ap_ids[cpu_id], 4, 4, 4, 4)
           for cpu_id in required):
        raise BootProofError("native rendezvous LAPIC/rounds/entry/exit/hit mismatch")
    if any(value != 100_000 for value in work_ids.values()):
        raise BootProofError("AP work counter did not reach exactly 100000")
    if any(index >= stage4 for index in ap_pos) or any(
        index <= stage4 or index >= stage5 for index in work_pos
    ) or any(index <= stage5 or index >= stage6 for index in ipi_pos) or any(
        index <= stage6 or index >= stage7 for index in stress_pos
    ) or any(index <= stage7 or index >= stage8 for index in barrier_pos):
        raise BootProofError("AP READY / work / IPI / stress markers out of order")
    if not any(
        line == f"[SMP3094] Stage 4 complete: {cpus - 1} native APs ready and parked; no AP scheduler or GC."
        for line in lines
    ):
        raise BootProofError("Stage4 native AP count is incorrect")
    return (f"[SMP3094-QEMU] PASS cpus={cpus} native_aps={cpus - 1} "
            "work_per_ap=100000 ipi_per_ap=1 stress_rounds=32 final_ipi_count=33 rendezvous_rounds=4")


def self_test() -> None:
    def good(count: int) -> str:
        return "\n".join([
            *[f"[SMP3094] AP READY CpuId={i} LAPIC={i}" for i in range(1, count)],
            f"[SMP3094] Stage 4 complete: {count - 1} native APs ready and parked; no AP scheduler or GC.",
            STAGE4,
            *[f"[SMP3094-WORK] CPU={i} counter=100000" for i in range(1, count)],
            STAGE5,
            *[f"[SMP3094-IPI] CPU={i} LAPIC={i} count=1" for i in range(1, count)],
            STAGE6,
            *[f"[SMP3094-IPI-STRESS] CPU={i} LAPIC={i} rounds=32 count=33"
              for i in range(1, count)],
            STAGE7,
            *[f"[SMP3094-BARRIER] CPU={i} LAPIC={i} rounds=4 enter=4 exit=4 hits=4"
              for i in range(1, count)],
            STAGE8,
            BOOT,
        ]) + "\n"

    for count in (1, 2, 4, 8, 16):
        check(good(count), count)
        # The normal exception handler registration banner is not a CPU fault.
        check("[ExceptionHandler] CPU exception handlers registered\n" + good(count), count)
    corrupt = [
        (good(4).replace("counter=100000", "counter=99999", 1), 4),
        (good(4).replace(BOOT, ""), 4),
        (good(4).replace("count=1", "count=0", 1), 4),
        (good(4).replace("LAPIC=2 count=1", "LAPIC=7 count=1", 1), 4),
        (good(4).replace(STAGE6, ""), 4),
        (good(4).replace(STAGE7, ""), 4),
        (good(4).replace(STAGE8, ""), 4),
        (good(4).replace("rounds=4 enter=4", "rounds=3 enter=4", 1), 4),
        (good(4).replace("enter=4 exit=4", "enter=3 exit=4", 1), 4),
        (good(4).replace("exit=4 hits=4", "exit=3 hits=4", 1), 4),
        (good(4).replace("exit=4 hits=4", "exit=4 hits=5", 1), 4),
        (good(4).replace("LAPIC=2 rounds=4", "LAPIC=8 rounds=4", 1), 4),
        (good(4).replace("[SMP3094-BARRIER] CPU=2", "[SMP3094-BARRIER] CPU=1", 1), 4),
        (good(4).replace(STAGE8, STAGE7), 4),
        (good(4).replace("rounds=32", "rounds=31", 1), 4),
        (good(4).replace("rounds=32 count=33", "rounds=32 count=32", 1), 4),
        (good(4).replace("rounds=32 count=33", "rounds=32 count=34", 1), 4),
        (good(4).replace("LAPIC=2 rounds=32", "LAPIC=7 rounds=32", 1), 4),
        (good(4).replace("[SMP3094-IPI-STRESS] CPU=2", "[SMP3094-IPI-STRESS] CPU=1", 1), 4),
        (good(4).replace(STAGE7, STAGE6), 4),
        (good(4).replace("[SMP3094-IPI] CPU=2", "[SMP3094-IPI] CPU=1", 1), 4),
        (good(4).replace(STAGE5, STAGE4), 4),
        (good(4).replace("CpuId=2", "CpuId=1", 1), 4),
        (good(4).replace("LAPIC=2", "LAPIC=1", 1), 4),
        (good(4) + BOOT + "\n", 4),
        (good(4) + "Kernel panic\n", 4),
        (good(4) + "CPU EXCEPTION #13\n", 4),
        (good(4), 8),
    ]
    for serial, count in corrupt:
        try:
            check(serial, count)
        except BootProofError:
            continue
        raise AssertionError("bad serial evidence was incorrectly accepted")
    print("[SMP3094] serial proof parser self-test PASS")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("serial", nargs="?")
    parser.add_argument("--cpus", type=int, default=4)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return
    if not args.serial:
        parser.error("serial file is required")
    try:
        print(check(Path(args.serial).read_text(errors="replace"), args.cpus))
    except (OSError, BootProofError) as error:
        print("[SMP3094-QEMU][FAIL] " + str(error), file=sys.stderr)
        raise SystemExit(1) from error


if __name__ == "__main__":
    main()
