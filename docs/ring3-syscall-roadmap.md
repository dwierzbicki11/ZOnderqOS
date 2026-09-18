# Ring 3 / syscall boundary roadmap

Status: **Stage R1 in validation. Ring 3 is not enabled yet.**

Every stage is dependency-gated. A later stage MUST NOT start until the previous stage is green for the exact PR-head SHA in GitHub Actions. Runtime stages additionally require real QEMU proof; compilation or a synthetic PASS marker is not sufficient.

## Stages

| Stage | Goal | Completion gate | Status |
|---|---|---|---|
| R1 | Stable ABI + fail-closed scaffolding | ABI contract tests green on exact SHA; invalid/unconfigured/high-half/wrapped ranges rejected | IN VALIDATION |
| R2 | Integrate real VMM/process address space | Per-process user range comes from VMM ownership; x64 + ARM64 regression CI green | BLOCKED by R1/VMM |
| R3 | x86_64 GDT/TSS CPL3 foundation | Valid CPL3 selectors + per-CPU kernel transition stack; QEMU proof | BLOCKED |
| R4 | x86_64 syscall entry/exit ASM | Documented register ABI; CPL3->CPL0->CPL3 round-trip in QEMU | BLOCKED |
| R5 | Minimal dispatcher + user-pointer access | exit/write/getpid with checked copy-in/out and negative pointer tests | BLOCKED |
| R6 | Minimal user payload/process | Isolated user payload executes real syscalls under owned address space | BLOCKED |
| R7 | Fault isolation | CPL3 kernel write faults, offending process is contained, kernel remains alive | BLOCKED |
| R8 | Stress/regression | Repeated syscall/fault cycles; 1-vCPU + SMP smoke; ARM64 regression green | BLOCKED |

## Current dependency

The current process implementation does not yet provide a verified per-process CR3/page-table address space. Entering CPL3 before the VMM/process work provides isolated address spaces would create a misleading or unsafe user/kernel boundary. Therefore R2 and later remain blocked even if R1 becomes green.

## ABI v1

Initial syscall numbers are defined in `UserKernelAbi.Syscall` and must remain stable after the first user binary is shipped. The initial contract reserves exit, read/write, open/close, mmap/munmap, getpid and yield.

All pointer-bearing syscalls must validate the complete `[pointer, pointer + length)` interval against the current process user address range before dereferencing it. Validation must reject integer wraparound, kernel/high-half addresses, unconfigured address spaces and ranges crossing the user/kernel boundary.

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
