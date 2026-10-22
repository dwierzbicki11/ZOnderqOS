# Full managed SMT for ZonderqOS / Cosmos 3.0.94

**Target**: `System.Threading.Thread` executing on every online logical CPU,
not merely a parked AP, native checksum, IPI or timer diagnostic. Keep the
3.0.85 production series separate: `patches/cosmos-smt/*` is NOT compatible
with Cosmos 3.0.94 by simple replay.

## Port order and fail-closed acceptance

1. **Stage 9b**: PR #167 (separate) adds AP-local periodic F3 IRQ delivery,
   per-CPU counters and STOP/quiet proof. Do not enable managed AP work yet.
2. **Stage 10**: native rendezvous to capture an AP's saved RSP/RIP/RFLAGS
   with bounded stop, release and abort. Preserve interrupt flags and
   prevent APs being stranded after timeout.
3. **Stage 11**: real OrionGC stop-the-world handshake. The GC must know
   every managed CPU's thread stack, safepoint, root set and current
   scheduler context. The native parked-AP proof is not a GC root map.
4. **Stage 12–15**: managed reverse P/Invoke entry and per-CPU runtime
   registration; verify AP residency, stack scanning and idle stack
   enrollment without allocations in unmanaged interrupt context.
5. **Stage 16–18**: production `Thread.Start` queue dispatch to APs,
   AP-local preemption and concurrent execution, with no global scheduler
   lock held across GC rendezvous.
6. **Stage 19–21**: thread migration, live handoff and bounded automatic
   load balancing, preserving TLS, GS, thread identity and stack maps.
7. **Stage 22–23**: 1/2/4/8/16/32/56 logical CPU matrix, Broadwell
   2C/4T, repeated GC pressure, forced stop/release failures, thread
   creation/exit, preemption and actual per-CPU accounting for `htop`.
   Only then update production 3.0.94 version pins together.

## Required Stage 23 serial proof

The strict parser `tools/verify-cosmos-3094-managed-smp-contract.py`
requires `[SMP3094-STAGE9B] PASS`, then
`[SMP3094-ORIONGC-STW] PASS`, then one
`[SMP3094-MANAGED] cpu=... thread=... dispatch=... gc_ack=... gc_resume=... preempt=...`
record per logical CPU, then `[SMP3094-MANAGED-STAGE23] PASS`.
Missing, duplicate, unpreempted, GC-incomplete, or forged native-only
proof is rejected. The parser's synthetic self-tests do **not** establish
that managed SMP is implemented; it must validate actual QEMU serial.

**Acceptance status:** Do not treat passing synthetic verifier self-tests, native AP
rendezvous, or timer proofs as evidence that production `Thread.Start`, OrionGC
stop-the-world, and managed preemption are certified on Cosmos 3.0.94. The
full real-QEMU and physical CPU matrix remains a separate hard gate.
