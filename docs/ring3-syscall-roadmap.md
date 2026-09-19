# Ring 3 / syscall boundary roadmap

Status: **Stage R1 complete. Stage R2 blocked by the real VMM/process address-space dependency. Ring 3 is not enabled yet.**

Every stage is dependency-gated. A later stage MUST NOT start until the previous stage is green for the exact PR-head SHA in GitHub Actions. Runtime stages additionally require real QEMU proof; compilation or a synthetic PASS marker is not sufficient.

## Stages

| Stage | Goal | Completion gate | Status |
|---|---|---|---|
| R1 | Stable ABI + fail-closed scaffolding | ABI contract tests green on exact SHA; invalid/unconfigured/high-half/wrapped ranges rejected | COMPLETE — CI green at `506a435cf723b386bee0e092cdb024daf40096fe`; revalidated green at `7a22c9b509de4c0479b90bfcff8b9683cdfbabd5` and `b41e65cbcb8a09b4f120f564f537fb652753070a` |
| R2 | Integrate real VMM/process address space | Per-process user range comes from VMM ownership; x64 + ARM64 regression CI green | BLOCKED by VMM V3/V4 ownership path |
| R3 | x86_64 GDT/TSS CPL3 foundation | Valid CPL3 selectors + per-CPU kernel transition stack; QEMU proof | BLOCKED |
| R4 | x86_64 syscall entry/exit ASM | Documented register ABI; CPL3->CPL0->CPL3 round-trip in QEMU | BLOCKED |
| R5 | Minimal dispatcher + user-pointer access | exit/write/getpid with checked copy-in/out and negative pointer tests | BLOCKED |
| R6 | Minimal user payload/process | Isolated user payload executes real syscalls under owned address space | BLOCKED |
| R7 | Fault isolation | CPL3 kernel write faults, offending process is contained, kernel remains alive | BLOCKED |
| R8 | Stress/regression | Repeated syscall/fault cycles; 1-vCPU + SMP smoke; ARM64 regression green | BLOCKED |

## R1 validation record

R1 was first validated by GitHub Actions workflow `Ring3 stage R1 ABI foundation`, run 2, for exact SHA `506a435cf723b386bee0e092cdb024daf40096fe`. The same R1 gate was revalidated successfully on PR-head SHA `7a22c9b509de4c0479b90bfcff8b9683cdfbabd5` (run 3) and, after tightening the R2 ownership/lifetime requirements, on SHA `b41e65cbcb8a09b4f120f564f537fb652753070a` (run 4, Actions run `35422876138`). These runs prove only the ABI/fail-closed scaffolding contract; they are not evidence that CPL3 or memory isolation is active.

## Current dependency: exact VMM gate for R2

The process/VMM work is tracked in PR #67 (`process/vmm-foundation`). Its current V3 work has a truthful x86_64 page-table encoding model, but it deliberately does not install mappings, own a PML4, switch CR3, or provide an isolated `UserProcess` address space. V3 is currently blocked on a safe physical page-table-frame allocation boundary with Cosmos; the Ring 3 branch must not work around that by inventing physical frames or by deriving a user range from constants.

R2 may start implementation only after the VMM side exposes an owned process address space backed by real page tables and a query path that can prove page presence plus user/read/write permissions. Before R2 can be marked complete, the Ring 3 integration must demonstrate all of the following:

- syscall context is constructed from the current `ProcessControlBlock` / owned address space, not caller-supplied constants;
- the full user buffer range is checked for overflow/canonicality and every covered page is proven user-accessible through VMM query semantics before any dereference;
- kernel/shared mappings are rejected even when their numeric address would otherwise fit a broad range;
- address-space lifetime is pinned for the duration of copy-in/copy-out so unmap/teardown cannot race validation;
- x86_64 regression CI is green and ARM64 continues to compile/boot without inheriting x86-specific CR3 semantics.

Until those prerequisites exist, `SyscallDispatcher.Context.UserRange` remains explicit/fail-closed and R3 MUST NOT begin. A green static VMM encoding workflow alone is not sufficient to unblock R2.

## ABI v1

Initial syscall numbers are defined in `UserKernelAbi.Syscall` and must remain stable after the first user binary is shipped. The initial contract reserves exit, read/write, open/close, mmap/munmap, getpid and yield.

All pointer-bearing syscalls must validate the complete `[pointer, pointer + length)` interval against the current process user address range before dereferencing it. Validation must reject integer wraparound, kernel/high-half addresses, unconfigured address spaces and ranges crossing the user/kernel boundary. Range validation is necessary but not sufficient: once R2 begins, VMM page permissions and address-space lifetime must also be checked.

## Mandatory runtime proof before calling Ring 3 complete

- CPL3 payload executes and performs a real syscall into CPL0 and returns to CPL3.
- A valid user buffer can be read/written only within its mapped user pages.
- A CPL3 write to a kernel page raises a controlled protection fault; the offending process is terminated/reported while the kernel remains alive.
- Invalid, wrapped and boundary-crossing syscall pointers return an error without causing a kernel page fault.
- Unknown syscall numbers return `NotSupported` (or the final documented equivalent).
- Repeated syscall/fault cycles run under QEMU x86_64 without corrupting scheduler/GC state.
- Existing 1-vCPU and SMP smoke tests remain green; ARM64 continues to compile/boot even though this first privilege-boundary implementation is x86_64-specific.

## Explicit non-goals of R1

R1 does not install GDT/TSS entries, change CPL, execute `syscall/sysret`, dereference user pointers, create page tables, or claim user-mode isolation. Those changes belong after the VMM/process dependency is available and testable.
