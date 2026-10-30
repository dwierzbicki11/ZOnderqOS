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

## Verified QEMU milestones (stock Cosmos 3.0.95)

The isolated CI gate has established N1 raw Ethernet TX (PCAP), N2 DHCP
DISCOVER/OFFER/REQUEST/ACK and lease 10.0.2.15, N3 ICMP echo round-trip to
10.0.2.2, N4 TCP handshake plus echo, and N5 DNS UDP A query/response
for `localhost` via QEMU's 10.0.2.3 resolver. All five stages passed
the **real QEMU** job in GitHub Actions run **#44**, commit `49bad210`
(on 2026-10-30). The capture, serial log and host TCP evidence are separate
from the synthetic parser fixtures. The last eight-second window also
requires six advancing guest heartbeats; QEMU process survival alone is
insufficient. These are emulator-only results, not a physical NIC test.

## N6: HTTP/1.0 GET over real TCP (experimental, pending CI)

Build `bash tools/build-cosmos-3095-local.sh network-n6`, then:

```sh
bash tools/smoke-network-n1-3095-qemu.sh \
  output-3095/ZonderqOS-3095-network-n6.iso \
  network-n6-3095.serial.log n6
```

N6 keeps N1–N5 intact, then makes a second TCP connection to
`10.0.2.2:18096`. A CI-owned unprivileged HTTP fixture binds only
`127.0.0.1:18096` on the QEMU host and responds to exactly
`GET /health HTTP/1.0` with `200 OK`, a 20-byte body and a
`Content-Length` header. The guest checks the exact HTTP status,
headers and body. The N6 PCAP gate independently checks the TCP handshake,
port/MAC/IP/sequence/ack continuity and full HTTP request/response byte
stream, including a possible segmented response. The smoke script also
requires the host success marker and eight seconds of guest heartbeat.

**Do not call N6 passed until its new real QEMU CI job succeeds.**
HTTP/1.0 here is unencrypted; it is **not HTTPS**, certificate validation,
a browser or a production network stack. `CosmosEnableNetwork=false`
continues to be the production default, and the probe never touches host
storage or the ZONDERQDATA partition.
