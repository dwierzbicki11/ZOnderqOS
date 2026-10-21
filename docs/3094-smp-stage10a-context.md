# ZonderqOS Cosmos 3.0.94 — Stage 10a: native AP context capture

This is a **real native interrupt-context snapshot**, but NOT full managed
stop-the-world, Thread.Start scheduling or OrionGC runtime registration.

## Mechanism

1. Stage 8 posts 4 F2 rendezvous generations with each AP in its own
   interrupt handler; the handler is running with interrupts disabled.
2. After the x64 CPU's same-CPL interrupt frame, the handler itself pushed
   RAX, RCX and RDX. It therefore obtains **interrupted** RSP as
   `RSP_current + 48`, interrupted RIP at `[RSP_current + 24]` and
   interrupted RFLAGS at `[RSP_current + 40]`.
3. It writes these three 64-bit values plus the actual F2 request epoch into
   a separate **64-byte cache-line record per logical AP**. An MFENCE and
   Stage8's atomic ARRIVED publication provide ordering.
4. The BSP, after Stage8's full arrive/release/exit proof and Stage9b timer
   delivery/STOP, verifies epoch 4 and the per-AP saved values. The original
   AP must have IF=1 when interrupted from its native STI/HLT park loop;
   the frame must have RFLAGS mandatory bit 1 and a nonzero valid-shaped
   64-bit RIP / 8-byte-aligned native RSP.
5. A strict parser verifies exactly one record per AP, stage-ordering, all
   CPU IDs and rejects forged, stale or missing captures. The previous
   Stage4–9b validators still execute in the same guest run.

## ABI

Only Cosmos **3.0.94** Core.X64 / HAL.X64 / native x64 assembly is changed,
through `tools/port-cosmos-3094-stage10a.py`, whose source anchors MUST
all match before any Cosmos file is written. No old 3.0.85 patch is replayed.

Native slot:
`_smp10_native_context[cpuId]` 64 bytes:
`+0 RSP, +8 RIP, +16 RFLAGS, +24 F2 epoch`.
Slots are read from BSP **only after Stage8 has released all APs**.

## Remaining full SMT blockers

- Stage10b: a production stop-the-world barrier must interrupt APs while
  **ordinary managed C# code** is running, not merely the native park loop.
- OrionGC must enumerate and pin correct registered managed stack/roots,
  preserve register TLS and runtime thread state during STW, then resume
  every CPU with a guaranteed abort-release path.
- Thread.Start dispatch + timer preemption + runnable contexts, load
  balancing and migration must be validated with repeated GC pressure on
  1/2/4/8/16/32/56 CPUs (including physical Broadwell).

**Stage10a PASS is not equivalent to GC-safe full SMT.** Do not deploy
this experiment as a production managed multi-core scheduler.
