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
