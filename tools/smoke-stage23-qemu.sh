#!/usr/bin/env bash
set -euo pipefail

CHECK_LOG=0
if [[ "${1:-}" == --check-log ]]; then
  CHECK_LOG=1
  shift
fi

ISO="${1:-output-x64/ZonderqOS.iso}"
LOG="${2:-stage23-qemu-serial.log}"
CPUS="${STAGE23_QEMU_CPUS:-8}"

export STAGE22_QEMU_CPU_MODEL="${STAGE23_QEMU_CPU_MODEL:-Nehalem}"
export STAGE22_QEMU_SOCKETS="${STAGE23_QEMU_SOCKETS:-1}"
export STAGE22_QEMU_CPUS="$CPUS"
export STAGE22_QEMU_CORES="${STAGE23_QEMU_CORES:-4}"
export STAGE22_QEMU_THREADS="${STAGE23_QEMU_THREADS:-2}"
export STAGE22_QEMU_TIMEOUT_SECONDS="${STAGE23_QEMU_TIMEOUT_SECONDS:-210}"
export STAGE22_QEMU_STABLE_SECONDS="${STAGE23_QEMU_STABLE_SECONDS:-60}"

if (( CHECK_LOG )); then
  bash tools/smoke-stage22-qemu.sh --check-log "$ISO" "$LOG"
else
  bash tools/smoke-stage22-qemu.sh "$ISO" "$LOG"
fi

python3 - "$LOG" "$CPUS" <<'PY'
import pathlib
import re
import sys

path, expected = pathlib.Path(sys.argv[1]), int(sys.argv[2])
lines = [line.rstrip("\r") for line in path.read_text(errors="replace").split("\n")[:-1]]

def fail(message):
    print("[SMT23-QEMU][FAIL] " + message, file=sys.stderr)
    raise SystemExit(1)

begins = [i for i, line in enumerate(lines) if line == "[SCHED-PRODUCTION-SMP] begin"]
passes = [i for i, line in enumerate(lines) if line == "[SCHED-PRODUCTION-SMP-TEST] PASS"]
summary_re = re.compile(
    r"\[SCHED-PRODUCTION-SMP\] workers=(\d+) unique-cpus=(\d+) "
    r"cpu-mask=0x([0-9a-fA-F]+) registry=restored aps=parked")
summaries = [(i, summary_re.fullmatch(line)) for i, line in enumerate(lines)]
summaries = [(i, match) for i, match in summaries if match]

if len(begins) != 1 or len(passes) != 1 or len(summaries) != 1:
    fail(
        f"missing or duplicated Stage-23 markers: "
        f"begins={len(begins)} summaries={len(summaries)} passes={len(passes)}")
summary_index, match = summaries[0]
if not (begins[0] < summary_index < passes[0]):
    fail("Stage-23 markers are out of order")

workers = int(match.group(1))
unique = int(match.group(2))
mask = int(match.group(3), 16)

expected_workers = expected
expected_mask = (1 << min(expected, 64)) - 1 if expected > 0 else 0

if workers != expected_workers:
    fail(f"kernel ran {workers} production AP workers, expected {expected_workers}")
if unique != expected:
    fail(f"kernel observed {unique} logical CPUs, expected {expected}")
if mask != expected_mask:
    fail(f"kernel CPU mask is 0x{mask:x}, expected 0x{expected_mask:x}")

htop = [line for line in lines if line.startswith("[HTOP-TEST]")]
expected_htop = f"[HTOP-TEST] PASS cpus={expected} mask=0x{expected_mask:X} processes=restored"
begins = [line for line in htop if line == "[HTOP-TEST] BEGIN"]
passes = [line for line in htop if line.startswith("[HTOP-TEST] PASS ")]
failures = [line for line in htop if line.startswith("[HTOP-TEST] FAIL ")]
if begins != ["[HTOP-TEST] BEGIN"] or passes != [expected_htop] or failures:
    fail(
        f"htop runtime verification missing or failed: "
        f"begins={begins} passes={passes} failures={failures}")

print(
    f"[SMT23-QEMU][OK] htop measured every logical CPU and restored processes")
print(
    f"[SMT23-QEMU][OK] ordinary managed threads executed across "
    f"{unique} logical CPUs (mask=0x{mask:x}); registry and AP idle state restored")
PY
