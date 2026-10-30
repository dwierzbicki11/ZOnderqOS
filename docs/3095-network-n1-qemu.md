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
operation**. Link proof does not imply DMA TX/RX, DHCP, IPv4 or Internet.
Do not merge the probe into production until the real QEMU gate passes.
