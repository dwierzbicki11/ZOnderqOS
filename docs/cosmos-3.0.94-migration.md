# Cosmos Gen3 upgrade: v3.0.85 (patched SMP) → v3.0.94

## Current state — 2026-10-14

ZonderqOS Stage 23 production managed-thread scheduling and real htop CPU
accounting are implemented as 23 local patches against **Cosmos v3.0.85**.
The QEMU test matrix previously passed with 1, 2, 4, 8 and 16 logical CPUs.

On 2026-10-14, commit `27527ea` updated only the SDK and two package
references to v3.0.94. The launcher, patches, local NuGet feed and Actions
workflows stayed on v3.0.85. Both Stage 22 and 23 failed with
`MSB4236: Cosmos.Sdk/3.0.94 could not be found`: the local NuGet feed
contains Cosmos.Sdk v3.0.85 and package source mapping excludes nuget.org
for Cosmos SDK packages.

**Recovery:** keep the three project references at v3.0.85 until the patch
series is ported. `tools/verify-cosmos-compat.sh` rejects accidental drift.
This is *not* a claim that v3.0.94 has been integrated.

## Upstream features to assess for v3.0.94

- v3.0.88: VirtIO GPU, experimental ext2, x64 FPU boot fix.
- v3.0.89: USB xHCI hubs, mass storage and HID boot keyboard.
- v3.0.90: kernel driver kit, Intel HD Audio, GC/runtime fixes, CSPRNG,
  TCP/e1000 improvements, per-console sessions and alpha compositing.
- v3.0.91: AMD DCN 3.1.5 display, USB HID mouse, socket fixes.
- v3.0.92: Intel integrated graphics, keyboard ring buffer,
  immediate switch when a thread blocks/sleeps.
- v3.0.93: editor/IDE installer changes.
- v3.0.94: no merged pull requests over v3.0.93.

These capabilities exist upstream; do not claim that they work inside
ZonderqOS before testing them with the local scheduler and graphics stack.

## Migration acceptance gates

1. Create a dedicated upgrade branch starting from current `main`.
   Recommended naming: `smp/cosmos-3094-stageX`.
2. Checkout Cosmos v3.0.94 and inspect changed scheduler/GC/HAL,
   NativeAOT exports and drivers before applying *any* local patches.
3. Rebase or replace the Stage 23 patch series in small increments.
   Treat already-upstream behavior as a candidate for removal only
   after equivalence tests; never drop managed CPU placement or idle
   accounting merely to make the patches apply.
4. Build **matching v3.0.94 local NuGet packages** for both architectures.
   Update SDK references, launchers, local cache paths and BOTH stage
   workflow checkouts together. Modify the version guard as part of
   this reviewed release change.
5. Pass patch apply/idempotence, x64 NativeAOT ISO build, ARM64 managed
   compile, and real Stage 22/23 QEMU regressions for 1, 2, 4, 8 and
   16 logical CPUs. Require full CPU mask, scheduler/GC stress, AP idle,
   real ProcessManager htop telemetry and cleanup; never synthesize PASS.
6. Run boot/login, automatic GUI/fallback, storage, shell, package and
   services smoke tests against actual release artifacts before merging.

Do not auto-merge if any required CI test is red or missing.

## Branch and release-line isolation

- Keep `main` pinned to v3.0.85 while migration work is in progress.
- Run migration validation only from migration branches; do not partially switch
  SDK/package pins in `main`.
- Merge to `main` only as one reviewed release-line switch after Stage 22/23 and
  functional regression gates are fully green on the exact migration revision.
