# Ring 3 / syscall boundary roadmap

Status: foundation only. **Ring 3 is not enabled yet.**

## Current dependency

`source/Core/ProcessManager.cs` currently represents kernel-managed work with `System.Threading.Thread`; it does not provide a per-process CR3/page-table address space. Entering CPL3 before the VMM/process work provides isolated address spaces would create a misleading or unsafe user/kernel boundary.

For that reason this branch starts with the stable ABI contract and fail-closed user-pointer validation only. The low-level `SYSCALL/SYSRET` or interrupt-gate entry path must not be enabled until the VMM dependency is real.

## ABI v1

Initial syscall numbers are defined in `UserKernelAbi.Syscall` and must remain stable after the first user binary is shipped. The initial contract reserves exit, read/write, open/close, mmap/munmap, getpid and yield.

All pointer-bearing syscalls must validate the complete `[pointer, pointer + length)` interval against the current process user address range before dereferencing it. Validation must reject integer wraparound, kernel/high-half addresses, unconfigured address spaces and ranges crossing the user/kernel boundary.

## Required implementation order

1. VMM exposes a per-process x86_64 address-space object and concrete user virtual range.
2. Process lifecycle owns that address space and switches CR3 safely.
3. x86_64 GDT/TSS has valid CPL3 code/data selectors and a kernel stack for privilege transitions.
4. Add syscall entry/exit assembly with an explicitly documented register ABI and preserved-register rules.
5. Dispatcher validates syscall number and all user pointers before touching user memory.
6. Implement minimal `exit`, `write`, `getpid` first; add file and memory calls only when their kernel backends can enforce process ownership.
7. Add a minimal user-mode payload/loader and runtime tests.

## Mandatory runtime proof before calling Ring 3 complete

- CPL3 payload executes and performs a real syscall into CPL0 and returns to CPL3.
- A valid user buffer can be read/written only within its mapped user pages.
- A CPL3 write to a kernel page raises a controlled protection fault; the offending process is terminated/reported while the kernel remains alive.
- Invalid, wrapped and boundary-crossing syscall pointers return an error without causing a kernel page fault.
- Unknown syscall numbers return `NotSupported` (or the final documented equivalent).
- Repeated syscall/fault cycles run under QEMU x86_64 without corrupting scheduler/GC state.
- Existing 1-vCPU and SMP smoke tests remain green; ARM64 continues to compile/boot even though this first privilege-boundary implementation is x86_64-specific.

## Explicit non-goals of the foundation commit

This checkpoint does not install GDT/TSS entries, change CPL, execute `syscall/sysret`, dereference user pointers, create page tables, or claim user-mode isolation. Those changes belong after the VMM/process dependency is available and testable.
