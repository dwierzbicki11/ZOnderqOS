# Networking → driver/hardware roadmap

This work is intentionally sequential. A stage may advance only after the exact PR-head SHA has green GitHub Actions validation and, for runtime stages, real QEMU proof. Compilation alone is not runtime proof.

## Phase 1 — Networking

| Stage | Goal | Completion gate | Status |
|---|---|---|---|
| N1 | x64 supported NIC + link | x64 ISO builds with networking enabled; QEMU supported NIC enumerates with MAC/link evidence; ARM64 regression remains safe | IN PROGRESS |
| N2 | Ethernet | frame TX/RX demonstrated in QEMU | BLOCKED BY N1 |
| N3 | ARP | request/reply and cache resolution demonstrated | BLOCKED BY N2 |
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

PR #66 started N1 by enabling Cosmos networking only for x64 while leaving ARM64 networking disabled. The first CI attempt for SHA `84d5ebb69b56cfe980ef5eeaa7eeb2d98454573b` failed while building the x64 ISO because `CmdPing.cs` referenced the obsolete/nonexistent `Cosmos.Kernel.System.Network.IPv4.EndPoint`. QEMU was therefore correctly skipped. The follow-up fixes the alias to `Cosmos.Kernel.System.Network.EndPoint` and initializes the receive endpoint with `Address4.Zero`, matching the Cosmos networking API. N1 remains IN PROGRESS until CI and QEMU runtime evidence are green.