# Networking → driver/hardware roadmap

This work is intentionally sequential. A stage may advance only after the exact PR-head SHA has green GitHub Actions validation and, for runtime stages, real QEMU proof. Compilation alone is not runtime proof.

## Phase 1 — Networking

| Stage | Goal | Completion gate | Status |
|---|---|---|---|
| N1 | x64 supported NIC + link | x64 ISO builds with networking enabled; QEMU supported NIC enumerates with MAC/link evidence; ARM64 regression remains safe | PASS — verified at `6db386000020a7e5e2652994af8dbfd74a6f4556`, Actions run #12 |
| N2 | Ethernet | frame TX/RX demonstrated in QEMU | PASS — verified at `3159db51e128310b9b0d14805223c7066cba6f7a`, Actions run #23 |
| N3 | ARP | request/reply and cache resolution demonstrated | PASS — verified at `566d5d932878ace911e8f002144d8ef4c8b07540`, Actions run #9; isolated x64 N3 ISO build and real QEMU ARP request/reply + resolution proof green |
| N4 | IPv4 | configured IPv4 packet TX/RX demonstrated | PASS — verified at `cb77d363aa4a28396759834ea06932138b8ec52a`, Actions run #2; isolated x64 N4 ISO build and real QEMU IPv4 TX/RX runtime proof green |
| N5 | ICMP | successful echo request/reply to controlled QEMU peer | PASS — verified at `4d55cb7beafa899ddf3ff66590485a98d2e75775`, Actions run #1; patched Cosmos preparation, isolated x64 N5 ISO build, real QEMU ICMP echo runtime proof and evidence upload green |
| N6 | UDP | datagram TX/RX to controlled peer | PASS — verified at `bb292d50e96303b3230c2fc729594ef31b2fe731`, Actions run #1; patched Cosmos preparation, isolated x64 N6 ISO build, real QEMU UDP TX/RX runtime proof and evidence upload green |
| N7 | DHCP | lease acquisition and applied network configuration | PASS — verified at `d6f84cdf34b03494788e956836b77afcefff2622`, Actions run #1; real QEMU DHCP DORA runtime proof green |
| N8 | DNS | hostname resolution through configured DNS server | IN PROGRESS |
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

N1 is complete and verified on exact SHA `6db386000020a7e5e2652994af8dbfd74a6f4556`. N2 is complete and verified on exact SHA `3159db51e128310b9b0d14805223c7066cba6f7a`. N3 is complete and verified on exact SHA `566d5d932878ace911e8f002144d8ef4c8b07540`. N4 is complete and verified on exact SHA `cb77d363aa4a28396759834ea06932138b8ec52a`. N5 is complete and verified on exact SHA `4d55cb7beafa899ddf3ff66590485a98d2e75775`. N6 is complete and verified on exact SHA `bb292d50e96303b3230c2fc729594ef31b2fe731`. N7 is complete and verified on exact SHA `d6f84cdf34b03494788e956836b77afcefff2622`: GitHub Actions `Network stage N7 DHCP` run #1 completed successfully, including the real QEMU DHCP DORA runtime gate. N8 DNS is now the only active networking stage. Its completion gate requires a real DNS query over the verified UDP path using the configured DNS server, a validated DNS response and resolved address in the guest, runtime evidence on the exact PR-head SHA, and green GitHub Actions. N9 remains blocked until N8 is green; Phase 2 remains blocked until N9.
