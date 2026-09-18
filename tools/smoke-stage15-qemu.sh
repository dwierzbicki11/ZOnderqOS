#!/usr/bin/env bash
set -euo pipefail

# Stage 14 owns the complete Stage 1..14 boot/runtime regression and 30-second
# stability gate. Stage 15 reuses it and additionally proves that every AP's
# registered idle-thread stack is scanned from the RSP captured by OrionGC.
CHECK_LOG=0
if [[ "${1:-}" == --check-log ]]; then
  CHECK_LOG=1
  shift
fi

ISO="${1:-output-x64/ZonderqOS.iso}"
LOG="${2:-stage15-qemu-serial.log}"
CPUS="${STAGE15_QEMU_CPUS:-${STAGE14_QEMU_CPUS:-8}}"

export STAGE14_QEMU_CPU_MODEL="${STAGE15_QEMU_CPU_MODEL:-${STAGE14_QEMU_CPU_MODEL:-Nehalem}}"
export STAGE14_QEMU_SOCKETS="${STAGE15_QEMU_SOCKETS:-${STAGE14_QEMU_SOCKETS:-1}}"
export STAGE14_QEMU_CPUS="$CPUS"
export STAGE14_QEMU_CORES="${STAGE15_QEMU_CORES:-${STAGE14_QEMU_CORES:-4}}"
export STAGE14_QEMU_THREADS="${STAGE15_QEMU_THREADS:-${STAGE14_QEMU_THREADS:-2}}"
export STAGE14_QEMU_TIMEOUT_SECONDS="${STAGE15_QEMU_TIMEOUT_SECONDS:-${STAGE14_QEMU_TIMEOUT_SECONDS:-90}}"
export STAGE14_QEMU_STABLE_SECONDS="${STAGE15_QEMU_STABLE_SECONDS:-${STAGE14_QEMU_STABLE_SECONDS:-30}}"

fail() {
  echo "[SMT15-QEMU][FAIL] $*" >&2
  exit 1
}

[[ "$CPUS" =~ ^[1-9][0-9]{0,3}$ ]] || fail "CPUS must be an integer in 1..9999 (got: $CPUS)"
(( CPUS <= 256 )) || fail 'Stage 15 supports at most 256 logical CPUs'
command -v python3 >/dev/null || fail 'python3 not found'

check_stage15_serial() {
  python3 - "$1" "$CPUS" <<'PY'
import pathlib
import re
import sys

path, expected = pathlib.Path(sys.argv[1]), int(sys.argv[2])
lines = [line.rstrip("\r") for line in path.read_text(errors="replace").split("\n")[:-1]]
expected_aps = max(0, expected - 1)

def fail(message):
    print("[SMT15-QEMU][FAIL] " + message, file=sys.stderr)
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
        r"bytes=(\d+) word-xor=0x([0-9a-fA-F]+)",
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
    r"\[SCHED-AP-STACK\] cpu=(\d+) canary=0x([0-9a-fA-F]+) "
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
    cpu, canary = int(match.group(1)), int(match.group(2), 16)
    if cpu not in range(1, expected) or cpu in ap_results:
        fail("out-of-range or duplicate AP idle-stack result: " + line)
    expected_canary = 0x51A6C0DEF00D0000 ^ cpu
    if canary != expected_canary:
        fail("AP idle-stack canary is not the deterministic expected value: " + line)
    ap_results[cpu] = canary

if expected == 1:
    if bsp_only != 1 or ap_results:
        fail("single-CPU Stage-15 evidence is inconsistent")
else:
    if bsp_only != 0 or set(ap_results) != set(range(1, expected)):
        raise SystemExit(2)

collection, scanned, scanned_bytes = map(int, summary.group(1, 2, 3))
word_xor = int(summary.group(4), 16)
expected_xor = 0
for canary in ap_results.values():
    expected_xor ^= canary
if collection <= 0:
    fail("invalid OrionGC collection index")
if scanned != expected_aps or scanned_bytes != expected_aps * 8:
    fail(
        f"remote stack scan summary is {scanned} stack(s)/{scanned_bytes} byte(s), "
        f"expected {expected_aps}/{expected_aps * 8}"
    )
if word_xor != expected_xor:
    fail("GC scan fingerprint does not equal the XOR of AP stack canaries")

print(
    f"[SMT15-QEMU] Serial proof: {expected_aps} AP idle stack(s) enrolled with "
    "explicit bounds; OrionGC scanned each captured remote RSP and matched "
    "the on-stack canary fingerprint before AP return to native HLT"
)
PY
}

if (( CHECK_LOG )); then
  set +e
  bash tools/smoke-stage14-qemu.sh --check-log "$ISO" "$LOG"
  stage14_status=$?
  set -e
  (( stage14_status == 0 )) || exit "$stage14_status"
  check_stage15_serial "$LOG"
  exit $?
fi

bash tools/smoke-stage14-qemu.sh "$ISO" "$LOG"
check_stage15_serial "$LOG"
echo '[SMT15-QEMU][OK] Stage 14 regression and Stage 15 AP idle-stack enrollment/GC scan survived the stability interval.'
