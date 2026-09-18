#!/usr/bin/env bash
set -euo pipefail

# Stage 12 already owns the full boot, serial, and 30-second stability gate.
# Stage 13 reuses that gate and adds the scheduler-state evidence that must be
# present before the QEMU run is considered complete.
CHECK_LOG=0
if [[ "${1:-}" == --check-log ]]; then
  CHECK_LOG=1
  shift
fi

ISO="${1:-output-x64/ZonderqOS.iso}"
LOG="${2:-stage13-qemu-serial.log}"
CPUS="${STAGE13_QEMU_CPUS:-${STAGE12_QEMU_CPUS:-8}}"

# Keep the Stage-12 runner's topology and stability gate aligned with the
# Stage-13 invocation, including the 1-vCPU regression matrix entry.
export STAGE12_QEMU_CPU_MODEL="${STAGE13_QEMU_CPU_MODEL:-${STAGE12_QEMU_CPU_MODEL:-Nehalem}}"
export STAGE12_QEMU_SOCKETS="${STAGE13_QEMU_SOCKETS:-${STAGE12_QEMU_SOCKETS:-1}}"
export STAGE12_QEMU_CPUS="$CPUS"
export STAGE12_QEMU_CORES="${STAGE13_QEMU_CORES:-${STAGE12_QEMU_CORES:-4}}"
export STAGE12_QEMU_THREADS="${STAGE13_QEMU_THREADS:-${STAGE12_QEMU_THREADS:-2}}"
export STAGE12_QEMU_TIMEOUT_SECONDS="${STAGE13_QEMU_TIMEOUT_SECONDS:-${STAGE12_QEMU_TIMEOUT_SECONDS:-90}}"
export STAGE12_QEMU_STABLE_SECONDS="${STAGE13_QEMU_STABLE_SECONDS:-${STAGE12_QEMU_STABLE_SECONDS:-30}}"

fail() {
  echo "[SMT13-QEMU][FAIL] $*" >&2
  exit 1
}

[[ "$CPUS" =~ ^[1-9][0-9]{0,3}$ ]] || fail "CPUS must be an integer in 1..9999 (got: $CPUS)"
(( CPUS <= 256 )) || fail 'Stage 13 supports at most 256 logical CPUs'
command -v python3 >/dev/null || fail 'python3 not found'

check_stage13_serial() {
  python3 - "$1" "$CPUS" <<'PY'
import pathlib
import re
import sys

path, expected = pathlib.Path(sys.argv[1]), int(sys.argv[2])
lines = [line.rstrip("\r") for line in path.read_text(errors="replace").split("\n")[:-1]]

def fail(message):
    print("[SMT13-QEMU][FAIL] " + message, file=sys.stderr)
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

bsp_re = re.compile(r"\[SCHED-PERCPU\] cpu=0 phase=2 entries=1 state=bsp-idle")
ap_re = re.compile(
    r"\[SCHED-PERCPU\] cpu=(\d+) phase=(\d+) entries=(\d+) "
    r"state=returned-native-idle"
)
cpus = {}
for line in lines:
    if line.startswith("[SCHED-PERCPU] cpu=0"):
        if not bsp_re.fullmatch(line):
            fail("malformed BSP per-CPU scheduler evidence: " + line)
        if 0 in cpus:
            fail("duplicate BSP per-CPU scheduler evidence")
        cpus[0] = (0, 0, "bsp-idle")
    elif line.startswith("[SCHED-PERCPU] cpu="):
        match = ap_re.fullmatch(line)
        if not match:
            fail("malformed AP per-CPU scheduler evidence: " + line)
        cpu, phase, entries = map(int, match.groups())
        if cpu not in range(1, expected) or cpu in cpus:
            fail("out-of-range or duplicate AP per-CPU evidence: " + line)
        if phase != 2 or entries != 1:
            fail("AP did not complete exactly one enter/leave transition: " + line)
        cpus[cpu] = (phase, entries, "returned-native-idle")

pass_count = lines.count("[SCHED-PERCPU-TEST] PASS")
if pass_count > 1:
    fail("duplicate Stage-13 PASS marker (possible reboot)")
if pass_count != 1:
    raise SystemExit(2)
if set(cpus) != set(range(expected)):
    raise SystemExit(2)

print(
    f"[SMT13-QEMU] Serial proof: {expected} scheduler CPU state(s), "
    f"{max(0, expected - 1)} AP enter/leave handshake(s), all returned to native HLT"
)
PY
}

if (( CHECK_LOG )); then
  set +e
  bash tools/smoke-stage12-qemu.sh --check-log "$ISO" "$LOG"
  stage12_status=$?
  set -e
  (( stage12_status == 0 )) || exit "$stage12_status"
  check_stage13_serial "$LOG"
  exit $?
fi

bash tools/smoke-stage12-qemu.sh "$ISO" "$LOG"
check_stage13_serial "$LOG"
echo '[SMT13-QEMU][OK] Stage 12 regression and Stage 13 per-CPU scheduler foundation survived the stability interval.'
