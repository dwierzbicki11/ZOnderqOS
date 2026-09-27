#!/usr/bin/env bash
set -euo pipefail

# Stage 13 owns the complete Stage 1..13 boot/runtime regression and 30-second
# stability gate. Stage 14 reuses it and additionally proves that every AP was
# stopped and resumed by a real OrionGC collection while executing managed code.
CHECK_LOG=0
if [[ "${1:-}" == --check-log ]]; then
  CHECK_LOG=1
  shift
fi

ISO="${1:-output-x64/ZonderqOS.iso}"
LOG="${2:-stage14-qemu-serial.log}"
CPUS="${STAGE14_QEMU_CPUS:-${STAGE13_QEMU_CPUS:-8}}"

export STAGE13_QEMU_CPU_MODEL="${STAGE14_QEMU_CPU_MODEL:-${STAGE13_QEMU_CPU_MODEL:-Nehalem}}"
export STAGE13_QEMU_SOCKETS="${STAGE14_QEMU_SOCKETS:-${STAGE13_QEMU_SOCKETS:-1}}"
export STAGE13_QEMU_CPUS="$CPUS"
export STAGE13_QEMU_CORES="${STAGE14_QEMU_CORES:-${STAGE13_QEMU_CORES:-4}}"
export STAGE13_QEMU_THREADS="${STAGE14_QEMU_THREADS:-${STAGE13_QEMU_THREADS:-2}}"
export STAGE13_QEMU_TIMEOUT_SECONDS="${STAGE14_QEMU_TIMEOUT_SECONDS:-${STAGE13_QEMU_TIMEOUT_SECONDS:-90}}"
export STAGE13_QEMU_STABLE_SECONDS="${STAGE14_QEMU_STABLE_SECONDS:-${STAGE13_QEMU_STABLE_SECONDS:-30}}"

fail() {
  echo "[SMT14-QEMU][FAIL] $*" >&2
  exit 1
}

[[ "$CPUS" =~ ^[1-9][0-9]{0,3}$ ]] || fail "CPUS must be an integer in 1..9999 (got: $CPUS)"
(( CPUS <= 256 )) || fail 'Stage 14 supports at most 256 logical CPUs'
command -v python3 >/dev/null || fail 'python3 not found'

check_stage14_serial() {
  python3 - "$1" "$CPUS" <<'PY'
import pathlib
import re
import sys

path, expected = pathlib.Path(sys.argv[1]), int(sys.argv[2])
lines = [line.rstrip("\r") for line in path.read_text(errors="replace").split("\n")[:-1]]
expected_aps = max(0, expected - 1)

def fail(message):
    print("[SMT14-QEMU][FAIL] " + message, file=sys.stderr)
    raise SystemExit(1)

fatal = re.compile(
    r"CPU\s+EXCEPTION(?!\s+handlers\s+registered)|System halted|\bpanic\b|"
    r"Unhandled Exception|Triple[ -]fault|Invalid RSP|undefined symbol|"
    r"\b(?:page|general protection)[ -]fault\b|\bdeadlock\b",
    re.IGNORECASE,
)
for line in lines:
    if fatal.search(line) or re.search(r"\[SCHED\]\s*WARNING:", line, re.IGNORECASE):
        fail("kernel failure or scheduler warning: " + line)

dispatch = [i for i, line in enumerate(lines) if line == "[SCHED-AP-RUNTIME] dispatch"]
resident = [
    (i, int(match.group(1)))
    for i, line in enumerate(lines)
    if (match := re.fullmatch(r"\[SCHED-AP-RUNTIME\] resident aps=(\d+)", line))
]
collection = [
    (i, int(match.group(1)))
    for i, line in enumerate(lines)
    if (match := re.fullmatch(r"\[SCHED-AP-RUNTIME\] gc-collection=(\d+)", line))
]
passes = [i for i, line in enumerate(lines) if line == "[SCHED-AP-RUNTIME-TEST] PASS"]

if any(len(items) > 1 for items in (dispatch, resident, collection, passes)):
    fail("duplicate Stage-14 evidence (possible reboot)")
if not dispatch or not resident or not collection or not passes:
    raise SystemExit(2)
if resident[0][1] != expected_aps:
    fail(f"managed resident count {resident[0][1]} != expected {expected_aps}")
if collection[0][1] <= 0:
    fail("invalid OrionGC collection index")
if not (dispatch[0] < resident[0][0] < collection[0][0] < passes[0]):
    fail("Stage-14 evidence is out of order")

segment = lines[resident[0][0] + 1 : collection[0][0]]
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
        fail("expected exactly one GC stop/resume pair inside managed AP residency")
    if resumes[0] != (stops[0], expected_aps):
        fail("GC resume epoch/AP count does not match the Stage-14 stop")
elif stops or resumes:
    fail("single-CPU run unexpectedly emitted an SMP GC rendezvous")

ap_re = re.compile(
    r"\[SCHED-AP-RUNTIME\] cpu=(\d+) heartbeats=(\d+) "
    r"checksum=0x([0-9a-fA-F]+) state=returned-native-idle"
)
ap_results = {}
bsp_only = 0
for line in lines[collection[0][0] + 1 : passes[0]]:
    if line == "[SCHED-AP-RUNTIME] cpu=0 state=bsp-only":
        bsp_only += 1
        continue
    if not line.startswith("[SCHED-AP-RUNTIME] cpu="):
        continue
    match = ap_re.fullmatch(line)
    if not match:
        fail("malformed managed AP residency evidence: " + line)
    cpu, heartbeat = int(match.group(1)), int(match.group(2))
    if cpu not in range(1, expected) or cpu in ap_results:
        fail("out-of-range or duplicate managed AP residency result: " + line)
    if heartbeat <= 0 or int(match.group(3), 16) == 0:
        fail("managed AP did not publish live progress/checksum: " + line)
    ap_results[cpu] = heartbeat

if expected == 1:
    if bsp_only != 1 or ap_results:
        fail("single-CPU Stage-14 evidence is inconsistent")
else:
    if bsp_only != 0 or set(ap_results) != set(range(1, expected)):
        raise SystemExit(2)

print(
    f"[SMT14-QEMU] Serial proof: {expected_aps} AP(s) remained in managed code, "
    "were stopped/resumed by real OrionGC, advanced heartbeat after resume, "
    "and returned to native HLT"
)
PY
}

if (( CHECK_LOG )); then
  set +e
  bash tools/smoke-stage13-qemu.sh --check-log "$ISO" "$LOG"
  stage13_status=$?
  set -e
  (( stage13_status == 0 )) || exit "$stage13_status"
  check_stage14_serial "$LOG"
  exit $?
fi

bash tools/smoke-stage13-qemu.sh "$ISO" "$LOG"
check_stage14_serial "$LOG"
echo '[SMT14-QEMU][OK] Stage 13 regression and Stage 14 managed AP/OrionGC residency survived the stability interval.'
