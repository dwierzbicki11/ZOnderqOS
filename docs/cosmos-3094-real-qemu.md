# Cosmos 3.0.94 actual AP execution validation

This test fills an important gap: stages 1–5 previously only compiled the
patched Cosmos sources and assembled native code. None of those checks could
prove a guest CPU executed the AP entry point.

The separate `tests/Cosmos3094SmpBoot` project contains a minimal
`Cosmos.Sdk/3.0.94` kernel, referencing **only** the newly built patched
Cosmos 3.0.94 packages. Build CI replays the five migration patches onto
an isolated checkout of upstream v3.0.94, uses upstream's full package
builder, checks NuGet source provenance and publishes a real bootable ISO.
The production ZonderqOS SDK/runtime and launchers are unchanged.

QEMU boots that ISO with 1, 2, 4, 8 and 16 logical CPUs. The strict
serial validator requires:
- One Stage 4 success marker **after** READY from every AP 1..N−1.
- One Stage 5 success marker **after** the matching native workload counters
  report **exactly 100,000** increments from every AP.
- A distinct **kernel BOOT** marker afterward.
- No duplicate CPU IDs, APIC IDs, passes, missing CPUs, panics or resets.
- A stability window after boot confirmation.

`python3 tests/Cosmos3094SmpBoot/verify_boot.py --self-test` runs
standalone parser validation against positive/negative synthetic log
fixtures. The self-test is **not** an emulated execution result.

### Exit gate

The AP bootstrap is runtime verified **only** when the QEMU matrix succeeds
with actual guest serial logs. A successful patch/assembly/compile workflow
does not satisfy that gate. Even a fully green QEMU boot suite does **not**
prove managed thread scheduling, IPI rendezvous or GC synchronization; those
are subsequent 3.0.94 migration stages.

## Stage 6 — fixed-delivery local APIC IPI
The isolated Cosmos v3.0.94 QEMU probe now applies patches **1–6**,
rebuilds the patched SDK and boots 1, 2, 4, 8 and 16 logical CPUs.
After Stage 5 native work completes, a private 256-vector x64 IDT is
installed on APs, with only vector 0xF1 (fixed IPI) and 0xFF (spurious)
configured. APs remain in native code, use their own GS/stack, and
do not enter managed interrupt dispatch. BSP addresses each AP by
its **physical Limine LAPIC ID**; dense CPU IDs are only for state.
Each AP acknowledges exactly one fixed IPI and issues a LAPIC EOI;
spurious interrupts never issue EOI.

The strict serial parser requires one correct `[SMP3094-IPI]` record per AP,
the `[SMP3094-STAGE6] PASS` marker and subsequent BOOT marker.
It rejects a missing/duplicate/wrong LAPIC ID or acknowledgement,
misordered markers, and boot failures. Existing stability window remains.

Passing CI/assembly compilation alone is insufficient: **runtime QEMU
checks must pass before merging the Stage 6 patch**. This still
does **not** prove GC/scheduler managed preemption on APs.

## Stage 7 — repeated IPI delivery stress

After Stage 6's verified single native IPI, the BSP delivers **32 further
fixed IPIs per AP**. The native sender checks that every per-AP counter
advances by exactly one per message, waits for xAPIC ICR delivery status,
targets the physical LAPIC ID, and uses one 15-second global TSC deadline.
All CPUs must finish with exactly **33 acknowledgements** (1 + 32).

The serial validator rejects a missing/duplicate Stage 7 marker, wrong
physical LAPIC ID, wrong round count, wrong final counter, missing AP,
early output and kernel faults. Self-tests exercise those negative cases.
The real ISO/QEMU matrix rebuilds patched Cosmos 3.0.94 with patches
1–7 and boots with 1/2/4/8/16 logical CPUs.

Stage 7 remains **native-only**, not managed preemption or GC.

## Stage 8 — native IPI multi-CPU rendezvous (NOT managed STW)

Stage 8 adds a **separate fixed IPI vector F2** to the private AP-only
IDT, without changing the existing F1 stress test. Each AP enters its
own interrupt handler, publishes an **arrival epoch**, and remains blocked
inside native code until the BSP has verified **every AP arrived**.
Only then does the BSP publish one global release epoch. Each AP sends
EOI, publishes its exit epoch, and resumes its native STI/HLT loop.

There are **4 sequential barrier generations** per QEMU boot. All state
lives in per-AP cache-line-aligned BSS slots and two shared epoch words.
The BSP validates every CPU's previous state before sending an IPI,
targets Limine **physical** LAPIC IDs, and applies **one bounded
10-second deadline** to all four rounds and all CPUs. It validates
arrival, no early exit, no early release, exactly one handler hit and
eventual exit. Failure halts the isolated boot probe.

The updated strict serial verifier expects one
`[SMP3094-BARRIER] CPU=... LAPIC=... rounds=4 enter=4 exit=4 hits=4`
per secondary CPU between Stage 7 and Stage 8 PASS, before BOOT.
The real QEMU matrix (1/2/4/8/16 CPUs) remains the merge gate.

**Scope:** this proves only a native cross-CPU rendezvous primitive.
It does not yet stop managed mutator threads, synchronize the NativeAOT
GC, implement IPIs on ARM64 or schedule managed threads on APs.
