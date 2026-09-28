# Driver stage D2 — AHCI/NVMe stabilization

Status: IN PROGRESS — D2.1 PASS, D2.2 PASS, D2.3 AHCI reset + NVMe disable PASS; NVMe admin-queue setup NEXT

Depends on: D1 device/driver binding + PCI/PCIe, verified at `375300d01181170208b969e98f88bb06bb08628d` by `Driver stage D1 device model` run #75. ETAP 1 networking N1–N9 was completed before D2 work began and remains a prerequisite.

## Scope and strict order

1. D2.1 — controller discovery and binding — **PASS**
   - Bind only PCI mass-storage controllers identified as AHCI (class 0x01, subclass 0x06) or NVMe/NVM (class 0x01, subclass 0x08).
   - Preserve the architecture-neutral device model/HAL boundary from D1.
   - Validation: deterministic model tests plus real QEMU PCI discovery.
   - Verified on exact SHA `17875d40de5a12c210feecf2cd3570e4fce0cc89` by `Driver stage D2 AHCI NVMe` run #4 (`35880352923`), conclusion `success`.

2. D2.2 — BAR/MMIO validation and controller capability probing — **PASS**
   - AHCI: validated ABAR and real CAP/PI MMIO reads.
   - NVMe: validated BAR0/BAR1 decoding and real CAP/VS/CSTS MMIO reads.
   - Validation is fail-closed for invalid/all-ones MMIO values and is exercised in QEMU.

3. D2.3 — controller reset/initialization — **IN PROGRESS**
   - AHCI HBA reset (`GHC.HR`) with bounded wait and post-reset CAP validation — **PASS**, real QEMU proof.
   - NVMe disable (`CC.EN=0 -> CSTS.RDY=0`) with CAP.TO-derived bounded wait — **PASS**, real QEMU proof on exact SHA `fe569c62953e6d548ec52069d1054604484fa95c`, D2 run #54 (`35963914100`).
   - NVMe admin queue allocation/programming and re-enable (`AQA/ASQ/ACQ`, then `CC.EN=1 -> CSTS.RDY=1`) — **NEXT**.
   - Do not enable NVMe until valid DMA-backed admin queues are programmed.

4. D2.4 — identify/read path — **BLOCKED on D2.3**
   - AHCI: command list/FIS/command table DMA setup and IDENTIFY/read proof.
   - NVMe: Identify Controller/Namespace and one bounded read proof after D2.3 queue/init closure.
   - Validation: real QEMU disk data proof, not synthetic PASS markers.

5. D2.5 — integration/regression closure — **BLOCKED on D2.4**
   - Keep a safe fallback until both controller paths used by the supported QEMU configurations are stable.
   - x86_64: exact-SHA CI plus QEMU runtime proof.
   - ARM64: compile regression for architecture-neutral D2 code and runtime proof only where the current ARM64 Cosmos/QEMU path supports it.
   - D3 xHCI remains blocked until D2.1–D2.5 are green.

## D2.3 DMA decision

The earlier assumption that Cosmos 3.0.85 lacked a suitable physically-addressable page allocator was incorrect. The Cosmos kernel has `PageAllocator.AllocPages(PageType.Unmanaged, ..., zero: true)` plus `PageAllocator.VirtualToPhysical(...)`. Its own NVMe implementation uses exactly this pair for 4 KiB admin SQ/CQ pages before programming `AQA`, `ASQ`, and `ACQ`; AHCI and xHCI use the same allocator pattern. Therefore D2.3 must integrate this real unmanaged-page path rather than inventing a managed-array DMA substitute.

`PageAllocator` is internal to Cosmos.Kernel.Core, so the standalone ZonderqOS D2 runtime probe cannot directly call it from `Cosmos.Kernel.System`. The next implementation checkpoint must expose a narrow DMA-page abstraction at the Cosmos/HAL boundary (or reuse an existing public HAL DMA facade if available), preserving physical address + kernel virtual address and zeroed/page-aligned guarantees. The runtime test must prove queue addresses are page aligned and nonzero before writing NVMe registers. No `byte[]`, pinning assumption, arbitrary physical address, or fake PASS is acceptable.

## Completion criterion

D2 is complete only when AHCI/NVMe discovery, safe MMIO initialization and a real storage read path are verified in QEMU on the exact PR-head SHA, required GitHub Actions are green, and existing boot/storage/GUI behavior has no detected regression. A successful compile alone is insufficient.
