#!/usr/bin/env bash
set -euo pipefail

# Stage 14 owns the complete Stage 1..14 boot/runtime regression and 30-second
# stability gate. Stage 18 reuses it, requires the Stage-15 stack and Stage-16
# dispatch proofs, then validates timer preemption and saved-context resume on
# every AP.
CHECK_LOG=0
if [[ "${1:-}" == --check-log ]]; then
  CHECK_LOG=1
  shift
fi

ISO="${1:-output-x64/ZonderqOS.iso}"
LOG="${2:-stage18-qemu-serial.log}"
CPUS="${STAGE18_QEMU_CPUS:-${STAGE14_QEMU_CPUS:-8}}"

export STAGE14_QEMU_CPU_MODEL="${STAGE18_QEMU_CPU_MODEL:-${STAGE14_QEMU_CPU_MODEL:-Nehalem}}"
export STAGE14_QEMU_SOCKETS="${STAGE18_QEMU_SOCKETS:-${STAGE14_QEMU_SOCKETS:-1}}"
export STAGE14_QEMU_CPUS="$CPUS"
export STAGE14_QEMU_CORES="${STAGE18_QEMU_CORES:-${STAGE14_QEMU_CORES:-4}}"
export STAGE14_QEMU_THREADS="${STAGE18_QEMU_THREADS:-${STAGE14_QEMU_THREADS:-2}}"
export STAGE14_QEMU_TIMEOUT_SECONDS="${STAGE18_QEMU_TIMEOUT_SECONDS:-${STAGE14_QEMU_TIMEOUT_SECONDS:-90}}"
export STAGE14_QEMU_STABLE_SECONDS="${STAGE18_QEMU_STABLE_SECONDS:-${STAGE14_QEMU_STABLE_SECONDS:-30}}"

fail() {
  echo "[SMT18-QEMU][FAIL] $*" >&2
  exit 1
}

[[ "$CPUS" =~ ^[1-9][0-9]{0,3}$ ]] || fail "CPUS must be an integer in 1..9999 (got: $CPUS)"
(( CPUS <= 256 )) || fail 'Stage 18 supports at most 256 logical CPUs'
command -v python3 >/dev/null || fail 'python3 not found'

check_stage18_serial() {
  python3 - "$1" "$CPUS" <<'PY'
import pathlib
import re
import sys

path, expected = pathlib.Path(sys.argv[1]), int(sys.argv[2])
lines = [line.rstrip("\r") for line in path.read_text(errors="replace").split("\n")[:-1]]
expected_aps = max(0, expected - 1)

def fail(message):
    print("[SMT18-QEMU][FAIL] " + message, file=sys.stderr)
    raise SystemExit(1)

fatal = re.compile(
    r"CPU\s+EXCEPTION(?!\s+handlers\s+registered)|System halted|\bpanic\b|"
    r"Unhandled Exception|Triple[ -]fault|Invalid RSP|undefined symbol|"
    r"\b(?:page|general protection)[ -]fault\b|\bdeadlock\b",
    re.IGNORECASE,
)
for line in lines:
    if (
        fatal.search(line)
        or re.search(r"\[SCHED\]\s*WARNING:", line, re.IGNORECASE)
        or re.search(r"\[GC\]\s*(?:WARNING|ERROR):", line, re.IGNORECASE)
    ):
        fail("kernel/GC/scheduler failure: " + line)

begins = [i for i, line in enumerate(lines) if line == "[SCHED-AP-STACK] begin"]
dispatches = [i for i, line in enumerate(lines) if line == "[SCHED-AP-STACK] dispatch"]
summaries = [
    (i, match)
    for i, line in enumerate(lines)
    if (match := re.fullmatch(
        r"\[SCHED-AP-STACK\] gc-collection=(\d+) scanned=(\d+) "
        r"bytes=(\d+) start-word-xor=0x([0-9a-fA-F]+) "
        r"word-xor=0x([0-9a-fA-F]+)",
        line,
    ))
]
passes = [i for i, line in enumerate(lines) if line == "[SCHED-AP-STACK-TEST] PASS"]

if any(len(items) > 1 for items in (begins, dispatches, summaries, passes)):
    fail("duplicate Stage-15 evidence (possible reboot)")
if not begins or not dispatches or not summaries or not passes:
    raise SystemExit(2)

begin = begins[0]
dispatch = dispatches[0]
summary_index, summary = summaries[0]
passed = passes[0]
if not (begin < dispatch < summary_index < passed):
    fail("Stage-15 evidence is out of order")

enrollment_re = re.compile(
    r"\[SCHED-AP-STACK\] cpu=(\d+) base=0x([0-9a-fA-F]+) "
    r"top=0x([0-9a-fA-F]+) size=16384 state=enrolled"
)
enrolled = {}
for line in lines[begin + 1 : dispatch]:
    if not line.startswith("[SCHED-AP-STACK] cpu="):
        continue
    match = enrollment_re.fullmatch(line)
    if not match:
        fail("malformed AP idle-stack enrollment: " + line)
    cpu, base, top = int(match.group(1)), int(match.group(2), 16), int(match.group(3), 16)
    if cpu not in range(1, expected) or cpu in enrolled:
        fail("out-of-range or duplicate AP idle-stack enrollment: " + line)
    if top - base != 16 * 1024 or top & 0xF:
        fail("invalid AP idle-stack bounds/alignment: " + line)
    enrolled[cpu] = (base, top)

if set(enrolled) != set(range(1, expected)):
    raise SystemExit(2)
ordered_ranges = sorted(enrolled.values())
for previous, current in zip(ordered_ranges, ordered_ranges[1:]):
    if previous[1] > current[0]:
        fail("AP idle-stack ranges overlap")

segment = lines[dispatch + 1 : summary_index]
stop_re = re.compile(r"\[GC-SMP\] STOP epoch=(\d+)")
resume_re = re.compile(r"\[GC-SMP\] RESUME epoch=(\d+) aps=(\d+)")
stops = [int(m.group(1)) for line in segment if (m := stop_re.fullmatch(line))]
resumes = [
    (int(m.group(1)), int(m.group(2)))
    for line in segment
    if (m := resume_re.fullmatch(line))
]
if expected_aps:
    if len(stops) != 1 or len(resumes) != 1:
        fail("expected exactly one GC stop/resume pair during idle-stack scan")
    if resumes[0] != (stops[0], expected_aps):
        fail("GC resume epoch/AP count does not match the Stage-15 stop")
elif stops or resumes:
    fail("single-CPU run unexpectedly emitted an SMP GC rendezvous")

ap_re = re.compile(
    r"\[SCHED-AP-STACK\] cpu=(\d+) saved-rsp=0x([0-9a-fA-F]+) "
    r"span=(\d+) canary=0x([0-9a-fA-F]+) "
    r"state=gc-scanned-returned-native-idle"
)
ap_results = {}
bsp_only = 0
for line in segment:
    if line == "[SCHED-AP-STACK] cpu=0 state=bsp-only":
        bsp_only += 1
        continue
    if not line.startswith("[SCHED-AP-STACK] cpu="):
        continue
    match = ap_re.fullmatch(line)
    if not match:
        fail("malformed AP idle-stack scan evidence: " + line)
    cpu = int(match.group(1))
    saved_rsp = int(match.group(2), 16)
    span = int(match.group(3))
    canary = int(match.group(4), 16)
    if cpu not in range(1, expected) or cpu in ap_results:
        fail("out-of-range or duplicate AP idle-stack result: " + line)
    base, top = enrolled[cpu]
    if not (base <= saved_rsp < top):
        fail("captured AP RSP is outside its enrolled stack: " + line)
    if span != top - saved_rsp or span < 8 or span > 16 * 1024 or span % 8:
        fail("AP stack scan span does not exactly match captured RSP to stack top: " + line)
    expected_canary = 0x51A6C0DEF00D0000 ^ cpu
    if canary != expected_canary:
        fail("AP idle-stack canary is not the deterministic expected value: " + line)
    ap_results[cpu] = (canary, span)

if expected == 1:
    if bsp_only != 1 or ap_results:
        fail("single-CPU Stage-15 evidence is inconsistent")
else:
    if bsp_only != 0 or set(ap_results) != set(range(1, expected)):
        raise SystemExit(2)

collection, scanned, scanned_bytes = map(int, summary.group(1, 2, 3))
start_word_xor = int(summary.group(4), 16)
word_xor = int(summary.group(5), 16)
expected_xor = 0
expected_bytes = 0
for canary, span in ap_results.values():
    expected_xor ^= canary
    expected_bytes += span
if collection <= 0:
    fail("invalid OrionGC collection index")
if scanned != expected_aps or scanned_bytes != expected_bytes:
    fail(
        f"remote stack scan summary is {scanned} stack(s)/{scanned_bytes} byte(s), "
        f"expected {expected_aps}/{expected_bytes} from the captured RSP ranges"
    )
if start_word_xor != expected_xor:
    fail("GC stack-start fingerprint does not equal the XOR of AP stack canaries")

print(
    f"[SMT18-QEMU] Serial proof: {expected_aps} AP idle stack(s) enrolled with "
    "explicit bounds; OrionGC scanned each exact captured-RSP range and matched "
    "the first live word to the on-stack canary before AP return to native HLT"
)

dispatch_begins = [i for i, line in enumerate(lines) if line == "[SCHED-AP-DISPATCH] begin"]
dispatch_passes = [i for i, line in enumerate(lines) if line == "[SCHED-AP-DISPATCH-TEST] PASS"]
if len(dispatch_begins) > 1 or len(dispatch_passes) > 1:
    fail("duplicate Stage-16 evidence (possible reboot)")
if not dispatch_begins or not dispatch_passes:
    raise SystemExit(2)
dispatch_begin = dispatch_begins[0]
dispatch_pass = dispatch_passes[0]
if not (passed < dispatch_begin < dispatch_pass):
    fail("Stage-16 dispatch evidence is out of order")

dispatch_re = re.compile(
    r"\[SCHED-AP-DISPATCH\] cpu=(\d+) thread=(\d+) "
    r"checksum=0x([0-9a-fA-F]+) state=exited-idle-restored"
)
dispatch_results = {}
thread_ids = set()
for line in lines[dispatch_begin + 1 : dispatch_pass]:
    if not line.startswith("[SCHED-AP-DISPATCH] cpu="):
        continue
    match = dispatch_re.fullmatch(line)
    if not match:
        fail("malformed AP scheduler-dispatch evidence: " + line)
    cpu = int(match.group(1))
    thread_id = int(match.group(2))
    checksum = int(match.group(3), 16)
    if cpu not in range(1, expected) or cpu in dispatch_results:
        fail("out-of-range or duplicate AP scheduler dispatch: " + line)
    if thread_id == 0 or thread_id in thread_ids:
        fail("invalid or duplicate scheduler thread id: " + line)
    expected_checksum = 0x534D503136000000 ^ (cpu << 32) ^ thread_id
    if checksum != expected_checksum:
        fail("AP scheduler-dispatch checksum mismatch: " + line)
    dispatch_results[cpu] = thread_id
    thread_ids.add(thread_id)

if set(dispatch_results) != set(range(1, expected)):
    raise SystemExit(2)

print(
    f"[SMT18-QEMU] Stage-16 regression: {expected_aps} AP worker thread(s) ran on "
    "their assigned CPU and allocated stack, exited, and returned through the "
    "LAPIC timer context-switch path to the enrolled idle thread"
)

preempt_begins = [i for i, line in enumerate(lines) if line == "[SCHED-AP-PREEMPT] begin"]
preempt_passes = [i for i, line in enumerate(lines) if line == "[SCHED-AP-PREEMPT-TEST] PASS"]
if len(preempt_begins) > 1 or len(preempt_passes) > 1:
    fail("duplicate Stage-17 evidence (possible reboot)")
if not preempt_begins or not preempt_passes:
    raise SystemExit(2)
preempt_begin = preempt_begins[0]
preempt_pass = preempt_passes[0]
if not (dispatch_pass < preempt_begin < preempt_pass):
    fail("Stage-17 preemption evidence is out of order")

preempt_re = re.compile(
    r"\[SCHED-AP-PREEMPT\] cpu=(\d+) thread=(\d+) "
    r"checksum=0x([0-9a-fA-F]+) "
    r"state=preempted-resumed-exited-idle-restored"
)
preempt_results = {}
preempt_thread_ids = set()
for line in lines[preempt_begin + 1 : preempt_pass]:
    if not line.startswith("[SCHED-AP-PREEMPT] cpu="):
        continue
    match = preempt_re.fullmatch(line)
    if not match:
        fail("malformed AP timer-preemption evidence: " + line)
    cpu = int(match.group(1))
    thread_id = int(match.group(2))
    checksum = int(match.group(3), 16)
    if cpu not in range(1, expected) or cpu in preempt_results:
        fail("out-of-range or duplicate AP timer preemption: " + line)
    if thread_id == 0 or thread_id in preempt_thread_ids or thread_id in thread_ids:
        fail("invalid or reused Stage-17 scheduler thread id: " + line)
    expected_checksum = 0x534D503137000000 ^ (cpu << 32) ^ thread_id
    if checksum != expected_checksum:
        fail("AP timer-preemption checksum mismatch: " + line)
    preempt_results[cpu] = thread_id
    preempt_thread_ids.add(thread_id)

if set(preempt_results) != set(range(1, expected)):
    raise SystemExit(2)

print(
    f"[SMT18-QEMU] Stage-17 regression: {expected_aps} AP worker(s) passed timer preemption/resume"
)

concurrent_begins = [i for i, line in enumerate(lines) if line == "[SCHED-AP-CONCURRENT] begin"]
concurrent_passes = [i for i, line in enumerate(lines) if line == "[SCHED-AP-CONCURRENT-TEST] PASS"]
if len(concurrent_begins) != 1 or len(concurrent_passes) != 1:
    raise SystemExit(2)
cb, cp = concurrent_begins[0], concurrent_passes[0]
if not (preempt_pass < cb < cp):
    fail("Stage-18 concurrent evidence is out of order")
worker_re = re.compile(r"\[SCHED-AP-CONCURRENT\] cpu=(\d+) worker=([01]) thread=(\d+) preemptions=(\d+) checksum=0x([0-9a-fA-F]+) state=concurrent-preempted-exited-idle-restored")
summary_re = re.compile(r"\[SCHED-AP-CONCURRENT\] gc-collection=(\d+) scanned-aps=(\d+) workers=(\d+) registry=restored")
workers, tids, summaries = {}, set(), []
for line in lines[cb + 1:cp]:
    m = worker_re.fullmatch(line)
    if m:
        cpu, slot, tid, preemptions = map(int, m.group(1,2,3,4))
        checksum = int(m.group(5), 16)
        key = (cpu, slot)
        if cpu not in range(1, expected) or key in workers or tid == 0 or tid in tids or preemptions < 2:
            fail("invalid Stage-18 worker: " + line)
        if checksum != (0x534D503138000000 ^ (cpu << 32) ^ (slot << 24) ^ tid):
            fail("Stage-18 checksum mismatch: " + line)
        workers[key] = tid
        tids.add(tid)
    m = summary_re.fullmatch(line)
    if m:
        summaries.append(tuple(map(int, m.groups())))
expected_keys = {(cpu, slot) for cpu in range(1, expected) for slot in range(2)}
if set(workers) != expected_keys or len(summaries) != 1:
    raise SystemExit(2)
collection, scanned, worker_count = summaries[0]
if collection <= 0 or scanned != expected_aps or worker_count != expected_aps * 2:
    fail("Stage-18 GC/registry summary mismatch")
print(f"[SMT18-QEMU] Concurrent proof: {worker_count} workers on {expected_aps} AP queues survived repeated timer preemption and OrionGC STW")
PY
}

if (( CHECK_LOG )); then
  set +e
  bash tools/smoke-stage14-qemu.sh --check-log "$ISO" "$LOG"
  stage14_status=$?
  set -e
  (( stage14_status == 0 )) || exit "$stage14_status"
  check_stage18_serial "$LOG"
  exit $?
fi

export SMT_QEMU_EXCEPTION_TRACE="${LOG}.qemu-exceptions.log"
bash tools/smoke-stage14-qemu.sh "$ISO" "$LOG"
check_stage18_serial "$LOG"
echo '[SMT18-QEMU][OK] Stage 14-17 regressions and Stage 18 concurrent AP scheduling survived the stability interval.'

