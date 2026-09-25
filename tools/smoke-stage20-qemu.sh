#!/usr/bin/env bash
set -euo pipefail

CHECK_LOG=0
if [[ "${1:-}" == --check-log ]]; then
  CHECK_LOG=1
  shift
fi

ISO="${1:-output-x64/ZonderqOS.iso}"
LOG="${2:-stage20-qemu-serial.log}"
CPUS="${STAGE20_QEMU_CPUS:-8}"

export STAGE19_QEMU_CPU_MODEL="${STAGE20_QEMU_CPU_MODEL:-Nehalem}"
export STAGE19_QEMU_SOCKETS="${STAGE20_QEMU_SOCKETS:-1}"
export STAGE19_QEMU_CPUS="$CPUS"
export STAGE19_QEMU_CORES="${STAGE20_QEMU_CORES:-4}"
export STAGE19_QEMU_THREADS="${STAGE20_QEMU_THREADS:-2}"
export STAGE19_QEMU_TIMEOUT_SECONDS="${STAGE20_QEMU_TIMEOUT_SECONDS:-90}"
export STAGE19_QEMU_STABLE_SECONDS="${STAGE20_QEMU_STABLE_SECONDS:-30}"

if (( CHECK_LOG )); then
  bash tools/smoke-stage19-qemu.sh --check-log "$ISO" "$LOG"
else
  bash tools/smoke-stage19-qemu.sh "$ISO" "$LOG"
fi

python3 - "$LOG" "$CPUS" <<'PY'
import pathlib
import re
import sys

path, expected = pathlib.Path(sys.argv[1]), int(sys.argv[2])
lines = [line.rstrip("\r") for line in path.read_text(errors="replace").split("\n")[:-1]]
expected_aps = max(0, expected - 1)

def fail(message):
    print("[SMT20-QEMU][FAIL] " + message, file=sys.stderr)
    raise SystemExit(1)

begins = [i for i, line in enumerate(lines) if line == "[SCHED-LIVE-MIGRATE] begin"]
passes = [i for i, line in enumerate(lines) if line == "[SCHED-LIVE-MIGRATE-TEST] PASS"]
if len(begins) != 1 or len(passes) != 1 or begins[0] >= passes[0]:
    fail("missing or duplicated Stage-20 proof markers")
begin, passed = begins[0], passes[0]

hop_re = re.compile(r"\[SCHED-LIVE-MIGRATE\] thread=(\d+) source=(\d+) target=(\d+) source-out=(\d+) target-in=(\d+) preemptions=(\d+) state=context-resumed")
summary_re = re.compile(r"\[SCHED-LIVE-MIGRATE\] gc-collection=(\d+) scanned-aps=(\d+) hops=(\d+) checksum=0x([0-9a-fA-F]+) registry=restored")
hops, summaries = [], []
for line in lines[begin + 1:passed]:
    match = hop_re.fullmatch(line)
    if match:
        hops.append(tuple(map(int, match.groups())))
        continue
    match = summary_re.fullmatch(line)
    if match:
        summaries.append((int(match.group(1)), int(match.group(2)),
                          int(match.group(3)), int(match.group(4), 16)))

if len(summaries) != 1:
    fail("missing or duplicated Stage-20 GC/registry summary")
collection, scanned, hop_count, checksum = summaries[0]

if expected == 1:
    if hops or collection != 0 or scanned != 0 or hop_count != 0 or checksum != 0:
        fail("single-CPU Stage-20 fallback reported AP work")
    print("[SMT20-QEMU][OK] single-CPU fallback preserved the scheduler and registry")
    raise SystemExit(0)

if expected != 8:
    fail(f"unsupported Stage-20 topology: {expected} CPUs")
if len(hops) != 6 or hop_count != 6:
    fail(f"expected 6 live context handoffs, got {len(hops)} records and summary={hop_count}")

thread_ids = {hop[0] for hop in hops}
if len(thread_ids) != 1 or 0 in thread_ids:
    fail("handoffs did not preserve one nonzero managed thread identity")
thread_id = next(iter(thread_ids))
for index, hop in enumerate(hops):
    tid, source, target, source_out, target_in, preemptions = hop
    expected_source, expected_target = index + 1, index + 2
    if tid != thread_id or source != expected_source or target != expected_target:
        fail(f"broken AP handoff chain at hop {index + 1}: {hop}")
    if source_out != 1 or target_in != 1 or preemptions < 2:
        fail(f"handoff lacks queue ownership/preemption proof: {hop}")

expected_checksum = 0x534D503140000000 ^ (7 << 32) ^ (6 << 24) ^ thread_id
if collection <= 0 or scanned != expected_aps or checksum != expected_checksum:
    fail("Stage-20 GC/checksum/registry summary mismatch")
print(f"[SMT20-QEMU][OK] thread {thread_id} retained its live context across 6 AP handoffs, timer preemption, GC scan and retirement")
PY
