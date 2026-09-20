# Networking → driver/hardware roadmap

This work is intentionally sequential. A stage may advance only after the exact PR-head SHA has green GitHub Actions validation and, for runtime stages, real QEMU proof. Compilation alone is not runtime proof.

## Phase 1 — Networking

| Stage | Goal | Completion gate | Status |
|---|---|---|---|
| N1 | x64 supported NIC + link | x64 ISO builds with networking enabled; QEMU supported NIC enumerates with MAC/link evidence; ARM64 regression remains safe | PASS — verified at `6db386000020a7e5e2652994af8dbfd74a6f4556`, Actions run #12 |
| N2 | Ethernet | frame TX/RX demonstrated in QEMU | PASS — verified at `3159db51e128310b9b0d14805223c7066cba6f7a`, Actions run #23 |
| N3 | ARP | request/reply and cache resolution demonstrated | IN PROGRESS |
| N4 | IPv4 | configured IPv4 packet TX/RX demonstrated | BLOCKED BY N3 |
| N5 | ICMP | successful echo request/reply to controlled QEMU peer | BLOCKED BY N4 |
| N6 | UDP | datagram TX/RX to controlled peer | BLOCKED BY N5 |
| N7 | DHCP | lease acquisition and applied network configuration | BLOCKED BY N6 |
| N8 | DNS | hostname resolution through configured DNS server | BLOCKED BY N7 |
| N9 | TCP | connection + payload TX/RX to controlled peer, with boot/storage/GUI regressions checked | BLOCKED BY N8 |

Phase 1 is complete only after N9 is green. Driver/hardware work must not start before then except dependency analysis required for the networking NIC.

## Phase 2 — Driver/hardware stack

| Stage | Goal | Completion gate | Status |
|---|---|---|---|
| D1 | device/driver binding + PCI/PCIe | deterministic enumeration/binding tests | BLOCKED BY N9 |
| D2 | AHCI/NVMe stabilization | storage runtime tests in QEMU | BLOCKED BY D1 |
| D3 | USB xHCI | controller init + transfer proof | BLOCKED BY D2 |
| D4 | HID | input device runtime proof | BLOCKED BY D3 |
| D5 | USB Mass Storage | enumerate, read/write test media safely | BLOCKED BY D4 |

## Current checkpoint

N1 is complete and verified on exact SHA `6db386000020a7e5e2652994af8dbfd74a6f4556`. N2 is complete and verified on exact SHA `3159db51e128310b9b0d14805223c7066cba6f7a`: GitHub Actions `Network stage N2 Ethernet TX/RX bring-up` run #23 completed successfully, including patched Cosmos preparation, isolated x64 N2 ISO build, real QEMU Ethernet TX/RX proof, and evidence upload. N3 ARP is now the only active networking stage. Its completion gate requires a real ARP request/reply exchange and verified cache resolution in QEMU on the exact PR-head SHA. N4 and all later protocol stages remain blocked until N3 is green; Phase 2 remains blocked until N9.