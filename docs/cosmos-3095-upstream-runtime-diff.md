# Cosmos v3.0.94 → v3.0.95 runtime compatibility checkpoint

Source: official `CosmosOS/Cosmos` tags `v3.0.94...v3.0.95`, compared on 2026-10-24.

**153 source files changed across 40 upstream commits.** The managed Stage23 patch set from older versions cannot be safely transplanted wholesale. Key runtime touchpoints changed:

| File | Upstream diff size |
|---|---:|
| `src/Cosmos.Kernel.Core/Scheduler/SchedulerManager.cs` | +10 / −5 |
| `src/Cosmos.Kernel.Core/Scheduler/SchedulerThread.cs` | +25 / −0 |
| `src/Cosmos.Kernel.Core/Scheduler/Stride/StrideScheduler.cs` | +54 / −39 |
| `src/Cosmos.Kernel.Core/Scheduler/ConditionVariable.cs` | +88 / −0 |
| `src/Cosmos.Kernel.Core/Scheduler/IScheduler.cs` | +5 / −1 |
| `src/Cosmos.Kernel.Core/Memory/GarbageCollector/GarbageCollector.Mark.cs` | +1 / −1 |

## Mandatory managed port review order

1. Isolate native startup (Limine MP response, xAPIC IDs, per-CPU stack and GS/IDT). The initial Stage1 overlay must neither wake APs nor run managed code.
2. After real native AP proof, inspect each 3.0.95 `SchedulerThread` state and context ownership change; preserve atomic state transitions.
3. Reconcile Stride queue operations with the 3.0.95 changes, including condition variable wake-ups; do not resurrect old idle-blocking behavior.
4. Review GC stack scanning and safe-point barriers across **every** managed AP. Do not run managed AP threads before global stop-the-world is correct.
5. Require real serial CPU residency and nontrivial worker work checks, then GC stress/topology/regression matrices.

This document is a code-review checklist **not** a statement that native AP startup or Stage23 managed scheduling is functional.
