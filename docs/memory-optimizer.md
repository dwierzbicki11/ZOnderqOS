# ZonderqOS Memory Optimizer (experimental)

Branch: `feat/memory-optimizer-10s`, based on
`fix/alpha-hardware-idle-memory` (Cosmos stock 3.0.95).

## Design

- **No new process or worker thread**. The existing `sys_guardian` wakes
  every 2 seconds. The existing 4-second memory-warning cadence is preserved;
  `MemoryOptimizer.SampleIfDue` samples allocator pages **every 10 seconds**.
- Read-only data: `PageAllocator.TotalPageCount`, `FreePageCount`, `PageSize`.
  The sampling path performs no filesystem I/O or managed buffer allocation.
- After **8 MiB growth** from the last baseline, or when free managed-pool
  pages fall to **12.5%**, request a trim. The minimum cooldown between trim
  requests is **120 seconds**.
- On the GUI thread, between application update and rendering, trim at most
  **16 cold scaled-icon cache entries**. On each sweep, entries touched since
  the previous sweep are preserved. First sweep primarily establishes the
  access baseline. Source PNG pixel buffers are never dropped.
- `MemoryOptimizer.SetGuiReady(false)` cancels pending requests on logout.
- No calls to `GC.Collect`, `Heap.Collect`, or manual page freeing; no
  changes to stock Cosmos Kernel or boot/USB/A-B update code.

**Important:** dropping `int[]` references only makes those arrays eligible
for eventual OrionGC reclamation. The `CandidateBytes` metric is **not**
actual RAM returned to `PageAllocator`, and need not correlate with a
decrease in Task Manager's used-memory counter.

## Counters

The Task Manager / Performance / Memory view exposes:

- `MEMOPT PROBKI`: executed 10-second allocator samples
- `MEMOPT ZADANIA`: conditional trim requests
- `CACHE SWEEPS`: completed GUI-thread sweeps
- `ODPIETE BUFORY`: scaled-image buffers unrooted
- `GC KANDYDAT KiB`: cumulative payload bytes eligible for future GC
  (not physical pages released).

The serial `[MEM_SAMPLE]` line includes:
`mo_checks`, `mo_req`, `mo_sweep`, `mo_detached`, `mo_candidate_kb`.

## Testing

Static contracts: `python3 -m pytest -q tests/test_memory_optimizer_contracts.py`.

Required before merging or flashing a physical USB device:

1. Full existing `pytest -q` and C# host smoke tests.
2. `dotnet publish` for x64 and ARM64, stock Cosmos SDK 3.0.95.
3. QEMU 1 vCPU and 8 vCPU, 512 MiB and 2 GiB configurations, with FAT32
   `ZONDERQDATA` 32 KiB clusters, comparing baseline vs this branch.
4. 60-minute idle desktop, 60-minute open Task Manager, GUI lock/unlock,
   open/close apps, resize icons; confirm correct icons after cache sweeps.
5. Log `[MEM_SAMPLE]` from serial and compare `p_gc`, `p_rat`, `p_sys`,
   `gc_comm_kb`, `gc_gen` and optimizer counters.
6. Physical ASUS X540LA A/B compare at 0/20/60/120 minutes, while confirming
   the exact kernel ELF hash in the booted slot.

**Do not claim a fixed leak or actual RAM reclamation solely because
`mo_candidate_kb` increases.** This optimization targets *safe cache
ownership* and diagnostic visibility, not forceful garbage collection.

## Known limitations

- There is currently only one explicitly approved trim target: the scaled
  icon cache. Active app state, source PNG buffers and graphics framebuffers
  are untouched.
- The original observed growth on the ASUS could still originate in OrionGC,
  drivers, VFS, or task-manager sampling; this branch is not evidence that
  those sources are fixed.
- If a user opens a previously cold icon size after eviction, the scaled
  buffer is recreated. Bounded sweeps and touch markers limit churn.
- Additional caches require explicit single-thread ownership rules and
  independent regression/QEMU validation.

## Lock-screen RAM growth diagnosis (experimental)

A reported ~1 MiB change when entering the locked session might be a
one-time FAT32/security logging cost or a sustained idle growth. The
lock screen avoids full-frame redraws without mouse/keyboard activity;
on entry it calls `SecurityLogger.LogEvent`, which in turn appends to both
`/var/log/system.log` and `/var/log/auth.log`.

To distinguish these costs, this branch emits additional **COM1-only**
`[LOCK_MEM]` snapshots on x64 with the fields `phase`, `used_p`,
`free_p`, `gc_heap_kb`, and `gc_comm_kb`.

- `before_lock_log` -> `after_lock_log`: security event / filesystem logging
- `after_lock_init` -> `after_first_lock_paint`: initial lock screen draw
- `after_first_lock_paint` -> periodic `[MEM_SAMPLE]` lines:
  long-running idle growth while the screen stays locked
- `before_lock_exit` -> `after_lock_screen` -> `after_unlock_log`:
  teardown and unlock event logging

Collect the entire serial output from QEMU or a USB-to-serial setup. Compare
`used_p` (multiply page deltas by allocator page size), `gc_comm_kb`,
`p_gc`, `p_rat`, and `p_sys`. A few transient pages do **not** prove a
memory leak; a repeatable sustained positive slope needs investigation.
No persistent audit logs or authentication checks are removed by tracing.

This instrumentation is not yet an identified root-cause fix. Test it on
QEMU before using the updated branch on bare metal.
