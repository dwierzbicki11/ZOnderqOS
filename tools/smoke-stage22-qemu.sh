#!/usr/bin/env bash
set -euo pipefail

CHECK_LOG=0
if [[ "${1:-}" == --check-log ]]; then
  CHECK_LOG=1
  shift
fi

ISO="${1:-output-x64/ZonderqOS.iso}"
LOG="${2:-stage22-qemu-serial.log}"
CPUS="${STAGE22_QEMU_CPUS:-8}"

export STAGE20_QEMU_CPU_MODEL="${STAGE22_QEMU_CPU_MODEL:-Nehalem}"
export STAGE20_QEMU_SOCKETS="${STAGE22_QEMU_SOCKETS:-1}"
export STAGE20_QEMU_CPUS="$CPUS"
export STAGE20_QEMU_CORES="${STAGE22_QEMU_CORES:-4}"
export STAGE20_QEMU_THREADS="${STAGE22_QEMU_THREADS:-2}"
export STAGE20_QEMU_TIMEOUT_SECONDS="${STAGE22_QEMU_TIMEOUT_SECONDS:-105}"
export STAGE20_QEMU_STABLE_SECONDS="${STAGE22_QEMU_STABLE_SECONDS:-30}"

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
    print("[SMT22-QEMU][FAIL] " + message, file=sys.stderr)
    raise SystemExit(1)

begins = [i for i, line in enumerate(lines) if line == "[SCHED-AUTO-BALANCE] begin"]
passes = [i for i, line in enumerate(lines) if line == "[SCHED-AUTO-BALANCE-TEST] PASS"]
if len(begins) != 9 or len(passes) != 9 or any(begin >= passed for begin, passed in zip(begins, passes)):
    fail(f"expected nine ordered Stage-21 generations, got begins={len(begins)} passes={len(passes)}")

summary_re = re.compile(
    r"\[SCHED-AUTO-BALANCE\] migrations=(\d+) sources=0x([0-9a-fA-F]+) "
    r"targets=0x([0-9a-fA-F]+)(?: preemptions=(\d+) resumes=(\d+))? "
    r"gc-scanned=(\d+) registry=restored")
summaries = []
for begin, passed in zip(begins, passes):
    matches = [summary_re.fullmatch(line) for line in lines[begin + 1:passed]]
    matches = [match for match in matches if match]
    if len(matches) != 1:
        fail("a Stage-21 generation is missing its unique balancing summary")
    match = matches[0]
    summaries.append((
        int(match.group(1)), int(match.group(2), 16), int(match.group(3), 16),
        int(match.group(4) or 0), int(match.group(5) or 0), int(match.group(6))))

stress_begins = [i for i, line in enumerate(lines) if line == "[SCHED-SMP-STRESS] begin"]
stress_passes = [i for i, line in enumerate(lines) if line == "[SCHED-SMP-STRESS-TEST] PASS"]
stress_re = re.compile(
    r"\[SCHED-SMP-STRESS\] rounds=(\d+) migrations=(\d+) preemptions=(\d+) resumes=(\d+) "
    r"sources=0x([0-9a-fA-F]+) targets=0x([0-9a-fA-F]+) gc-collections=(\d+) "
    r"registry=restored idle=restored")
stress_matches = [stress_re.fullmatch(line) for line in lines]
stress_matches = [match for match in stress_matches if match]
if len(stress_begins) != 1 or len(stress_passes) != 1 or len(stress_matches) != 1 or not (stress_begins[0] < stress_passes[0]):
    fail("missing or duplicated Stage-22 stress markers")

stress = tuple(int(value, 16) if index in (4, 5) else int(value)
               for index, value in enumerate(stress_matches[0].groups()))
rounds, migrations, preemptions, resumes, sources, targets, collections = stress
stress_rounds = summaries[1:]
if expected == 1:
    if rounds != 8 or collections != 0 or any((migrations, sources, targets, preemptions, resumes)):
        fail("single-CPU Stage-22 fallback reported AP work")
    if any(any(summary) for summary in summaries):
        fail("a single-CPU Stage-21 generation reported AP work")
    print("[SMT22-QEMU][OK] eight single-CPU stress generations preserved scheduler state")
    raise SystemExit(0)

if expected != 8:
    fail(f"unsupported Stage-22 topology: {expected} CPUs")
required_sources = (1 << 1) | (1 << 7)
for index, (generation_migrations, generation_sources, generation_targets,
            generation_preemptions, generation_resumes, generation_scanned) in enumerate(summaries, 1):
    if generation_migrations < 4 or generation_sources & required_sources != required_sources:
        fail(f"generation {index} did not balance from both edge APs")
    if generation_targets & 1 or generation_targets.bit_count() < 3:
        fail(f"generation {index} reached too few targets: 0x{generation_targets:x}")
    if generation_preemptions < 16 or generation_resumes < 8:
        fail(f"generation {index} has incomplete timer/context continuation evidence")
    if generation_scanned != 7:
        fail(f"generation {index} scanned {generation_scanned} AP stacks instead of 7")

if rounds != 8 or collections != 8:
    fail(f"stress aggregate reports rounds={rounds} collections={collections}, expected 8/8")
if migrations != sum(summary[0] for summary in stress_rounds):
    fail("stress migration aggregate does not equal its eight generations")
if preemptions != sum(summary[3] for summary in stress_rounds):
    fail("stress preemption aggregate does not equal its eight generations")
if resumes != sum(summary[4] for summary in stress_rounds):
    fail("stress resume aggregate does not equal its eight generations")
if sources != 0x82 or sources != __import__('functools').reduce(lambda value, summary: value | summary[1], stress_rounds, 0):
    fail(f"stress source aggregate is invalid: 0x{sources:x}")
if targets & 1 or targets.bit_count() < 3 or targets != __import__('functools').reduce(lambda value, summary: value | summary[2], stress_rounds, 0):
    fail(f"stress target aggregate is invalid: 0x{targets:x}")
print(f"[SMT22-QEMU][OK] {rounds} stress generations completed {migrations} live migrations, {preemptions} preemptions, {resumes} resumes and {collections} OrionGC/STW cycles")
PY
