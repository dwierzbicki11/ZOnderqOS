# ZonderqOS Gen3 — hardware NIC inventory (Cosmos 3.0.95)

The production image still has **networking disabled**. An isolated QEMU E1000E probe has verified DHCP, ICMP, TCP, DNS and HTTP/1.0 (see `docs/3095-network-n1-qemu.md`), but this is not production or physical RTL810xE support.
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

The read-only inventory also prints `prog_if`, PCI `rev` and type-0 header `subsystem=VVVV:DDDD` (or `UNKNOWN` when not available). These hardware identifiers are needed to distinguish RTL810xE revisions before selecting a driver. Class and device ID are detected, not proof that a driver has bound. The
command always reports `driver=UNVERIFIED`, `link=UNKNOWN`,
`ip=UNASSIGNED` while the networking subsystem is disabled.
On ARM64 the existing PCI ECAM backend explicitly declines enumeration,
so N0 reports unavailable instead of inventing a NIC.

## Safely progressing toward real wired connectivity

1. **N0**: read-only PCI identification on the physical laptop and QEMU.
   This inventory covers source and compile gates, NOT a successful real-device scan.
2. **N1**: port a Cosmos **3.0.95** NIC driver/device binding for the
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

The feature-specific GitHub Actions workflows compile the actual C# sources
with stock Cosmos 3.0.95; the read-only PCI metadata changes still require physical laptop validation.
It does **not** validate real link, RX/TX or DHCP.

### RTL810xE PCI BAR read-only inventory (Cosmos 3.0.95)

`nicdiag` additionally reads the six type-0 PCI BAR registers **only** for a
confirmed `10EC:8136` Ethernet function on x64. It reports decoded I/O,
32-bit and 64-bit MMIO base addresses and the prefetchable bit. It never
writes PCI configuration space, probes BAR aperture sizes, maps MMIO, enables
bus mastering, DMA or IRQ, or binds a driver. BAR sizes remain `UNKNOWN`.
Unassigned, reserved and truncated BARs are not presented as usable.
ARM64 continues to report unavailable until its ECAM backend exists.
The `PciMetadataSmoke` C# harness tests synthetic BAR layouts; this does
**not** establish physical Realtek operation. Verify BAR data on the laptop
before selecting MMIO/IO and designing the RTL810xE TX/RX rings.

`nicdiag` also reports read-only PCI command/status flags and legacy IRQ
line/pin for `10EC:8136`. These flags describe PCI configuration **only**:
they do not prove interrupts are delivered, MSI/MSI-X works, DMA is safe,
or that any network driver is running. PCI revision `06` must not be
confused with the Realtek internal MAC version; chip-specific initialization
still requires a separate validated implementation.


### MSI-X memory BAR resolution (read-only diagnostic)

The RTL810xE PCI diagnostic now resolves the MSI-X table and pending-bit
array (PBA) offsets against assigned **memory** BAR metadata, if an MSI-X
capability exists. It rejects I/O BARs, unassigned BARs, the upper DWORD of
a 64-bit BAR, overflow of the **entire** table/PBA address ranges, and
overlapping table/PBA ranges. The table spans `16 * vectors` bytes and the
PBA spans `8 * ceil(vectors / 64)` bytes. A range ending exactly at the
largest 64-bit address is allowed; one extending past it is rejected. Malformed, unaligned PCI
capability pointers are rejected instead of silently rounded down.

`MSI-X_MEMORY_BARS=VALID` means only that PCI config-space metadata
yields plausible addresses. It does **not** verify the BAR aperture size,
map or read MMIO, enable MSI-X, deliver an interrupt, or initialize the
NIC. `INVALID_OR_UNASSIGNED` is diagnostic, not proof of faulty hardware;
firmware may not have assigned a resource. The C# tests use synthetic PCI
configuration fixtures and do not replace real laptop testing.

### RTL810xE station-address and PHY status decoder (offline only)

`Realtek810xERegisterDecoder.TryDecode` is now a **pure C# decoder** for
six station-address bytes (IDR0–IDR5, offsets `0x00..0x05`) and the
RTL810xE PHY status byte (offset `0x6C`). The status bit meanings follow
the Realtek 810x family layout also used by Linux's r8169 driver:
carrier `0x02`, 10 Mb/s `0x04`, 100 Mb/s `0x08`, full duplex `0x01`.
It rejects invalid/unicast-unsafe MACs and conflicting/unknown link-up
speed bits. A link-down snapshot reports speed 0 (unknown).

**Important:** No production path calls this decoder with real hardware
register values yet. It performs **no MMIO reads or writes** and cannot
establish the laptop's MAC, Ethernet carrier, DMA, IRQ or driver binding.
The decoder is covered by the real C# synthetic smoke harness; the
next step requires an isolated, revision-validated register reader and
actual laptop evidence before the values can be displayed as live data.
