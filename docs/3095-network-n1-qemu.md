# Network N1 (Cosmos Gen3 3.0.95) — isolated QEMU proof

This is an **opt-in, x64-only diagnostic ISO**. Normal ZonderqOS remains
`CosmosEnableNetwork=false`, uses the stock 3.0.95 runtime, and still
requires an approved FAT32 ZONDERQDATA volume for its full GUI.

`bash tools/build-cosmos-3095-local.sh network-n1` builds the dedicated
`ZonderqOS-3095-network-n1.iso` from exact-tag upstream packages.
The probe kernel never mounts a disk, initializes users or starts the GUI.

`bash tools/smoke-network-n1-3095-qemu.sh output-3095/ZonderqOS-3095-network-n1.iso network-n1-3095.serial.log`
boots QEMU q35 with a single vCPU and explicit Intel 82574L E1000E.
Only a real guest serial log with initialized NetworkManager, a registered
`e1000e` interface, nonzero unicast MAC, ready state and link up, followed
by eight seconds of guest survival, may satisfy the runtime gate.

The parser self-test and static source contract **do not prove real NIC
operation**. After reporting ready/link, the isolated probe sends one 60-byte broadcast
Ethernet frame with experimental EtherType 0x88B5 and payload
`ZONDERQ3095TX`. The QEMU smoke script attaches `filter-dump` to the
explicit E1000E netdev and accepts success only if its real PCAP contains
that exact frame from the MAC reported by the guest, followed by eight
seconds of guest survival. A successful `NetworkManager.Send` alone is
only a queue acknowledgement, **not** physical TX evidence.

This does not prove receive, DHCP, IPv4, Internet or hardware RTL810xE.
Do not merge the probe into production until the real QEMU gate passes.

## N4: bounded TCP echo over QEMU E1000E (experimental)

`bash tools/build-cosmos-3095-local.sh network-n4` builds a fourth,
isolated, x64-only profile using the exact stock v3.0.95 tag. It inherits
N1 raw TX, N2 DHCP and N3 ICMP gates and adds a bounded TCP client.

```sh
bash tools/smoke-network-n1-3095-qemu.sh \
  output-3095/ZonderqOS-3095-network-n4.iso \
  network-n4-3095.serial.log n4
```

The smoke runner launches an unprivileged Python listener on **host loopback
127.0.0.1:18095**. QEMU user networking makes it reachable from the guest
as **10.0.2.2:18095**. The guest uses stock plugged .NET `TcpClient` to
exchange `ZONDERQ3095-TCP-PING\\n` / `ZONDERQ3095-TCP-PONG\\n`.
The fail-closed validator requires the actual QEMU PCAP to contain a
correlated three-way TCP handshake and the exact bidirectional payloads,
plus the guest success marker and host listener's receipt. An extra eight
seconds of live QEMU is required. Python synthetic fixtures are parser
regression tests only, never proof of TCP connectivity.

The host listener exits after one request. No host disk or partition is
mounted or modified by the guest. This **does not** prove HTTP, DNS, TLS,
the physical Realtek RTL810xE driver, or production networking. Never enable
this profile in the disk-backed GUI without independent integration tests.
