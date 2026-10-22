# ZonderqOS Gen3 — network bring-up, hardware inventory (Cosmos 3.0.94)

The production image still has **no working NIC driver or IP stack**.
`<CosmosEnableNetwork>false</CosmosEnableNetwork>` remains unchanged.
A PCI network function is NOT a bound NIC, connected Ethernet cable, DHCP lease,
working Wi-Fi association or operational network stack.

## Stage N0 — read-only PCI NIC identification

From the installed full GUI terminal or text shell, run:

```text
nicdiag
lspci -v
```

For a **diskless diagnostic ISO** built with
`-p:ZonderqReadOnlyHardwareProbe=true`, use `nicdiag` at `input-diag>`.
It does not require `ZONDERQDATA` or initialize users/VFS.

This command queries the existing architecture-aware `PciSysfsProvider`,
then prints actual PCI class 02 network functions and hardware IDs:
- Laptop Realtek **10EC:8136** RTL810xE/RTL810x-series wired controller.
- Laptop Realtek **10EC:B723** RTL8723BE wireless controller.
- QEMU example **8086:100E** E1000 and **8086:10D3** E1000E.

Class and device ID are detected, not proof that a driver has bound. The
command always reports `driver=UNVERIFIED`, `link=UNKNOWN`,
`ip=UNASSIGNED` while the networking subsystem is disabled.
On ARM64 the existing PCI ECAM backend explicitly declines enumeration,
so N0 reports unavailable instead of inventing a NIC.

## Safely progressing toward real wired connectivity

1. **N0**: read-only PCI identification on the physical laptop and QEMU.
   This PR covers source and compile gates, NOT a successful real-device scan.
2. **N1**: port a Cosmos **3.0.94** NIC driver/device binding for the
   actual **10EC:8136**, checking BAR mapping, bus-master DMA ownership,
   IRQ, descriptor-ring lifetimes, link state, RX/TX, and cleanup.
   Isolate driver bring-up in QEMU before enabling it on real hardware.
3. **N2**: Ethernet TX/RX including strict buffer length and CRC/ownership
   boundaries, ARP and IPv4.
4. **N3**: DHCP Discover/Offer/Request/ACK, lease validation and renew,
   gateway/subnet/DNS state, ICMP and UDP; only then report a configured IP.
5. **N4**: DNS/TCP, package downloads and browser connectivity.
   RTL8723BE Wi-Fi needs its **own** driver, PHY/firmware and security path.

Archived `archive/branch-prune-20261022/network/stage1-nic-link`
contains isolated N1–N9 QEMU experiments, some with old kernel/project
assumptions. Never merge it wholesale into production or mark its QEMU
claims as new 3.0.94 physical-hardware proof. Port and re-test intentionally.

## Gates

```sh
bash tools/verify-cosmos-compat.sh
python3 tests/test_3094_nic_pci_inventory.py
python3 tests/test_3094_storage_policy.py
```

The feature-specific GitHub Actions workflow compiles the actual C# sources
with Cosmos 3.0.94 for x64 and ARM64, including the read-only probe path.
It does **not** validate real link, RX/TX or DHCP.
