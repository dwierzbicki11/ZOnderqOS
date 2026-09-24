#!/usr/bin/env bash
set -euo pipefail

# --check-log validates a captured serial log without claiming a QEMU run or
# stability interval. Exit 2 means required evidence has not arrived yet.
CHECK_LOG=0
if [[ "${1:-}" == --check-log ]]; then
  CHECK_LOG=1
  shift
fi
ISO="${1:-output-x64/ZonderqOS.iso}"
LOG="${2:-stage12-qemu-serial.log}"
CPU_MODEL="${STAGE12_QEMU_CPU_MODEL:-Nehalem}"
SOCKETS="${STAGE12_QEMU_SOCKETS:-1}"
CPUS="${STAGE12_QEMU_CPUS:-8}"
CORES="${STAGE12_QEMU_CORES:-4}"
THREADS="${STAGE12_QEMU_THREADS:-2}"
TIMEOUT="${STAGE12_QEMU_TIMEOUT_SECONDS:-90}"
STABLE_SECONDS="${STAGE12_QEMU_STABLE_SECONDS:-30}"

fail() {
  echo "[SMT12-QEMU][FAIL] $*" >&2
  exit 1
}

for value_name in SOCKETS CPUS CORES THREADS TIMEOUT STABLE_SECONDS; do
  value="${!value_name}"
  [[ "$value" =~ ^[1-9][0-9]{0,3}$ ]] || fail "$value_name must be an integer in 1..9999 (got: $value)"
done
(( CPUS <= 256 )) || fail 'Stage 12 supports at most 256 logical CPUs'
(( STABLE_SECONDS >= 30 )) || fail 'Stage 12 requires at least 30 seconds after managed AP proof and first timer tick'
(( CPUS == SOCKETS * CORES * THREADS )) || fail 'invalid topology: cpus must equal sockets*cores*threads'
[[ "$CPU_MODEL" =~ ^[A-Za-z0-9][A-Za-z0-9_.-]*$ ]] || fail 'invalid CPU model name'
command -v python3 >/dev/null || fail 'python3 not found'

check_serial() {
  python3 - "$1" "$CPUS" <<'PY'
import pathlib
import re
import sys

path, expected = pathlib.Path(sys.argv[1]), int(sys.argv[2])

def fail(message):
    print("[SMT12-QEMU][FAIL] " + message, file=sys.stderr)
    raise SystemExit(1)

# Ignore an unfinished serial line while QEMU is still writing it.
lines = [line.rstrip("\r") for line in path.read_text(errors="replace").split("\n")[:-1]]
fatal = re.compile(
    r"CPU\s+EXCEPTION(?!\s+handlers\s+registered)|System halted|\bpanic\b|"
    r"Unhandled Exception|Triple[ -]fault|Invalid RSP|"
    r"\b(?:page|general protection)[ -]fault\b|undefined symbol|"
    r"\bwatchdog\b.*\btimeout\b|\bAP(?: startup)? timeout\b|\bdeadlock\b",
    re.IGNORECASE,
)
for line in lines:
    if fatal.search(line) or re.search(r"\[SMP\]\s*WARNING:", line, re.IGNORECASE):
        fail("kernel failure or SMP warning: " + line)

mapping = {}
lapics = set()
ready = {}
stacks = set()
ipi = {}
run = {}
idle = {}
timer = {}
gc_rdv = {}
gc_stops = []
gc_resumes = []
gc_integration = {}
managed = {}
managed_checksums = set()
map_re = re.compile(r"\[SMP\] CpuId\[(\d+)\] LAPIC=(\d+) processor=(\d+)( BSP)?")
ready_re = re.compile(r"\[SMP\] AP READY: CpuId=(\d+) LAPIC=(\d+) GS=(\d+) stack=0x([0-9a-fA-F]+)")
ipi_re = re.compile(r"\[APIC\] CPU (\d+) fixed IPI ack count=(\d+)")
run_re = re.compile(r"\[APIC-RUN\] CPU (\d+) repeated fixed IPI total count=(\d+)")
idle_re = re.compile(r"\[SMP-IDLE\] cpu=(\d+) parked-command counter=(\d+) ipi=(\d+)")
timer_re = re.compile(r"\[SMP-TIMER\] cpu=(\d+) ticks=(\d+) ipi=(\d+)")
gc_rdv_re = re.compile(r"\[GC-RDV\] cpu=(\d+) ack=(\d+) resume=(\d+) state=(\d+) rsp=0x([0-9a-fA-F]+) rip=0x([0-9a-fA-F]+)")
gc_stop_re = re.compile(r"\[GC-SMP\] STOP epoch=(\d+)")
gc_resume_re = re.compile(r"\[GC-SMP\] RESUME epoch=(\d+) aps=(\d+)")
gc_integration_re = re.compile(r"\[GC-SMP-INTEGRATION\] round=(\d+) collection=(\d+) freed=(\d+)")
managed_re = re.compile(r"\[MANAGED-AP\] cpu=(\d+) checksum=0x([0-9a-fA-F]+) state=returned-native-idle")
for line in lines:
    if line.startswith("[SMP] CpuId["):
        match = map_re.fullmatch(line)
        if not match:
            fail("malformed dense CPU map entry: " + line)
        cpu, lapic = int(match[1]), int(match[2])
        if cpu >= expected or cpu in mapping or lapic in lapics:
            fail("out-of-range or duplicate CpuId/LAPIC in dense map: " + line)
        if bool(match[4]) != (cpu == 0):
            fail("exactly CpuId 0 must be the BSP: " + line)
        mapping[cpu] = lapic
        lapics.add(lapic)
    if line.startswith("[SMP] AP READY:"):
        match = ready_re.fullmatch(line)
        if not match:
            fail("malformed AP READY entry: " + line)
        cpu, lapic, gs, stack = int(match[1]), int(match[2]), int(match[3]), int(match[4], 16)
        if cpu not in range(1, expected) or cpu in ready:
            fail("out-of-range or duplicate AP CpuId: " + line)
        if gs != cpu:
            fail("AP GS-local CpuId does not match its dense CpuId: " + line)
        if stack == 0 or stack > 0xffffffffffffffff or stack % 16 != 0 or stack in stacks:
            fail("AP stack must be nonzero, unique and 16-byte aligned: " + line)
        ready[cpu] = lapic
        stacks.add(stack)
    if line.startswith("[APIC] CPU "):
        match = ipi_re.fullmatch(line)
        if not match:
            fail("malformed fixed IPI result: " + line)
        cpu, count = int(match[1]), int(match[2])
        if cpu not in range(1, expected) or cpu in ipi:
            fail("out-of-range or duplicate fixed IPI CpuId: " + line)
        if count != 1:
            fail("fixed IPI acknowledgement count is not exactly one: " + line)
        ipi[cpu] = count
    if line.startswith("[APIC-RUN] CPU "):
        match = run_re.fullmatch(line)
        if not match:
            fail("malformed repeated fixed IPI result: " + line)
        cpu, count = int(match[1]), int(match[2])
        if cpu not in range(1, expected) or cpu in run:
            fail("out-of-range or duplicate repeated fixed IPI CpuId: " + line)
        if count != 4:
            fail("repeated fixed IPI total count is not exactly four: " + line)
        run[cpu] = count
    if line.startswith("[SMP-IDLE] cpu="):
        match = idle_re.fullmatch(line)
        if not match:
            fail("malformed parked AP command result: " + line)
        cpu, counter, ipi_count = int(match[1]), int(match[2]), int(match[3])
        if cpu not in range(1, expected) or cpu in idle:
            fail("out-of-range or duplicate parked AP command CpuId: " + line)
        if counter != 100001 or ipi_count != 5:
            fail("parked AP command did not produce the exact expected state: " + line)
        idle[cpu] = (counter, ipi_count)
    if line.startswith("[SMP-TIMER] cpu="):
        match = timer_re.fullmatch(line)
        if not match:
            fail("malformed AP-local timer result: " + line)
        cpu, timer_ticks, ipi_count = int(match[1]), int(match[2]), int(match[3])
        if cpu not in range(1, expected) or cpu in timer:
            fail("out-of-range or duplicate AP-local timer CpuId: " + line)
        if timer_ticks < 3 or ipi_count != 7:
            fail("AP-local timer did not reach the required tick/IPI state: " + line)
        timer[cpu] = (timer_ticks, ipi_count)
    if line.startswith("[GC-RDV] cpu="):
        match = gc_rdv_re.fullmatch(line)
        if not match:
            fail("malformed GC rendezvous result: " + line)
        cpu, ack, resume, state = int(match[1]), int(match[2]), int(match[3]), int(match[4])
        saved_rsp, saved_rip = int(match[5], 16), int(match[6], 16)
        if cpu not in range(1, expected) or cpu in gc_rdv:
            fail("out-of-range or duplicate GC rendezvous CpuId: " + line)
        if ack != 3 or resume != 3 or state != 0 or saved_rsp == 0 or saved_rip == 0:
            fail("GC rendezvous did not finish all epochs cleanly: " + line)
        gc_rdv[cpu] = (ack, resume, state, saved_rsp, saved_rip)
    if line.startswith("[GC-SMP] STOP epoch="):
        match = gc_stop_re.fullmatch(line)
        if not match:
            fail("malformed real GC STOP marker: " + line)
        gc_stops.append(int(match[1]))
    if line.startswith("[GC-SMP] RESUME epoch="):
        match = gc_resume_re.fullmatch(line)
        if not match:
            fail("malformed real GC RESUME marker: " + line)
        epoch, aps = int(match[1]), int(match[2])
        if expected > 1 and aps != expected - 1:
            fail("real GC resumed wrong AP count: " + line)
        gc_resumes.append(epoch)
    if line.startswith("[GC-SMP-INTEGRATION] round="):
        match = gc_integration_re.fullmatch(line)
        if not match:
            fail("malformed real GC integration result: " + line)
        round_id, collection, freed = int(match[1]), int(match[2]), int(match[3])
        if round_id not in (1, 2, 3) or round_id in gc_integration:
            fail("invalid or duplicate real GC integration round: " + line)
        gc_integration[round_id] = (collection, freed)
    if line.startswith("[MANAGED-AP] cpu="):
        match = managed_re.fullmatch(line)
        if not match:
            fail("malformed managed AP result: " + line)
        cpu, checksum = int(match[1]), int(match[2], 16)
        if cpu not in range(1, expected) or cpu in managed:
            fail("out-of-range or duplicate managed AP CpuId: " + line)
        if checksum == 0 or checksum in managed_checksums:
            fail("managed AP checksum must be nonzero and unique: " + line)
        managed[cpu] = checksum
        managed_checksums.add(checksum)
    if line.startswith("[SMP] Dense CPU map:") and line != f"[SMP] Dense CPU map: {expected} logical CPU(s)":
        fail("kernel CPU count does not match QEMU topology: " + line)
    if line.startswith("[SMP] Stage 4 complete:") and line != (
        f"[SMP] Stage 4 complete: {expected - 1} AP(s) ready; AP scheduler/interrupts remain disabled."
    ):
        fail("Stage 4 AP count or state does not match expected topology: " + line)
    if line.startswith("[SMP] Native work proof complete:") and line != (
        f"[SMP] Native work proof complete: {expected - 1} AP worker(s); APs are now parked awaiting native fixed IPI."
    ):
        fail("native AP worker count or parked state does not match expected topology: " + line)

for cpu, lapic in ready.items():
    if cpu in mapping and mapping[cpu] != lapic:
        fail(f"AP CpuId {cpu} LAPIC does not match the dense map")

required = [
    f"[SMP] Dense CPU map: {expected} logical CPU(s)",
    "[SMP] Dense CPU map verified: BSP=0, CpuIds are unique and contiguous.",
    "[SMP] Stage 2 complete: dense CPU map ready; APs remain parked.",
    "[SMP] Stage 3 complete: BSP CpuId=0, GS per-CPU storage active; APs remain parked.",
    f"[SMP] Stage 4 complete: {expected - 1} AP(s) ready; AP scheduler/interrupts remain disabled.",
    "[SMP-BOOT] PASS",
    f"[SMP] Native work proof complete: {expected - 1} AP worker(s); APs are now parked awaiting native fixed IPI.",
    "[APIC-TEST] PASS",
    "[APIC-RUN-TEST] PASS",
    "[SMP-IDLE-TEST] PASS",
    "[SMP-TIMER-TEST] PASS",
    "[GC-RDV] epochs=3 all APs stopped and resumed cleanly.",
    "[GC-RDV-TEST] PASS",
    "[GC-SMP-INTEGRATION] survivor=OK collections=3",
    "[GC-SMP-INTEGRATION-TEST] PASS",
    "[MANAGED-AP-TEST] PASS",
]
for marker in required:
    if lines.count(marker) > 1:
        fail("duplicate bootstrap marker (possible reboot): " + marker)

# Serial output is written by interrupt and managed-runtime paths without a
# shared line lock.  A timer message can therefore begin after another
# subsystem's partial line (observed on the 1-vCPU CI run).  Match the complete
# LAPIC marker anywhere on the physical serial line while retaining the strict
# numeric and monotonicity checks below.
ticks = [int(match[1]) for line in lines
         if (match := re.search(r"\[LAPIC\] Timer tick ([1-9]\d*)\b", line))]
if any(current <= previous for previous, current in zip(ticks, ticks[1:])):
    fail("BSP timer tick count repeated or went backwards (possible reboot)")

collections = [gc_integration[i][0] for i in (1, 2, 3)] if set(gc_integration) == {1, 2, 3} else []
if collections and not (collections[1] == collections[0] + 1 and collections[2] == collections[1] + 1):
    fail("real GC integration collection indexes are not consecutive")
if expected > 1:
    if len(gc_stops) < 3 or len(gc_resumes) < 3:
        raise SystemExit(2)
    for epoch in gc_stops[:3]:
        if epoch not in gc_resumes:
            fail(f"real GC STOP epoch {epoch} has no matching RESUME")

if (any(marker not in lines for marker in required)
        or set(mapping) != set(range(expected))
        or set(ready) != set(range(1, expected))
        or set(ipi) != set(range(1, expected))
        or set(run) != set(range(1, expected))
        or set(idle) != set(range(1, expected))
        or set(timer) != set(range(1, expected))
        or set(gc_rdv) != set(range(1, expected))
        or set(gc_integration) != {1, 2, 3}
        or set(managed) != set(range(1, expected))
        or 1 not in ticks):
    raise SystemExit(2)

if expected == 1 and managed:
    fail("single-CPU regression unexpectedly reported an AP managed result")
print(f"[SMT12-QEMU] Serial proof: {expected} dense CPU(s), {len(ready)} AP(s) READY, Stage-11 real OrionGC preserved, {len(managed)} AP(s) executed managed C# and returned to native HLT; last-tick={ticks[-1]}")
PY
}

if (( CHECK_LOG )); then
  [[ -f "$ISO" ]] || fail "serial log not found: $ISO"
  check_serial "$ISO"
  exit $?
fi

command -v qemu-system-x86_64 >/dev/null || fail 'qemu-system-x86_64 not found'
[[ -f "$ISO" ]] || fail "ISO not found: $ISO"
models="$(qemu-system-x86_64 -cpu help)" || fail 'cannot list QEMU CPU models'
awk -v model="$CPU_MODEL" '$2 == model { found=1 } END { exit !found }' <<< "$models" || \
  fail "QEMU CPU model '$CPU_MODEL' is not available"

: > "$LOG"
QEMU_STDERR="${LOG}.stderr"
: > "$QEMU_STDERR"
QEMU_DEBUG_ARGS=()
if [[ -n "${SMT_QEMU_EXCEPTION_TRACE:-}" ]]; then
  : > "$SMT_QEMU_EXCEPTION_TRACE"
  QEMU_DEBUG_ARGS=(-d int,cpu_reset -D "$SMT_QEMU_EXCEPTION_TRACE")
fi
echo "[SMT12-QEMU] ${CPU_MODEL}: ${SOCKETS}S/${CORES}C/${THREADS}T = ${CPUS} logical CPU(s); stability=${STABLE_SECONDS}s"
qemu-system-x86_64 \
  -M q35 -accel tcg \
  -cpu "$CPU_MODEL" \
  -smp "cpus=$CPUS,sockets=$SOCKETS,cores=$CORES,threads=$THREADS" \
  -m 2G \
  -drive "file=$ISO,media=cdrom,if=ide,readonly=on" -boot d \
  -display none -monitor none -serial "file:$LOG" \
  "${QEMU_DEBUG_ARGS[@]}" \
  -no-reboot -no-shutdown 2> "$QEMU_STDERR" &
QEMU_PID=$!
trap 'kill "$QEMU_PID" 2>/dev/null || true; wait "$QEMU_PID" 2>/dev/null || true' EXIT

started=$SECONDS
ready_at=-1
last_tick=0
first_observed_tick=0
last_progress=$SECONDS
while :; do
  if ! kill -0 "$QEMU_PID" 2>/dev/null; then
    cat "$QEMU_STDERR" "$LOG" >&2
    fail 'QEMU exited before completion of the stability interval'
  fi
  if proof="$(check_serial "$LOG")"; then
    [[ "$proof" =~ last-tick=([0-9]+) ]] || fail 'missing timer count in validated serial proof'
    current_tick="${BASH_REMATCH[1]}"
    if (( ready_at < 0 )); then
      ready_at=$SECONDS
      first_observed_tick=$current_tick
      echo "$proof"
      echo "[SMT12-QEMU] Observing another ${STABLE_SECONDS}s for late faults or warnings..."
    fi
    if (( current_tick > last_tick )); then
      last_tick=$current_tick
      last_progress=$SECONDS
    fi
    (( SECONDS - last_progress < 10 )) || fail 'BSP timer stopped advancing for 10 seconds during stability observation'
    if (( SECONDS - ready_at >= STABLE_SECONDS )); then
      kill -0 "$QEMU_PID" 2>/dev/null || fail 'QEMU exited at end of stability interval'
      (( current_tick > first_observed_tick )) || fail 'BSP timer did not advance after readiness'
      echo '[SMT12-QEMU][OK] Managed AP execution, real OrionGC stop-the-world and BSP timer survived the stability interval.'
      echo "[SMT12-QEMU][OK] BSP timer advanced from tick $first_observed_tick to $current_tick."
      if (( CPUS == 1 )); then
        echo '[SMT12-QEMU][OK] Single-CPU regression: no application-processor managed entry exists in this topology.'
      else
        echo "[SMT12-QEMU][OK] $((CPUS - 1)) AP(s) executed one managed C# export and returned to native idle; no AP scheduler is claimed."
      fi
      exit 0
    fi
  else
    status=$?
    if (( status != 2 )); then
      cat "$QEMU_STDERR" "$LOG" >&2
      fail 'serial validation failed'
    fi
    (( ready_at < 0 )) || fail 'serial proof disappeared during stability observation'
    if (( SECONDS - started >= TIMEOUT )); then
      cat "$QEMU_STDERR" "$LOG" >&2
      fail "timeout after ${TIMEOUT}s waiting for complete Stage 12 managed-AP evidence"
    fi
  fi
  sleep 1
done
