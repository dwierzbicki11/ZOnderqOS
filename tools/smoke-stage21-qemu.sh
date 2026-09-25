#!/usr/bin/env bash
set -euo pipefail

CHECK_LOG=0
if [[ "${1:-}" == --check-log ]]; then
  CHECK_LOG=1
  shift
fi

ISO="${1:-output-x64/ZonderqOS.iso}"
LOG="${2:-stage21-qemu-serial.log}"
CPUS="${STAGE21_QEMU_CPUS:-8}"

export STAGE20_QEMU_CPU_MODEL="${STAGE21_QEMU_CPU_MODEL:-Nehalem}"
export STAGE20_QEMU_SOCKETS="${STAGE21_QEMU_SOCKETS:-1}"
export STAGE20_QEMU_CPUS="$CPUS"
export STAGE20_QEMU_CORES="${STAGE21_QEMU_CORES:-4}"
export STAGE20_QEMU_THREADS="${STAGE21_QEMU_THREADS:-2}"
export STAGE20_QEMU_TIMEOUT_SECONDS="${STAGE21_QEMU_TIMEOUT_SECONDS:-105}"
export STAGE20_QEMU_STABLE_SECONDS="${STAGE21_QEMU_STABLE_SECONDS:-30}"

if (( CHECK_LOG )); then
  bash tools/smoke-stage20-qemu.sh --check-log "$ISO" "$LOG"
else
  bash tools/smoke-stage20-qemu.sh "$ISO" "$LOG"
fi

python3 - "$LOG" "$CPUS" <<'PY'
import pathlib
import re
import sys

path, expected = pathlib.Path(sys.argv[1]), int(sys.argv[2])
lines = [line.rstrip("\r") for line in path.read_text(errors="replace").split("\n")[:-1]]

def fail(message):
    print("[SMT21-QEMU][FAIL] " + message, file=sys.stderr)
    raise SystemExit(1)

begins = [i for i, line in enumerate(lines) if line == "[SCHED-AUTO-BALANCE] begin"]
passes = [i for i, line in enumerate(lines) if line == "[SCHED-AUTO-BALANCE-TEST] PASS"]
if len(begins) != 1 or len(passes) != 1 or begins[0] >= passes[0]:
    fail("missing or duplicated Stage-21 proof markers")

summary_re = re.compile(
    r"\[SCHED-AUTO-BALANCE\] migrations=(\d+) sources=0x([0-9a-fA-F]+) "
    r"targets=0x([0-9a-fA-F]+)(?: preemptions=(\d+) resumes=(\d+))? "
    r"gc-scanned=(\d+) registry=restored")
summaries = []
for line in lines[begins[0] + 1:passes[0]]:
    match = summary_re.fullmatch(line)
    if match:
        summaries.append((
            int(match.group(1)), int(match.group(2), 16), int(match.group(3), 16),
            int(match.group(4) or 0), int(match.group(5) or 0), int(match.group(6))))
if len(summaries) != 1:
    fail("missing or duplicated Stage-21 balancing summary")

migrations, sources, targets, preemptions, resumes, scanned = summaries[0]
if expected == 1:
    if any((migrations, sources, targets, preemptions, resumes, scanned)):
        fail("single-CPU Stage-21 fallback reported AP work")
    print("[SMT21-QEMU][OK] single-CPU fallback preserved the scheduler and registry")
    raise SystemExit(0)

if expected != 8:
    fail(f"unsupported Stage-21 topology: {expected} CPUs")
required_sources = (1 << 1) | (1 << 7)
if migrations < 4 or sources & required_sources != required_sources:
    fail(f"automatic balancing did not run from both imbalanced owners: migrations={migrations} sources=0x{sources:x}")
if targets & 1 or targets.bit_count() < 3:
    fail(f"automatic balancing did not reserve at least three AP targets: 0x{targets:x}")
if preemptions < 16 or resumes < 8 + migrations:
    fail(f"timer/context continuation proof too weak: preemptions={preemptions} resumes={resumes} migrations={migrations}")
if scanned != 7:
    fail(f"OrionGC scanned {scanned} AP stacks instead of 7")
print(f"[SMT21-QEMU][OK] {migrations} owner-driven migrations from both edge APs reached {targets.bit_count()} targets with timer, GC and registry proof")
PY
