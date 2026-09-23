# Networking → driver/hardware roadmap

This work is intentionally sequential. A stage may advance only after the exact PR-head SHA has green GitHub Actions validation and, for runtime stages, real QEMU proof. Compilation alone is not runtime proof.

## Phase 1 — Networking

| Stage | Goal | Completion gate | Status |
|---|---|---|---|
| N1 | x64 supported NIC + link | x64 ISO builds with networking enabled; QEMU supported NIC enumerates with MAC/link evidence; ARM64 regression remains safe | PASS |
| N2 | Ethernet | frame TX/RX demonstrated in QEMU | PASS |
| N3 | ARP | request/reply and cache resolution demonstrated | PASS |
| N4 | IPv4 | configured IPv4 packet TX/RX demonstrated | PASS |
| N5 | ICMP | successful echo request/reply to controlled QEMU peer | PASS |
| N6 | UDP | datagram TX/RX to controlled peer | PASS |
| N7 | DHCP | lease acquisition and applied network configuration | PASS |
| N8 | DNS | hostname resolution through configured DNS server | PASS |
| N9 | TCP | connection + payload TX/RX to controlled peer, with boot/storage/GUI regressions checked | PASS — exact PR-head `80fdb88eff80974ceba54cf39391356fc4bbe4df`; TCP + boot/storage + sustained GUI QEMU proof green |

Phase 1 is COMPLETE. It was revalidated after the documentation checkpoint `e269d32857499abbbde816df2df13351fd671ff2`: N1–N9 GitHub Actions were all green, including N9 run #15.

## Phase 2 — Driver/hardware stack

| Stage | Goal | Completion gate | Status |
|---|---|---|---|
| D1 | device/driver binding + PCI/PCIe | deterministic enumeration/binding tests, x86_64 QEMU PCI runtime proof, ARM64 compile regression, exact-SHA green CI | PASS — exact SHA `74df4906cc4b2fb6195dd611cc86a0539e7eceb0`; D1 workflow run #73 green, including real x86_64 QEMU PCI proof and ARM64 production-model compile regression |
| D2 | AHCI/NVMe stabilization | storage runtime tests in QEMU | NEXT — unblocked after D1; implementation must begin only after the D1 documentation checkpoint is itself green |
| D3 | USB xHCI | controller init + transfer proof | BLOCKED BY D2 |
| D4 | HID | input device runtime proof | BLOCKED BY D3 |
| D5 | USB Mass Storage | enumerate, read/write test media safely | BLOCKED BY D4 |

## D1 implementation order

1. System-facing device identity/descriptor and deterministic driver registry, independent of Cosmos PCI details.
2. PCI/PCIe HAL adapter that translates discovered functions into `DeviceDescriptor` values; no UI/storage coupling.
3. Deterministic enumeration ordering and duplicate-address rejection.
4. Binding tests covering specific-before-generic driver selection, failed-bind fallback, and idempotent rebind.
5. x86_64 QEMU PCI enumeration/binding runtime proof using known emulated devices.
6. ARM64 compile regression (and boot proof where current toolchain supports it).
7. Exact PR-head GitHub Actions must be green before D1 is marked PASS and D2 starts.

## Current checkpoint

D1 is verified COMPLETE on exact SHA `74df4906cc4b2fb6195dd611cc86a0539e7eceb0`. GitHub Actions `Driver stage D1 device model` run #73 completed successfully on that SHA. The run includes the deterministic device/PCI model checks, real x86_64 CF8/CFC-backed QEMU PCI enumeration proof, and ARM64 compile-regression of the architecture-neutral production hardware model. The x86-only CF8/CFC accessor remains covered by the x86_64 runtime gate rather than being forced into ARM64.

This documentation update is the D1 closure checkpoint. D2 AHCI/NVMe remains untouched in this commit and may start only after exact-SHA CI for this checkpoint is green, preserving the strict stage ordering.
