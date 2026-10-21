# Cosmos 3.0.94 SMP migration — evidence ledger (Gen 3)

This is **Cosmos v3.0.94 only**. Do not use or silently apply the old
`patches/cosmos-smt/*` series from an earlier Cosmos ABI.

## What the 3.0.94 kernel can already prove

`patches/cosmos-3094/0001..0008` adds Limine MP CPU enumeration,
per-CPU GS/stack setup, native AP parking/work, real fixed IPI,
32-round IPI stress and a 4-epoch native F2 rendezvous.

The Stage 8 implementation now also publishes a **monotonic atomic release
epoch on all BSP failure paths** (timeout and invalid state) before returning
an error. This prevents APs which already received F2 from remaining trapped
forever in the native spin loop during an aborted proof. The BSP still fails
the proof; it must never log a false Stage 8 PASS after an abort.

Stage 8 only proves APs enter and leave a native barrier under IPI;
**it does not dispatch ordinary System.Threading.Thread managed code on
multiple CPUs, preempt them, or stop the world for OrionGC**. It must not
be counted as a production SMP Stage23 PASS.

## Extended physical topology matrix (PR #163)

The isolated Cosmos **3.0.94** QEMU workflow builds a *single matching
patched 3.0.94 ISO* and boots it separately on the following profiles:

| QEMU model | vCPUs | Cores | SMT threads/core | Boots |
|---|---:|---:|---:|---:|
| Nehalem | 1 | 1 | 1 | 1 |
| Nehalem | 2 | 2 | 1 | 1 |
| Nehalem | 4 | 4 | 1 | 1 |
| Broadwell | 4 | 2 | 2 | 2 |
| Nehalem | 8 | 4 | 2 | 1 |
| Nehalem | 16 | 8 | 2 | 1 |
| EPYC-Rome | 32 | 16 | 2 | 1 |
| EPYC-Rome | 56 | 28 | 2 | 2 |

The Broadwell 2C/4T profile matches the user's physical Intel Core
i3-5005U **topology** (not every BIOS/ACPI peripheral of the laptop).
The larger EPYC-style matrices check that dense CPU indexing, physical
APIC-ID routing and stage-8 global rendezvous state scale up.

Each QEMU boot must show **every** expected AP ID and exact real
per-AP work counter (100000), IPI count (1), IPI stress rounds (32),
and barrier generation enters/exits/hits (4); otherwise fail. A
negative scenario with a missing/duplicated/APIC>255 or spoofed marker
must be rejected. The full source serial is stored as an Actions
artifact for debugging.

No test can claim native timer interrupts, GC safepoints, true parallel
managed work or physical Broadwell reliability from these proofs alone.

## Stage 9a — AP-local timer *readiness* (new isolation gate)

The independent `0009-ap-timer-readiness.patch` builds on the validated
3.0.94 Stage8 patch, without replaying the older 3.0.85 Stage9 native code.
While executing an F2 rendezvous on **each actual AP**, its handler samples
the LAPIC Timer LVT register (offset `0x320`) into the AP's own 64-byte
barrier record. The BSP reads that real hardware sample through a native
getter **after Stage8 has published EXITED**. It rejects unmasked or
periodic timer configurations, verifies its already-calibrated LAPIC
timer has a bounded nonzero 10ms interval count, and emits
`[SMP3094-TIMER-BASE]`, `[SMP3094-TIMER-READY]` for each AP, followed
by `[SMP3094-STAGE9A] PASS` only on success.

The new CI workflow `cosmos-3094-stage9a.yml` applies Stage1..8 + Stage9a
in a fresh **v3.0.94** checkout, assembles native x64 code, builds pinned
SDK/runtime packages and a standalone QEMU ISO. It requires both the
existing strict Stage8 serial proof and the new Stage9a parser, including
negative cases for duplicate CPU IDs, wrong calibration, unknown markers
and unmasked/periodic AP timer.

**Stage9a is NOT complete Stage9.** It has NOT enabled AP LAPIC periodic
timer interrupts, run timer ISRs, dispatched ordinary managed threads,
preempted AP threads, stopped OrionGC, or passed a production SMP soak.
The next Stage9b must add a native F3 timer vector with bounded
AP-local periodic tick observation and explicit stop/cleanup on failure.

## Remaining hard blockers before a production 3.0.94 Stage 23 gate

1. **Native AP LAPIC periodic timer (Stage 9)**. Port the timer vector
   and bounded TSC waits to the already installed 3.0.94 AP-local IDT,
   keeping interrupts and handlers native until GC is safe.
2. **GC stop-the-world (Stages 10–11)**. Coordinate *every* managed
   scheduler CPU and check stop/release/abort barriers, including failures.
3. **Managed execution and scheduler (Stages 12–21)**. Managed AP
   thread residency, AP timer preemption, migration, live handoff,
   load-balancing; preserve register/GC stack maps correctly.
4. **Stage 22/23 production**. Full QEMU topology/soak including 1/2/4/8/
   16/32/56 vCPUs, real `htop` scheduling counters and physical Broadwell
   long-running tests without OrionGC kernel panic.

The former production workflow is still a failing compatibility gate
against an older Cosmos release. Its failures must **not** be
relabelled successful or hidden. Only a complete v3.0.94 port can
replace it.
