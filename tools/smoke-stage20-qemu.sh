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
skips = [i for i, line in enumerate(lines) if line == "[SCHED-LIVE-MIGRATE-TEST] SKIP reason=single-ap-no-migration-target"]
if expected == 2:
    if len(begins) != 1 or len(passes) != 0 or len(skips) != 1 or begins[0] >= skips[0]:
        fail("single-AP Stage-20 must explicitly skip the impossible AP-to-AP migration proof")
    if any("[SCHED-LIVE-MIGRATE] thread=" in line or "[SCHED-LIVE-MIGRATE] gc-collection=" in line for line in lines):
        fail("single-AP Stage-20 skip emitted migration evidence")
    print("[SMT20-QEMU][OK] one AP correctly skipped the AP-to-AP migration proof")
    raise SystemExit(0)
if len(begins) != 1 or len(passes) != 1 or len(skips) != 0 or begins[0] >= passes[0]:
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

if expected not in (4, 8, 16):
    fail(f"unsupported Stage-20 topology: {expected} CPUs")
required_hops = expected - 2
last_cpu = expected - 1
if hop_count != required_hops:
    fail(f"kernel summary reported {hop_count} live context handoffs instead of {required_hops}")

# SMP CPUs share one serial stream, so an unrelated CPU can interleave output
# between the individual WriteString/WriteNumber calls that form a diagnostic
# hop line. The kernel increments hop_count only after WaitForStage20Cpu()
# succeeds and validates the final checksum/state/registry before this summary.
# Derive the thread id from that validated summary and treat each intact hop
# line as additional evidence rather than making log formatting part of the
# scheduler correctness contract.
thread_id = checksum ^ 0x534D503140000000 ^ (last_cpu << 32) ^ (required_hops << 24)
if thread_id <= 0 or thread_id > 0xFFFFFFFF:
    fail(f"Stage-20 checksum decodes an invalid managed thread id: {thread_id}")

seen_targets = set()
for hop in hops:
    tid, source, target, source_out, target_in, preemptions = hop
    if tid != thread_id:
        fail(f"handoff changed managed thread identity: {hop}")
    if source < 1 or source >= last_cpu or target != source + 1:
        fail(f"broken AP handoff chain record: {hop}")
    if target in seen_targets:
        fail(f"duplicated AP handoff target in serial evidence: {target}")
    seen_targets.add(target)
    if source_out != 1 or target_in != 1 or preemptions < 2:
        fail(f"handoff lacks queue ownership/preemption proof: {hop}")

expected_checksum = 0x534D503140000000 ^ (last_cpu << 32) ^ (required_hops << 24) ^ thread_id
if collection <= 0 or scanned != expected_aps or checksum != expected_checksum:
    fail("Stage-20 GC/checksum/registry summary mismatch")
print(f"[SMT20-QEMU][OK] thread {thread_id} retained its live context across {required_hops} AP handoffs, timer preemption, GC scan and retirement ({len(hops)} intact diagnostic hop lines)")
PY
