# SMT Nightly Report

Date: 2026-09-18
Start commit: `e81c88590b0d1b8d83e792722e8b04bc6d27c977`
End commit: `4139e788b5907500e1e337c580fc4d85cfee5575`
Branch: `smt/stage13-percpu-scheduler`
Cosmos base: v3.0.85 (`109ec2aacf2226b8cef53ab496920a285b626848`)
Agent: Codex autonomous SMT run

## Starting state

- Stage 12 was complete on open PR #62, stacked at `e81c88590b0d1b8d83e792722e8b04bc6d27c977`.
- Stage 12 CI run `35364108276` was green, including the real 1-vCPU and 8-vCPU QEMU gates.
- `main` was not modified.
- No full SMP scheduler, AP managed threads, migration, AP context switching, or preemption was claimed.

## Stage 13 status

Status: PASS, limited to the per-CPU scheduler foundation proof.

Implemented:

- one-shot atomic phase and entry counters on each managed `PerCpuState`;
- BSP validation and enter/leave exercise for every initialized per-CPU scheduler state;
- native command 6 and fixed-IPI wake for each parked AP;
- a bounded managed AP export that checks only GS-local `CpuId`, publishes phase/count, and returns to native HLT;
- native mailbox validation, exact serial diagnostics, apply/idempotence checks, ARM64 compile coverage, ISO build, QEMU smoke, and stability gates.

The AP export deliberately does not traverse the managed scheduler object graph. AP-side managed thread/runtime/GC participation is reserved for a later, separately specified stage.

## Validation

Local checks:

- PASS: fresh pinned Stage 1..13 apply;
- PASS: reverse apply 13..1 and final clean checkout;
- PASS: repeated-apply/idempotence detection;
- PASS: applied-tree `git diff --check`;
- PASS: Bash syntax and native x64 assembler-with-C preprocessor compile.
- Not available locally: .NET/Cosmos package build, ISO, and QEMU tools.

CI run [35374185146](https://github.com/dwierzbicki11/ZOnderqOS/actions/runs/35374185146) is green:

- 8 vCPU (`1S/4C/2T`, Nehalem): real ISO, Stage 12 regression, 7 AP CPU-local handshakes, Stage 13 PASS, and 30-second stability; BSP timer advanced from tick 4 to 2900.
- 1 vCPU (`1S/1C/1T`, Nehalem): real ISO, Stage 12 regression, ARM64 managed regression compile, BSP Stage 13 proof, Stage 13 PASS, and 30-second stability; BSP timer advanced from tick 5 to 2900.

Observed 8-vCPU Stage 13 evidence:

```text
[SCHED-PERCPU] cpu=0 phase=2 entries=1 state=bsp-idle
[SCHED-PERCPU] cpu=1..7 phase=2 entries=1 state=returned-native-idle
[SCHED-PERCPU-TEST] PASS
```

The actual serial log contains one separate line for each AP (`cpu=1` through `cpu=7`); the compact range above is only a summary.

## Failed experiments

1. CI run `35372111569` passed build/ISO and 1-vCPU QEMU, but 8-vCPU QEMU stopped after `[SCHED-PERCPU] dispatch` and timed out before `native-return`. The first implementation traversed `PerCpuState` and scheduler objects from the AP reverse-P/Invoke entry.
2. The fix reduced the AP entry to a CPU-local identity check and moved the managed `PerCpuState` lifecycle proof to the BSP. This preserves a bounded, testable foundation without pretending that APs already own managed scheduler threads.
3. Commit `871cf09a94a6cc92d384295d88ba283e09201c37` briefly published an incorrectly generated patch containing `tools/...` paths. CI run `35373638070` caught this during preflight before compilation. The patch was regenerated from a Cosmos-only Stage 12 baseline and published as `4139e788b5907500e1e337c580fc4d85cfee5575`.

## Current SMP/SMT state

- QEMU detects 8 logical CPUs and maps BSP to dense `CpuId=0`.
- Seven APs boot, receive bounded native commands, execute the managed CPU-local identity export, publish results, and return to native HLT.
- Each managed per-CPU scheduler state is initialized and BSP-tested once.
- IPI, APIC, native AP work, OrionGC rendezvous/integration, and Stage 12 managed-export regressions remain green through the Stage 13 workflow.
- There is no full SMP scheduler, no managed AP idle thread, no thread migration, no AP context switch, and no timer preemption.

## Unsafe assumptions and review points

- The current x64 AP path is xAPIC-oriented and bounds dense IDs to 0..255.
- Limine supplies the AP descriptor in RDI and APs inherit sufficient descriptor/IDT state for this native entry.
- BSP is dense `CpuId=0`; TSC progress is sufficient for bounded native waits.
- The Stage 13 AP export must remain allocation-free and object-graph-free until a managed AP thread/runtime contract exists.
- ARM64 is compile-regressed only; no ARM64 SMP runtime claim is made.

## Open PRs

- [PR #62](https://github.com/dwierzbicki11/ZOnderqOS/pull/62): Stage 12 managed C# export on APs; open, not merged.
- [PR #63](https://github.com/dwierzbicki11/ZOnderqOS/pull/63): Stage 13 per-CPU scheduler foundation; open, not merged; head `4139e788b5907500e1e337c580fc4d85cfee5575`.
- `main` remains untouched.

## Recommended next step

The repository has no numbered Stage 14 contract. The next implementation should be a separately reviewed, narrowly scoped AP managed-thread enrollment stage with explicit runtime/GC prerequisites and a new QEMU proof. Do not start context switching or preemption by inference from this report.

# Morning review handoff

## Commits created in this run

- `f8b1193` — bound Stage 13 AP proof to CPU-local identity (local checkpoint).
- `7aabddb` — publish complete Stage 13 Cosmos patch (local checkpoint).
- Remote PR head `871cf09a94a6cc92d384295d88ba283e09201c37` contains the first bounded-proof correction.
- Remote PR head `4139e788b5907500e1e337c580fc4d85cfee5575` contains the corrected Cosmos-only patch.

## Important code areas

- `patches/cosmos-smt/0013-percpu-scheduler-foundation.patch`
- `tools/smoke-stage13-qemu.sh`
- `tools/prepare-cosmos-smt.sh`
- Cosmos `SchedulerManager.RunPerCpuSchedulerFoundationProof`
- Cosmos `PerCpuSchedulerNative.Entry`
- Cosmos native command-6 mailbox in `Interrupts.s`

## Passing tests

- `SMT stage 13 per-CPU scheduler foundation`, run `35374185146`, 8-vCPU and 1-vCPU matrix entries: PASS.
- Stage 12 regression and 30-second stability inside both Stage 13 QEMU jobs: PASS.

## Failing tests

- Prior 8-vCPU Stage 13 run `35372111569`: timeout at the managed scheduler-object traversal; fixed and superseded.
- Publication preflight run `35373638070`: malformed patch artifact with non-Cosmos paths; fixed and superseded.
