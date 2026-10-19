# ZonderqOS Cosmos 3.0.94 SMP migration

**Do not** change `ZonderqOS.csproj`, `run.sh` or production Stage 23
`patches/cosmos-smt`: they remain pinned to patched Cosmos 3.0.85 until
all SMP and managed runtime functionality has been carried over and tested.

## Upstream drift observed

- v3.0.94 HAL x64 initialization no longer matches the v3.0.85 hunks.
- Upstream v3.0.94 scheduler has different internals, and the old
  `SchedulerDiagnostics.cs` file in patch 0001 is absent.
- `RhWaitForPendingFinalizers` now exists upstream in `Stdllib.cs`;
  adding the old export again would be incorrect.
- The Gen3 keyboard API changed from `Keyboard.KeyEvent` /
  `ConsoleKeyEx` to `Input.KeyEvent` / `Key`. ZonderqOS already
  includes conditional source compatibility for this change.

## Stage 1: Limine MP discovery only

`patches/cosmos-3094/0001-limine-mp-discovery.patch` is **a new patch
against v3.0.94**, not a blind replay of the 3.0.85 patch 0001.
It introduces a bounded Limine MP descriptor validator with duplicate
LAPIC/BSP checks and diagnostic count logging from x64 HAL init.
The validator rejects invalid input and never sets an AP entry point,
so it does not claim that SMP scheduling is working.

The CI job checks it against a clean pinned Cosmos v3.0.94 checkout and
compiles the patched Limine protocol library and x64 HAL. Its isolated
checkout must not affect the existing 3.0.85 build and QEMU workflows.

## Next stages / exit gates

1. Port topology and AP bootstrap with validated dense CPU IDs.
2. Port GS, interrupt/IPI, per-CPU scheduler state, timers.
3. Integrate GC stop-the-world and managed runtime thread exports
   **against the rewritten 3.0.94 runtime**, avoiding duplicate exports.
4. Rebuild the patched packages, add ARM64 compile checks, and test
   QEMU for 1, 2, 4, 8, and 16+ logical CPUs with managed work on APs.
5. Verify real CPU accounting/htop, shell, GUI, keyboard, storage, then
   update all version pins in one reviewed change.

The migration is complete only when the final production Stage 23
regressions pass on 3.0.94. Stage 1 alone is not a production upgrade.

## Stage 2: dense logical CPU IDs (development only)

`patches/cosmos-3094/0002-dense-cpu-topology.patch` is applied **after**
stage 1 on an isolated v3.0.94 checkout. It validates the Limine topology,
assigns BSP CPU 0 and then contiguous IDs to all other logical CPUs through
`ExtraArgument`, without assuming hardware LAPIC IDs are dense.
It never touches `GotoAddress` and never starts APs.
The dedicated stage-2 CI checks sequential patch application and compiles
the boot library and x64 HAL. A passing build is not a runtime SMP proof.

## Stage 3 — BSP GS and reserved AP stacks
`patches/cosmos-3094/0003-percpu-gs-and-ap-stacks.patch` initializes
an x64 BSP GS base before IRQ setup and adds a guard to return CPU0
before GS is initialized. Native assembly reserves 256 cache-line
CPU-local records and 256×16KiB AP stacks (4MiB BSS, no AP execution).
The BSP validates GS identity; ARM64 remains BSP-only.

This deliberately **does not** migrate interrupt context-switch scratch
state or enable AP IRQ/GC/scheduler activity: they need dedicated
multi-CPU auditing before the native AP bootstrap can be enabled.
Stage 3 CI compiles x64 Core/HAL, ARM64 Core/HAL, and checks native
assembly symbols after applying stages 1-3 sequentially.
Passing this CI is not a QEMU boot proof.

## Stage 4: native x64 AP entry / READY / park

Stage 4 ports the original 3.0.85 native AP bootstrap with a new patch based
on upstream v3.0.94 and the sequentially applied 1–3 series. The x64 HAL:

- Revalidates MP descriptors and dense CPU IDs.
- Checks 16 KiB per-AP stacks prepared in stage 3.
- Publishes native entry pointers for APs waiting in Limine, with preflight
  validation of all descriptors before any AP is released.
- Waits for the AP READY counters with **one bounded TSC deadline**.
- Checks observed LAPIC ID, GS CPU ID, stack alignment and interrupts disabled.

The AP assembly changes its own stack, initializes its own GS base, records
native state and parks in a CLI/HLT loop. No managed C#, heap allocation, GC
or scheduler work is allowed on APs yet. Boot deliberately fails closed if
all expected APs cannot be validated. The production system remains on the
patched Cosmos 3.0.85.

The CI validates sequential patch application and native exports and compiles
x64 Core/HAL plus ARM64 Core/HAL. **This is not a runtime QEMU AP-execution
proof**. Building a real v3.0.94 ISO and QEMU serial smoke logs is a further
required gate before we treat AP bootstrap as operational.

### Architecture boundary fix

The upstream Cosmos 3.0.94 `Cosmos.Kernel.HAL.X64` assembly enforces
`AllowUnsafeBlocks=false`. The Limine pointer traversal and native call
wrappers therefore belong in `Cosmos.Kernel.Core.X64/Cpu/SmpApBootstrap.cs`
(an assembly which explicitly permits unsafe code). The HAL initializer only
calls the Core.X64 helper through its established internal friend assembly
access. This preserves the HAL's safe-code rule rather than weakening it.

## Stage 5: actual native work command (not yet QEMU-proven)

After all APs report READY in Stage 4, the BSP sends command 1 to
each per-CPU cache-line mailbox. Each AP performs exactly 100,000
atomic increments of its own counter, publishes DONE, then parks with
interrupts disabled. The BSP verifies all DONE bits and exact counters
using one bounded TSC deadline. The mailbox is validated in a separate
preflight pass before any command is sent.

This is native code only, not C#/managed scheduling. Passing the new
stage-5 CI proves patch application, native assembly and x64/ARM64
compilation; **runtime AP execution requires real QEMU boot/serial logs**.
