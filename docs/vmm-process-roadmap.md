# VMM / real-process roadmap

This roadmap gates the migration from managed kernel tasks to real isolated processes. A stage is complete only when the exact PR-head SHA has green required GitHub Actions and, for runtime stages, a real QEMU proof. Compilation alone is not runtime proof. Do not advance on a red gate.

## V1 — truthful process model and lifecycle

Status: **implementation present; validation gate pending**.

Criteria:
- existing `ProcessManager.Start` objects are explicitly kernel tasks, not isolated processes;
- PID allocation and lifecycle live in process-specific abstractions;
- shared kernel address space is represented explicitly;
- x86_64 and ARM64 builds remain regression-free.

Gate: build/CI for the exact PR head. No runtime-isolation claim is made in V1.

## V2 — architecture-neutral VMM contracts

Status: blocked by V1 gate.

Criteria:
- page size/alignment and mapping contracts;
- map/unmap/protect/query semantics and permission flags;
- user/kernel virtual-range validation;
- no CR3-specific API leaks into architecture-neutral process code;
- contract/unit/static tests in CI.

## V3 — x86_64 page-table backend

Status: blocked by V2.

Criteria:
- controlled PML4 root creation/destruction;
- kernel mappings inherited/shared intentionally, user mappings isolated;
- safe page-table walking and permission updates;
- TLB invalidation rules documented and implemented;
- QEMU proof for mapping/query/unmapping.

ARM64 must remain behind the common VMM contract; x86 CR3 semantics must not be forced onto it.

## V4 — process address-space ownership and CR3 switching

Status: blocked by V3.

Criteria:
- a `UserProcess` owns a real isolated address space;
- scheduler/context-switch path switches address spaces safely on x86_64;
- kernel tasks continue to use the shared kernel address space;
- QEMU alternation test proves mappings do not leak between two address spaces.

## V5 — controlled page-fault isolation

Status: blocked by V4.

Criteria:
- faults are attributed to the current process/address space;
- invalid user access terminates/faults that process rather than panicking the whole kernel where recovery is valid;
- kernel faults remain fatal/diagnostic rather than being hidden;
- QEMU negative tests include unmapped access and cross-process mapping access.

## V6 — user-process resources and loader-facing contract

Status: blocked by V5.

Criteria:
- process-owned mapping/resource bookkeeping and deterministic cleanup;
- loader-facing API can construct a process image without bypassing VMM permissions;
- lifecycle covers create/run/stop/fault/exit and releases resources;
- CI + QEMU lifecycle/regression tests.

## V7 — isolation stress/regression

Status: blocked by V6.

Criteria:
- repeated process/address-space create/destroy and context switches;
- no cross-process writable-memory leakage;
- no regression in boot/storage/GUI and existing SMP/GC gates;
- x86_64 QEMU runtime proof plus available ARM64 compile/boot regression.

Only after V7 is green may this VMM/process foundation be called complete. Ring-3/syscall work may consume only contracts whose corresponding VMM stage is green.