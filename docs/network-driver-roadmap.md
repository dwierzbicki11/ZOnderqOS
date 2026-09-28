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
| N7 | DHCP | lease acquisition and applied network configuration | PASS — revalidated at `80fdb88eff80974ceba54cf39391356fc4bbe4df`, Actions run #27; real QEMU DHCP DORA gate green |
| N8 | DNS | hostname resolution through configured DNS server | PASS — revalidated at `80fdb88eff80974ceba54cf39391356fc4bbe4df`, Actions run #24; real DNS QEMU runtime gate green |
| N9 | TCP | connection + payload TX/RX to controlled peer, with boot/storage/GUI regressions checked | PASS — verified at exact PR-head `80fdb88eff80974ceba54cf39391356fc4bbe4df`, Actions run #14; isolated x64 N9 ISO + real QEMU TCP runtime proof, normal x64 boot/storage regression proof, GUI regression ISO, and sustained GUI render-loop QEMU proof all green |

Phase 1 is COMPLETE at exact PR-head SHA `80fdb88eff80974ceba54cf39391356fc4bbe4df`. All N1–N9 workflows on that SHA completed successfully, and N9 specifically verified TCP runtime plus normal boot/storage and sustained GUI render-loop regressions. Phase 2 may now begin, strictly from D1.

## Phase 2 — Driver/hardware stack

| Stage | Goal | Completion gate | Status |
|---|---|---|---|
| D1 | device/driver binding + PCI/PCIe | deterministic enumeration/binding tests plus required exact-SHA CI; runtime proof where hardware interaction is exercised | READY — Phase 1 gate satisfied; next stage |
| D2 | AHCI/NVMe stabilization | storage runtime tests in QEMU | BLOCKED BY D1 |
| D3 | USB xHCI | controller init + transfer proof | BLOCKED BY D2 |
| D4 | HID | input device runtime proof | BLOCKED BY D3 |
| D5 | USB Mass Storage | enumerate, read/write test media safely | BLOCKED BY D4 |

## Current checkpoint

Networking Phase 1 is closed by `Network stage N9 TCP` run #14 on SHA `80fdb88eff80974ceba54cf39391356fc4bbe4df`. The N9 job passed the real TCP QEMU proof, normal boot/storage-init regression proof, and sustained GUI render-loop regression proof; the other N1–N8 workflows on the same SHA are also green. The next permitted implementation stage is D1 — device/driver binding + PCI/PCIe. D2–D5 remain blocked until their immediate predecessor has its required green CI/runtime evidence.