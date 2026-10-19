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
