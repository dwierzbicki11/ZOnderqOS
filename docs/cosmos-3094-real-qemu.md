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
