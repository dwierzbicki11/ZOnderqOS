# Driver stage D2 — AHCI/NVMe stabilization

Status: IN PROGRESS — D2.1 VERIFIED, D2.2 BLOCKED pending closure-checkpoint CI

Depends on: D1 device/driver binding + PCI/PCIe, verified at `375300d01181170208b969e98f88bb06bb08628d` by `Driver stage D1 device model` run #75.

## Scope and strict order

1. D2.1 — controller discovery and binding — **PASS**
   - Bind only PCI mass-storage controllers identified as AHCI (class 0x01, subclass 0x06) or NVMe/NVM (class 0x01, subclass 0x08).
   - Preserve the architecture-neutral device model/HAL boundary from D1.
   - Validation: deterministic model tests plus real QEMU PCI discovery with `ich9-ahci` and `nvme` devices.
   - Verified on exact SHA `17875d40de5a12c210feecf2cd3570e4fce0cc89` by `Driver stage D2 AHCI NVMe` run #4 (`35880352923`), conclusion `success`. D1 regression workflow run #82 on the same SHA also passed.

2. D2.2 — BAR/MMIO validation and controller capability probing — **NEXT / BLOCKED until the D2.1 closure checkpoint is green**
   - AHCI: validate ABAR before MMIO access and read controller capability/implemented-port registers without issuing I/O.
   - NVMe: validate BAR0/BAR1 MMIO mapping and read CAP/VS/CSTS without enabling queues.
   - Validation: QEMU runtime proof; invalid/missing BARs must fail closed.

3. D2.3 — controller reset/initialization
   - AHCI: controlled HBA/port initialization with bounded waits/timeouts.
   - NVMe: disable/enable sequence with bounded CSTS waits and page-size/capability checks.
   - Validation: QEMU runtime proof with no boot/storage/GUI regression.

4. D2.4 — DMA/queue setup and identify/read path
   - AHCI: command list/FIS/command table DMA setup and IDENTIFY/read proof.
   - NVMe: admin queues, Identify Controller/Namespace and one bounded read proof.
   - DMA/MMIO helpers may use ASM/C/C++ when this is safer than managed code.
   - Validation: real QEMU disk data proof, not synthetic PASS markers.

5. D2.5 — integration/regression closure
   - Keep a safe fallback until both controller paths used by the supported QEMU configurations are stable.
   - x86_64: exact-SHA CI plus QEMU runtime proof.
   - ARM64: compile regression for architecture-neutral D2 code and runtime proof only where the current ARM64 Cosmos/QEMU path supports it.
   - D3 xHCI remains blocked until D2.1–D2.5 are green.

## Completion criterion

D2 is complete only when AHCI/NVMe discovery, safe MMIO initialization and a real storage read path are verified in QEMU on the exact PR-head SHA, required GitHub Actions are green, and existing boot/storage/GUI behavior has no detected regression. A successful compile alone is insufficient.

## Repository audit / verified progress

The existing QEMU launcher already knows how to attach `ich9-ahci` and `nvme` devices, so D2 reuses those device models for runtime validation rather than inventing a synthetic hardware environment. D2.1 controller classification/binding and its dedicated CI/runtime gate are now verified. D2.2 must not start until this documentation closure checkpoint itself has green exact-SHA CI, preserving the stage-order rule.
