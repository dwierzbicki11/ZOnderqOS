#!/usr/bin/env bash
set -euo pipefail

CHECK_LOG=0
if [[ "${1:-}" == --check-log ]]; then
  CHECK_LOG=1
  shift
fi

ISO="${1:-output-x64/ZonderqOS.iso}"
LOG="${2:-stage19-qemu-serial.log}"
CPUS="${STAGE19_QEMU_CPUS:-8}"

export STAGE18_QEMU_CPU_MODEL="${STAGE19_QEMU_CPU_MODEL:-Nehalem}"
export STAGE18_QEMU_SOCKETS="${STAGE19_QEMU_SOCKETS:-1}"
export STAGE18_QEMU_CPUS="$CPUS"
export STAGE18_QEMU_CORES="${STAGE19_QEMU_CORES:-4}"
export STAGE18_QEMU_THREADS="${STAGE19_QEMU_THREADS:-2}"
export STAGE18_QEMU_TIMEOUT_SECONDS="${STAGE19_QEMU_TIMEOUT_SECONDS:-90}"
export STAGE18_QEMU_STABLE_SECONDS="${STAGE19_QEMU_STABLE_SECONDS:-30}"

if (( CHECK_LOG )); then
  bash tools/smoke-stage18-qemu.sh --check-log "$ISO" "$LOG"
else
  bash tools/smoke-stage18-qemu.sh "$ISO" "$LOG"
fi

python3 - "$LOG" "$CPUS" <<'PY'
import pathlib
import re
import sys

path, expected = pathlib.Path(sys.argv[1]), int(sys.argv[2])
lines = [line.rstrip("\r") for line in path.read_text(errors="replace").split("\n")[:-1]]
expected_aps = max(0, expected - 1)

def fail(message):
    print("[SMT19-QEMU][FAIL] " + message, file=sys.stderr)
    raise SystemExit(1)

begins = [i for i, line in enumerate(lines) if line == "[SCHED-MIGRATE] begin"]
passes = [i for i, line in enumerate(lines) if line == "[SCHED-MIGRATE-TEST] PASS"]
if len(begins) != 1 or len(passes) != 1 or begins[0] >= passes[0]:
    raise SystemExit(2)
begin, passed = begins[0], passes[0]

ready_re = re.compile(r"\[SCHED-MIGRATE\] thread=(\d+) source=1 target=(\d+) slot=([01]) state=ready-rehomed")
runtime_re = re.compile(r"\[SCHED-MIGRATE-RUNTIME\] cpu=(\d+) worker=([01]) thread=(\d+) preemptions=(\d+) checksum=0x([0-9a-fA-F]+) state=migrated-preempted-exited-idle-restored")
summary_re = re.compile(r"\[SCHED-MIGRATE\] gc-collection=(\d+) scanned-aps=(\d+) workers=(\d+) registry=restored")
ready, runtime, tids, summaries = {}, {}, set(), []
for line in lines[begin + 1:passed]:
    match = ready_re.fullmatch(line)
    if match:
        tid, cpu, slot = map(int, match.groups())
        key = (cpu, slot)
        if cpu not in range(1, expected) or key in ready or tid == 0:
            fail("invalid ready-thread migration: " + line)
        ready[key] = tid
        continue
    match = runtime_re.fullmatch(line)
    if match:
        cpu, slot, tid, preemptions = map(int, match.group(1, 2, 3, 4))
        checksum = int(match.group(5), 16)
        key = (cpu, slot)
        if key in runtime or tid in tids or preemptions < 2:
            fail("invalid migrated worker runtime: " + line)
        if checksum != (0x534D503139000000 ^ (cpu << 32) ^ (slot << 24) ^ tid):
            fail("Stage-19 checksum mismatch: " + line)
        runtime[key] = tid
        tids.add(tid)
        continue
    match = summary_re.fullmatch(line)
    if match:
        summaries.append(tuple(map(int, match.groups())))

expected_keys = {(cpu, slot) for cpu in range(1, expected) for slot in range(2)}
if set(ready) != expected_keys or set(runtime) != expected_keys or ready != runtime or len(summaries) != 1:
    raise SystemExit(2)
collection, scanned, worker_count = summaries[0]
if collection <= 0 or scanned != expected_aps or worker_count != expected_aps * 2:
    fail("Stage-19 GC/registry summary mismatch")
print(f"[SMT19-QEMU][OK] {worker_count} unpinned workers were balanced across {expected_aps} AP queues, preempted, GC-scanned and retired")
PY
