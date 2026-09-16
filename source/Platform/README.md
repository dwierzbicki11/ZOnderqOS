# ZonderqOS platform architecture

ZonderqOS userspace and GUI code are architecture-neutral. Hardware/ABI differences belong under `source/Platform`.

## Layout

- `Common/` - compatibility and runtime adapters shared by every architecture.
- `X64/` - x86_64-only implementations (CPUID, x86 intrinsics, x64-specific hardware knowledge).
- `Arm64/` - ARM64-only implementations (QEMU virt / AArch64 platform knowledge).

`ZonderqOS.csproj` compiles `Common/` for every target and selects exactly one architecture backend. Code outside `source/Platform` should not use `ARCH_ARM64`/`ARCH_X64` to choose hardware implementations and should not directly import architecture-only APIs such as `System.Runtime.Intrinsics.X86`.

Where both backends implement the same application-facing type (for example `ZonderqOS.GUI.Apps.CpuHardwareInfo`), their public shape must stay compatible so the GUI does not need architecture branches.

This separation does not hide real platform limitations. For example, if the Cosmos ARM64 HAL currently manages only CPU0, ARM64 telemetry reports one online CPU rather than inventing SMP support.
