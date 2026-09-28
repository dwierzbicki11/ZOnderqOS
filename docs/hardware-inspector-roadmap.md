# Hardware Inspector roadmap

This subsystem is intentionally read-only and independent from the SMP/VMM/driver-stack work.

## Stage H1 - shared snapshot + shell command
Status: implemented on this branch.

- architecture-neutral `HardwareSnapshot`
- CPU vendor/model/topology/features/cache/frequency
- total/free/used physical memory from `PageAllocator`
- scheduler name, online CPU count and thread-slot count
- block-device and partition counts from the existing storage manager
- `hwinfo [all|cpu|memory|scheduler]`
- no direct hardware mutation

Completion gate: both x64 and ARM64 managed builds must compile with the existing architecture-specific `CpuHardwareInfo` backend.

## Stage H2 - GUI Hardware Inspector
Status: implemented on this branch.

The existing advanced-kernel panel now has a third `HARDWARE` page backed by the shared `HardwareSnapshot`. CPUID/memory logic remains outside the GUI.

## Stage H3 - report/export
Status: implemented on this branch.

`sysreport [path]` exports a stable plain-text diagnostic report with hardware, memory, scheduler, storage and recent system-log state. Serial numbers, MAC addresses, user identity and command history are deliberately excluded.

## Non-goals
PCI enumeration, storage-controller probing and USB device binding belong to the driver-stack roadmap and must not be duplicated here.
