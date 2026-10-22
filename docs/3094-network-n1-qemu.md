# ZonderqOS — Cosmos 3.0.94 QEMU E1000E NIC/link proof (N1)

This is an **isolated test image**, not a change that turns networking on
in the normal ZonderqOS desktop or on the physical Broadwell laptop.

The upstream code and API are pinned to `CosmosOS/Cosmos` tag
`v3.0.94`: `NetworkManager.IsInitialized`, `DeviceCount`,
`GetAdapter`, `NetworkAdapter.Ready`, `LinkUp`, and `MacAddress`.

## Build and run

Use the existing provenance-checked local 3.0.94 builder:

```bash
cd ~/Pulpit/ZOnderqOS
git switch feat/3094-qemu-e1000e-link
bash tools/build-cosmos-3094-local.sh network-n1
bash tools/smoke-network-n1-3094-qemu.sh \
  output-3094/ZonderqOS-3094-network-n1.iso \
  output-3094/network-n1.serial.log
```

The builder creates a private staged checkout and a private, version-matched
Cosmos package feed. The project sets
`ZonderqNetworkN1Probe=true` **only** for this isolated x64 build, selects
`ZonderqOS.NetworkN1ProbeKernel` and enables networking **only** there.
The ordinary `main` profile retains
`<CosmosEnableNetwork>false</CosmosEnableNetwork>`.
This kernel does not mount any filesystem, initialize users or start GUI.

QEMU gets one explicit emulated Intel E1000E on a user-mode NAT backend.
A strict parser rejects missing MAC, link-down, not-ready, empty device table,
panics, duplicate success, and claims of completed DHCP or SMP. Successful
proof must come from **real guest serial output** and survive eight seconds.
A successful compile or parser self-test by itself is never marked N1 PASS.

## Scope, limitations and next step

N1 is MAC/link detection for QEMU E1000E only. It does NOT prove
Ethernet packet TX/RX, an address assigned by DHCP, ICMP, TCP, DNS or internet
connectivity. Those are later stages and need their own real QEMU tests.

The laptop's 10EC:8136 RTL810xE needs a separate driver; E1000E will
not bind Realtek hardware. RTL8723BE Wi-Fi is also a separate project.

The historical branch
`archive/branch-prune-20261022/network/stage1-nic-link` used Cosmos
**3.0.85**. Its historical results do not certify this new 3.0.94 runtime.
