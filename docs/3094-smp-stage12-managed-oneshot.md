# Cosmos 3.0.94 — Stage 12 isolated managed C# AP one-shot

This Stage 12 candidate is a deliberate **proof of a single allocation-free
managed function executing on an application processor**, not production
managed SMP. The native interrupt and AP are still fully isolated from the
normal ZonderqOS GUI, storage and scheduler.

## Delivery

- Stage1–9b and Stage10a proofs run first on the patched 3.0.94 runtime.
- The BSP dispatches command 3 by **physical LAPIC F1 IPI** through the
  source-guarded Stage9b controller. No timer is rearmed on this path.
- AP F1 ISR acknowledges the signal but **does not call managed code in an
  interrupt**. The AP resumes its native STI/HLT park loop, disables
  interrupts and invokes `__cosmos_smp3094_managed_ap_entry` once using
  its own 16KiB native stack.
- The C# `ManagedApNative.Execute()` entry checks `CpuLocal.CurrentCpuId`,
  computes 1024 rounds of deterministic work with no heap allocations, and
  publishes its checksum via an architecture-native bridge. Return goes
  directly back to the AP native park code.
- BSP requires an expected checksum, a completion state and one serial line
  per AP, then emits `[SMP3094-STAGE12] PASS`. The serial parser rejects
  missing, duplicate, forged, wrong checksum and out-of-order evidence.

## IMPORTANT safety boundary

**Not** an ordinary System.Threading.Thread or long-lived managed AP stack.
There is **no managed AP scheduler residency**, **no AP GC stack enrollment**,
**no concurrent OrionGC** collection, no context migration, no preemption
during this function and no automatic load balancing.

Cosmos 3.0.94 `GarbageCollector.Collect()` currently masks interrupts on
the **current CPU only**. Enabling arbitrary managed C# work concurrently
with it across all APs would be unsafe. Therefore the test runs solely
during isolated hardware initialization while the BSP waits, does not
allocate on the AP, and fails the kernel boot probe if native transition
or per-CPU checksum validation fails.

Production managed SMP must still implement and verify global STW,
safepoints and GC root enumeration, then the complete per-CPU scheduler
and native preemption handoff.
