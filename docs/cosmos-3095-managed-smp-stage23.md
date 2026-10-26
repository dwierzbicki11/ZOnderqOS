# Managed SMP Stage 23 on Cosmos v3.0.95 — engineering contract

**Status: implementation BLOCKED, not PASS.** This branch is based on
`feat/cosmos-3095-migration`; it does not modify `main`.

## Observed real KVM evidence (2026-10-24)

User booted `ZonderqOS-3095-usb.iso` with QEMU
`-accel kvm -cpu host,+topoext -smp sockets=1,cores=4,threads=2`.
The guest reported `[ACPI] Found 8 CPU(s)` and
`[SCHED] Detected 8 CPU(s)`, then allocated idle thread entries 0..7.
No AP-start, native AP execution, managed worker residency, GC stop-world,
or distributed scheduler evidence was present. This is **NOT** SMP Stage 23.

## Strict requirements for an isolated runtime build

1. Checkout exactly `CosmosOS/Cosmos v3.0.95` with checked tag identity.
   Apply ONLY manually reviewed patches rebased against that source.
   Do not apply old 3.0.85 or 3.0.94 diffs blindly, and do not mix NuGet packages.
2. Native boot chain: validate Limine MP response and xAPIC IDs, establish
   per-AP stack, GS/TLS, interrupt table and idle context; prove all APs enter
   and leave a native rendezvous with bounded timeout and abort release.
3. Native per-AP timer and IPI proof. Every AP must acknowledge real timer
   interrupts and survive repeated native barrier/timeout tests.
4. Scheduler: install truly per-CPU current-thread state, locks, ready queues,
   and context-switch handling. Prove AP threads start and exit, with no
   double scheduling, use-after-free or BSP-only fallback.
5. Runtime/OrionGC: patch barriers/safepoints/stack scanning to stop/release
   **every active managed CPU** during collections; verify failed CPU stop
   has bounded fail-closed behavior. Test allocations under concurrent load.
6. Managed `Thread.Start()` worker test on 1/2/4/8/16+ vCPUs, logging the
   *executing physical logical CPU ID* from a trusted runtime query.
   A worker's intended target CPU or scheduler queue ID is not proof.
7. Preserve production disk/EFI safety, GUI, USB and x64/ARM64 behavior.
   Production network must remain off until independently proven.

## Evidence format for a dedicated instrumented QEMU build

```text
[SMP3095-STAGE23] START cpus=2 rounds=1000
[SMP3095-STAGE23] WORKER cpu=0 rounds=1000 checksum=00000001
[SMP3095-STAGE23] WORKER cpu=1 rounds=1000 checksum=00000002
[SMP3095-STAGE23] PASS cpus=2 rounds=1000
```

Run `python3 tests/verify_3095_managed_smp.py --self-test` for synthetic
negative tests. Actual validation:

```bash
python3 tests/verify_3095_managed_smp.py /path/to/real-qemu-serial.log --cpus 8
```

**Caution:** the parser only checks log shape and consistency. Real runtime
proof additionally requires review of instrumented kernel code, true current
CPU reads, timer/preemption/GC traces and provenance of raw guest serial logs.
It never upgrades a synthetic `--self-test` to an actual SMP pass.


## Stage 2: real dense logical CPU mapping (experimental)

Stage1 QEMU passed on the user's machine for **1 and 8 CPUs** (5s main
loop stability in both cases). Those actual serial logs showed
`AP_NOT_STARTED`; this is not an AP execution proof.

Stage2 adds `LimineMp.TryAssignDenseX64CpuIds` to a separate
**v3.0.95-only, QEMU-only** source overlay. The BSP always gets logical
ID 0 and valid AP descriptors get consecutive IDs 1..N-1. A complete
descriptor preflight refuses any already-published `GotoAddress`
before making changes. No AP is started or managed code executed on APs.

### Build and run

```bash
cd /mnt/Magazyn/ZOnderqOS
git pull --ff-only
ZONDERQ_3095_NATIVE_STAGE1=true ZONDERQ_3095_NATIVE_STAGE2=true \
    bash tools/build-cosmos-3095-local.sh probe
ZONDERQ_3095_REQUIRE_DENSE=true \
    bash tools/smoke-smp-3095-stage1-qemu.sh \
    output-3095/ZonderqOS-3095-smp-stage2-probe.iso \
    smp3095-stage2-8cpu.log 8
```

Stage1 and Stage2 have different Cosmos clones, package caches and ISO
filenames. The separate `cosmos-3095-smp-stage2.yml` workflow checks
real serial evidence at 1/8 CPU, not synthetic results alone. This still
**does not** validate GS, AP stacks, native AP entry, IPI, timer ISR,
managed Thread.Start(), preemptive scheduling or stop-the-world GC.

## Stage 3: x64 BSP GS identity and reserved AP stacks (experimental)

The isolated `tools/port-cosmos-3095-stage3.py` builds from exact
Cosmos v3.0.95, with the Stage1/Stage2 overlays composed from the
pristine tag, rather than replaying older patch files. It adds a
native `_native_x64_init_percpu` entry and `CpuLocal.CurrentCpuId`
for the **BSP only**, plus 256 reserved 16 KiB AP stacks.

The diagnostic `[SMP3095-STAGE3] BSP_GS=0 cpus=N
AP_STACKS_RESERVED AP_NOT_STARTED` proves BSP GS and stack-reservation
consistency, **not AP bootstrap, real per-AP stack use, or managed
Thread.Start scheduling**. Never treat Stage3 as Stage23. Hardware
testing must wait for AP bootstrap and full runtime validation.

```bash
cd /mnt/Magazyn/ZOnderqOS
git pull --ff-only
ZONDERQ_3095_NATIVE_STAGE1=true \
ZONDERQ_3095_NATIVE_STAGE2=true \
ZONDERQ_3095_NATIVE_STAGE3=true \
  bash tools/build-cosmos-3095-local.sh probe

ZONDERQ_3095_REQUIRE_GS=true \
  bash tools/smoke-smp-3095-stage1-qemu.sh \
  output-3095/ZonderqOS-3095-smp-stage3-probe.iso \
  smp3095-stage3-8cpu.log 8
```

Stage3 uses a separate isolated Cosmos workspace, NuGet package feed
and ISO; when the previously built Stage1 runtime clone contains the
exact tag's pinned runtime Git commit, it shares its Git object storage
to avoid re-downloading dotnet/runtime. This dependency is checked,
never assumed. Stage1 and Stage2 images are not overwritten.


## Build cache: avoid redundant Cosmos runtime downloads

Keep `ZONDERQ_3095_WORK_DIR` stable across local builds. The builder retains
the exact-tag Cosmos checkout, the pinned `dotnet/runtime` Git objects, and
the locally packaged SDK. Stage2/Stage3 borrow the Stage1 runtime Git objects
when available; their **patched package feeds remain separate**.

The **project NuGet package cache** is now preserved when the exact-tag
package stamp and all required packages are unchanged. If upstream packages
are rebuilt (same version but different overlay), the private project cache
is discarded before restoring to prevent stale NativeAOT/Cosmos binaries.
The per-package `.nupkg.metadata` source check still rejects nonlocal Cosmos
packages. No system-wide NuGet cache, production `main` packages or installed
SDKs are deleted.

To validate cache behavior without a Cosmos rebuild:

```bash
python3 tests/test_3095_project_nuget_cache.py
```

A fresh GitHub-hosted CI runner has no retained local workspace by default,
so separate workflows can still each download the upstream runtime. This
local-build optimization does not assert cross-run CI cache persistence.


## Stage 4: native AP entry and parked rendezvous (experimental, QEMU ONLY)

`ZONDERQ_3095_NATIVE_STAGE4=true` requires Stages 1–3 to be explicitly
enabled. Stage4 is an independent 3.0.95 exact-tag Cosmos overlay. Its
native x64 Limine `GotoAddress` entry obtains the already-validated dense
logical CPU ID from `ExtraArgument`, switches to the reserved 16 KiB AP
stack, installs its private GS base, publishes a per-CPU ready flag, and
parks the AP with interrupts disabled (`cli; hlt`).

The BSP checks **each** AP readiness flag by logical CPU ID, rather than
inferring progress from the ACPI count. A missing AP causes a bounded
rendezvous failure and a diagnostic halt. A Stage4 parser must see the
actual guest serial marker
`[SMP3095-STAGE4] cpus=8 native_ap_online=7 AP_PARKED MANAGED_OFF`
plus Stage1/2/3 proof and stable QEMU main-loop execution.

This does **not** install an AP IDT or per-AP timers/IPIs, does not run the
scheduler on APs, does not implement OrionGC stop-the-world, and must
**never** be interpreted as Stage23 managed SMP or production readiness.
This native rendezvous has not yet been accepted until the dedicated
real-QEMU CI run is green. Never flash this experimental ISO onto a
physical disk or merge Stage4 into `main` without later runtime evidence.

```bash
cd /mnt/Magazyn/ZOnderqOS
git switch feat/cosmos-3095-managed-smp-stage23
git pull --ff-only
python3 tests/verify_3095_native_stage1.py --self-test

ZONDERQ_3095_NATIVE_STAGE1=true \
ZONDERQ_3095_NATIVE_STAGE2=true \
ZONDERQ_3095_NATIVE_STAGE3=true \
ZONDERQ_3095_NATIVE_STAGE4=true \
  bash tools/build-cosmos-3095-local.sh probe

ZONDERQ_3095_REQUIRE_NATIVE_AP=true \
  bash tools/smoke-smp-3095-stage1-qemu.sh \
    output-3095/ZonderqOS-3095-smp-stage4-probe.iso \
    smp3095-stage4-8cpu.log 8
```
