# Experimental OrionGC collection probe: QEMU only

**Never use this experiment on the ASUS or production USB.**
This branch is based on `feat/memory-optimizer-10s` and is not intended
to be merged to the production `main`.

## Motivation

Stock Cosmos Gen3 OrionGC (single generation, nonmoving mark/sweep)
normally expands its heap before collecting. A long-running system may
therefore show zero collections and growing used-page counters even
when short-lived objects have become unreachable.

The production optimizer only drops cold cache references and never
forces GC. That is intentional: earlier forced GC in the GUI/input
workload was associated with fatal #PF/#GP.

## Safety contract

This build adds a one-shot, explicit-root-only `gcprobe` command behind
`ZonderqGcProbe=true`. Its build guard demands **x64 + RAM-root GUI**;
the compile flag defaults to `false`. Normal builds do not register or
compile the command. Still, the build guard cannot distinguish physical
hardware from QEMU: **execute only in QEMU**.

Example **configuration parameters**, not a production flash command:

```sh
# Use a disposable QEMU run with private RAM-root/disk and serial output.
# Preserve your existing stock-3.0.95 build settings and add:
-p:ZonderqRamRoot=true -p:ZonderqGcProbe=true
```

After booting the *diagnostic ISO* into QEMU and authenticating as root:

```
gcprobe collect --accept-qemu-crash
```

The command cannot be repeated in the same boot. If it hangs or faults,
record the full COM1 output and do not enable it in the normal kernel.

## Verification

The COM1 log includes `[GC_PROBE] stage=before` and
`[GC_PROBE] stage=after` with `used_p`, `heap_kb`,
`committed_kb`, and `collections`. Run 60-minute baseline QEMU
soak with 1 CPU and 8 vCPU separately, with and without the probe.
Check that an actual `GC` collection incremented its counter, that
`used_p` or GC committed pages decreased, and that the next 60 minutes
remain stable. A collection may free objects while retaining segments,
so **zero decrease in used pages is not automatically failure**.

The probe will likely be unsafe on real hardware until scheduling,
stack scanning, and interrupt interactions are thoroughly verified.
It does not change stock Cosmos SDK or the production Memory Optimizer.
