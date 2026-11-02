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

The N1 probe also requires one live stock DriverKit `E1000EDriver`
bound to `pci:0000:00:02.0`, with exactly one published device, no
recorded handler faults, and nonzero PCI resource/interrupt-source counts.
It samples DriverKit ownership before and after the raw TX queue call and
rejects a changed resource/interrupt-source inventory. The N1 serial
validator requires this checkpoint in the correct order. These counts
prove only registration, **not guest ISR execution**.

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

## N6: HTTP/1.0 GET over real TCP (verified in QEMU)

Build `bash tools/build-cosmos-3095-local.sh network-n6`, then:

```sh
bash tools/smoke-network-n1-3095-qemu.sh \
  output-3095/ZonderqOS-3095-network-n6.iso \
  network-n6-3095.serial.log n6
```

N6 keeps N1–N5 intact, then makes a second TCP connection to
`10.0.2.2:18096`. A CI-owned unprivileged HTTP fixture binds only
`127.0.0.1:18096` on the QEMU host and responds to exactly
`GET /health HTTP/1.0` with `200 OK`, a 4116-byte body and a
`Content-Length` header. The guest checks the exact HTTP status,
headers and body. The N6 PCAP gate independently checks the TCP handshake,
port/MAC/IP/sequence/ack continuity and full HTTP request/response byte
stream, including a possible segmented response. The gate additionally
requires a matching server FIN after the complete HTTP response and a
subsequent guest ACK for that FIN, correlated to the same TCP sequence
space. This tests observed TCP teardown, not TLS or IRQ delivery. The
smoke script also requires the host success marker and eight seconds of
guest heartbeat.

**Confirmed:** GitHub Actions run **#54** on 2026-10-30 built the exact stock Cosmos 3.0.95 N6 ISO and passed the real QEMU HTTP/1.0 PCAP gate, the host fixture, the N1–N5 prerequisites and the eight-second guest heartbeat window. The evidence was uploaded as separate serial, PCAP and host logs.
HTTP/1.0 here is unencrypted; it is **not HTTPS**, certificate validation,
a browser or a production network stack. `CosmosEnableNetwork=false`
continues to be the production default, and the probe never touches host
storage or the ZONDERQDATA partition.


## N5/N6: explicit ARP resolution evidence (gate hardening)

The N5 DNS gate now requires an Ethernet ARP request from the guest
`10.0.2.15` for the QEMU DNS proxy `10.0.2.3`, followed by a
matching ARP reply addressed to the guest MAC, **before** the DNS UDP
query/answer. The DNS request's Ethernet destination must also match
the MAC learned from that ARP reply. The gate validates ARP hardware/
protocol types, sender and target MAC/IP pairs, opcode and capture
ordering; missing or mismatched pairs fail closed. N6 inherits the N5
ARP prerequisite.

This change was motivated by inspection of **actual** N5 and N6 PCAP
artifacts from the previous successful QEMU workflow #59: both captures
contain the guest ARP request followed by the QEMU DNS proxy reply.
That historical capture confirms the ARP frames existed; the stricter
new gate must pass its own current CI run before claiming the updated
end-to-end proof. Synthetic positive/negative ARP fixtures remain parser
tests, not hardware or network evidence. Physical RTL810xE is untested.

## N6 repeated TCP connection / RX-ring reuse probe (2026-10-31)

The isolated N6 guest now performs **two separate TCP connections** to the
same loopback-only HTTP fixture. Each connection receives the exact 4116-byte
body, verifies the complete response and closes. The host accepts exactly two
requests. The PCAP validator requires two distinct client source ports, each
with a full handshake, contiguous multi-segment response, and server FIN
acknowledged by the guest. The second handshake must occur after the first
server FIN/ACK. This is a bounded RX descriptor and socket lifecycle test,
not a direct proof that hardware IRQs fired; stock Cosmos may still poll.
It remains an isolated QEMU-only diagnostic, never production GUI networking.

**QEMU evidence (2026-10-31):** GitHub Actions run #41201638372 (https://github.com/dwierzbicki11/ZOnderqOS/actions/runs/41201638372) passed the N6 boot/PCAP step for the two-connection version at commit `df0612ff`. This is a QEMU result only; it does not establish E1000E IRQ delivery or physical Realtek functionality.

## E1000E IRQ emission tracing (experimental, 2026-10-31)

N6 CI now runs the isolated QEMU guest with `NET3095_IRQ_TRACE=1`.
The smoke runner checks that QEMU exposes the three trace events
`e1000e_irq_legacy_notify`, `e1000e_irq_msi_notify`, and
`e1000e_irq_msix_notify_vec`, enables them and preserves their output in
`network-n6-3095.serial.log.stderr` alongside the PCAP and guest serial log.

`tests/verify_3095_e1000e_irq_trace.py` classifies real QEMU-side
notifications as `EMITTED` or `NOT_OBSERVED`, with separate legacy/MSI/MSI-X
counts. As of the N6 gate hardening, `--require-notify` is mandatory
for the isolated N6 CI run: **zero device-side notifications fails the N6
smoke gate** even if packet delivery succeeded through polling. N1–N5
still establish their separate Ethernet/TCP evidence. The parser's
`--self-test` uses synthetic fixtures and is **not** an emulator or
guest test.

**Measured baseline (31 Oct 2026):** real QEMU N6 Actions run
[#41210672507](https://github.com/dwierzbicki11/ZOnderqOS/actions/runs/41210672507)
at `c9957ee4` emitted `legacy=24 msi=0 msix=0` in the device trace
while the N1–N6 PCAP/serial gates passed. This historical measurement
motivates the new fail-closed assertion; it does **not** prove guest ISR
execution, and the new commit must pass its own CI run before its stricter
N6 gate can be called verified.

The N6 gate additionally checks the **guest's own serial record** with
`verify_3095_e1000e_irq_binding.py`: exactly one E1000E driver bound to
the same PCI node that reported an IRQ line, and the published ready
adapter's MAC matching the driver's MAC. It rejects missing line, an
unbound driver, inconsistent node/MAC and ambiguous duplicate markers.
The historical N6 serial artifact from #41210672507 contains
`E1000EDriver: ... line 11, polling every 10 ms` and a matching
`52:54:00:12:34:56` adapter. This is a **driver IRQ registration**
observation, not evidence of execution of the registered handler.
The stronger combined guest-binding/QEMU-emission gate needs its own
new QEMU run to be considered verified.

**Crucial limitation:** QEMU raising a legacy interrupt or sending MSI/MSI-X
does **not** prove that Cosmos 3.0.95 delivered it to a guest ISR, that
the E1000E ISR ran, or that RX completed without polling. The next step
requires independently observable guest-side IRQ counters or an explicitly
IRQ-only RX test, before making any interrupt-driven-driver claim.
Production networking remains disabled; no PCI/IRQ/DMA configuration is
changed by this diagnostic trace.

## N6 ordered raw Ethernet TX burst (experimental, 2026-10-31)

After the two verified HTTP/1.0 RX sessions, the isolated N6 kernel queues
**128 paced, uniquely tagged broadcast Ethernet frames** cycling through 60, 128, 512, 1024 and 1514 bytes (excluding FCS) with
experimental EtherType `0x88B5` and payloads
`ZONDERQ3095-BURST-000` through `ZONDERQ3095-BURST-127`.
The 5 ms inter-frame sleep avoids treating a full software queue as a
DMA failure and exercises repeated submissions. This code is behind
`ZONDERQ_NETWORK_N6_HTTP`; N1–N5 and the disk-backed production GUI do not
run it. The burst is not a replacement for a real RX or ISR test.

`tests/verify_3095_network_n6_burst.py` first requires the independent
N6 HTTP/PCAP proof, then checks that the guest emitted exactly one
post-HTTP burst-start and burst-queued marker. It locates the second HTTP
server FIN acknowledged by the guest in the actual PCAP and requires all
128 unique raw Ethernet frames **after** that packet, in order, with the
expected source MAC, broadcast destination, EtherType, exact length and every deterministic payload byte.
A queue-success serial marker without the frames fails closed.
Synthetic parser negatives cover missing, reordered, corrupted and
misaddressed frames. After the burst, the guest performs **four sequential ICMP echo request/reply
cycles** with identifier 0x3096 and distinct sequences 2–5. The N6 PCAP
gate requires all four ordered, checksum-valid, 32-byte payload-matching
round trips after the final burst frame, with exact peer MAC/IP pairs.
Duplicate, reordered or missing replies fail. It also requires the initial
N1 raw TX frame exactly once before the HTTP completion. These checks
exercise sustained RX after TX stress, but still do not prove guest ISR
execution. The runner waits for this PCAP proof *before*
starting its eight-second managed guest heartbeat window.

The DHCP DORA PCAP gate now additionally correlates the Ethernet
source MAC of OFFER and ACK, rejects multicast/zero server MACs, and
checks the destination MACs of client and server packets. The IRQ gate
now rejects unassigned PCI legacy line zero and requires a real QEMU
**legacy** E1000E assertion in addition to the guest driver binding;
an MSI-only trace cannot satisfy a legacy-line proof. None of these
gates demonstrates execution of the guest ISR.

**Verification status:** source and Python self-test changes are committed
to `feat/3094-qemu-e1000e-link`; a full new exact-stock-3.0.95
build and QEMU PCAP run are required before declaring the 128-frame
burst, tightened DHCP L2 proof or combined legacy IRQ gate PASS.
Historical N1–N6 QEMU evidence does not cover these new checks.

## N6 post-burst TCP RX stress (2026-10-31)

The isolated N6 probe additionally attempts a **third** 4116-byte HTTP
response after all 128 diagnostic Ethernet TX frames and four ICMP
request/reply pairs. It reuses the same Cosmos 3.0.95 TCP and E1000E stack,
but opens a new connection. The host fixture now accepts three independent
connections. The new `verify_3095_network_n6_post_http.py` gate requires a
third complete TCP handshake, exact multi-segment HTTP response and peer
FIN/ACK **strictly after the fourth ICMP reply** in QEMU's captured PCAP;
its source port must differ from the first two. This tests RX/TCP reuse
after TX-ring stress. A serial PASS alone is insufficient.

The new probe is isolated to the N6 QEMU profile. It does not modify
production networking, the stock SDK, or the disk-backed GUI. CI/QEMU
must pass on the new commit before this extension is considered proven.

The isolated N6 guest also re-reads the primary E1000E adapter after the
third HTTP session, rejecting lost readiness, dropped link or changed MAC.
The independent post-HTTP validator correlates the final serial MAC with
the original ready adapter. This is a read-only status observation, not
an IRQ/ISR proof or a physical Realtek test.


## N6 four post-burst HTTP RX sessions (experimental, 2026-10-31)

The isolated N6 guest now opens **four independent HTTP/1.0 TCP
connections after** the 128-frame raw Ethernet TX burst and four ICMP
echo round-trips (attempts 3–6; six HTTP connections in total). Each
connection requests a 4116-byte test body from the loopback-only
CI HTTP fixture, verifies the entire response in the guest, and closes
its TCP socket. Each connection now uses a deterministic **byte-varying
4 KiB body**, with distinct content at every offset between connections
and nonuniform content inside each 1 KiB block. This detects both stale
blocks from earlier sessions and one-byte shifts *within* a descriptor
that the former uniform A/B/C/D blocks could conceal. The host and guest
compute the same bounded ASCII pattern independently; the PCAP validator
checks the complete byte stream. The fixture emits six distinct host
completion markers.

The post-burst PCAP validator requires **four distinct client source
ports**, ordered TCP handshakes, complete segmented HTTP RX payloads,
and server FIN acknowledgments, all after the final post-burst ICMP
reply. The serial gate also requires all four ordered guest completion
markers and a final unchanged MAC/link snapshot. The QEMU smoke runner
checks each corresponding host marker. The isolated guest additionally
reports per-session `HTTP_RX_STATS` (successful socket Read() calls,
largest returned chunk and total bytes). The post-HTTP validator
requires six ordered, internally consistent telemetry records; these
counters measure guest-side managed reads, **not** guest ISR execution.
The independent PCAP remains mandatory for real TX/RX proof.

This tests additional repeated RX/TCP teardown under the existing stock
Cosmos 3.0.95 E1000E driver and remains opt-in N6-only. It is **not**
proof of guest ISR execution, physical RTL810xE operation, TLS/HTTPS,
or production desktop networking. New code requires fresh CI/QEMU
evidence before claiming success; older three-session evidence does not
validate the six-session extension.

## N6 stock DriverKit post-stress health snapshot (2026-11-01)

After all six independently checked HTTP sessions, 128 raw Ethernet TX
frames and four ICMP round trips, the isolated N6 guest now inspects
**public Cosmos 3.0.95** `DriverDiagnostics.TryGetNode` snapshots. It
requires exactly one bound `E1000EDriver` on the expected QEMU PCI
function, one published network device, nonzero held resources and
interrupt sources, and **zero recorded interrupt-handler exceptions**.
The post-stress MAC/link check must pass first. The read-only marker
`DRIVER_HEALTH=PASS` is required by the N6 smoke gate even without
QEMU IRQ tracing; the IRQ-trace gate additionally correlates it with the
real guest driver-binding log and device-side legacy IRQ assertions.
The Python verifier rejects missing, duplicated, reordered, mismatched
or malformed health markers, including changed PCI node or MAC.

This adds a genuine guest-side **binding/health observation**, not a
guest ISR invocation count. In stock Cosmos 3.0.95 the
`E1000EState.InterruptCount` property exists but its declaring type
is internal to the upstream driver assembly. The public
`DeviceNodeInfo.InterruptCount` is the **number of available IRQ
sources**, not how often the E1000E handler executed. A zero
`FaultCount` proves only that no handler exception was recorded.
The driver still polls every 10 ms, so QEMU RX and legacy IRQ emission
cannot establish interrupt-driven delivery. No SDK changes or
production NIC activation are involved. The new gate is considered
unverified until its own current QEMU workflow finishes.

## N6 final ICMP after all HTTP socket teardowns (2026-11-01)

The isolated N6 guest now sends one additional IPv4 ICMP echo
(`id=0x3097, seq=1`) **after** the sixth independent HTTP/1.0
connection has completed and its socket has been disposed. This is
separate from the four echoes sent immediately after the 128-frame TX
burst. A real QEMU `filter-dump` PCAP must show exactly one final
request and matching reply, with valid IPv4/ICMP checksums, a 32-byte
echoed payload, the same Ethernet peer and an index strictly after
the sixth TCP FIN acknowledgment. Guest serial markers must occur
after the sixth HTTP completion and before the final link/IPv4 health
snapshot. Missing, duplicated, damaged, out-of-order or wrong-peer
evidence fails the new `verify_3095_network_n6_final_icmp.py` gate.

This extends RX liveness coverage across repeated TCP teardown and
reopening. It does **not** demonstrate ISR invocation (stock E1000E
still polls), physical Realtek RTL810xE support, HTTPS, or production
GUI networking. The opt-in probe performs no disk operations. A
synthetic validator self-test is not a substitute for a successful
real QEMU PCAP run on the current commit.

## N6 DriverKit and lease checks around each post-stress ICMP (2026-11-01)

The isolated N6 probe now samples the **public read-only** Cosmos 3.0.95
DriverKit resource/IRQ-source counts and the original DHCP lease, MAC and
link before and after each of the four post-burst ICMP echo exchanges.
It also samples before and after the final ICMP echo, after all six HTTP
TCP connections have closed: **10 checkpoints total**. These catch
transient binding or lease changes that a single final snapshot misses.

The serial validator requires each of the ten checkpoints exactly once,
with matching PCI/IRQ ownership, ordered phases and ICMP sequences.
It rejects missing, duplicate, reordered or altered records. The
independent PCAP validators remain mandatory to prove actual packets
crossed the emulated E1000E interface. A checkpoint is only a snapshot;
it is **not proof of DMA completion or ISR execution**, and the stock
E1000E driver still polls. This diagnostic code remains isolated from
the disk-backed production GUI, with no storage writes and no SDK
downgrade. The new checks require a fresh passing QEMU run on their
commit before any runtime success claim.

## N6 live NIC/lease checkpoints during TX burst (2026-11-01)

The isolated E1000E probe now verifies the **original DHCP address, mask,
gateway, MAC and link** alongside DriverKit resource/IRQ-source ownership at
five 32-descriptor TX-ring checkpoints (0, 32, 64, 96 and 128 frames).
The guest emits `TX_NIC_CHECK=PASS sent=N` only after a successful read-only
check. This detects transient lease/link changes that a single post-burst
snapshot could miss. The independent QEMU PCAP proof is still mandatory:
neither guest checkpoints nor successful queueing prove wire transmission,
ISR execution or DMA completion. The opt-in probe never touches storage or
activates production GUI networking.

## Two successive QMP link-down/up cycles (2026-11-02)

The opt-in N6 QEMU probe now re-arms after its first verified link recovery.
With `NET3095_LINK_CYCLE=1 NET3095_LINK_CYCLES=2`, the host drives a
**second** independent QMP down/up transition without rebooting the kernel.
The guest must observe both transitions, preserve its original MAC, DHCP
address/mask/gateway and DriverKit resource/IRQ-source ownership, and send
an ICMP echo with a new identifier and sequence (`0x3099/2`). The first
recovery uses `0x3098/1`. A fail-closed PCAP validator requires exactly
one valid request/reply pair for each identifier, in the correct order,
with matching IP/MAC, payload and checksums. A serial-only PASS or a QMP
acknowledgment does not satisfy the gate.

The single-cycle mode remains supported for local diagnostics. The
production GUI remains offline, the stock Cosmos SDK/Kernel stays at
3.0.95, and the probe performs no storage I/O. This test exercises
repeated E1000E link recovery in **emulated QEMU**, not physical RTL810xE
or a confirmed guest ISR. A successful previous single-cycle run
(#41383999788) does not validate this new two-cycle code; only the
current workflow's real QEMU result can do that.


## N6 TCP reconnect after two QMP link recoveries (2026-11-02)

The isolated N6 guest now establishes a **new TCP connection after each**
of two QMP-driven E1000E link down/up cycles, following the corresponding
ICMP echo. Each connection sends a cycle-specific request and verifies a
different exact reply from the loopback-only host fixture. The independent
`verify_3095_network_n6_tcp_recovery.py` PCAP gate requires fresh ordered
SYN/SYN-ACK/ACK handshakes and both exact request/reply byte streams after
the respective link recovery. Serial markers or QMP acceptance alone are
not proof of recovered TCP connectivity.

**Observed real QEMU evidence:** GitHub Actions run
[#41394844044](https://github.com/dwierzbicki11/ZOnderqOS/actions/runs/41394844044)
passed its N6 two-cycle link/TCP step on stock Cosmos 3.0.95. The logs
include `[NET3095-N6-TCP-RECOVERY-GATE] PASS cycles=2` and
`[NET3095-N6-LINK-QEMU] PASS QMP link cycles=2`. This confirms
recovered TCP RX/TX in the emulated E1000E PCAP, **not** guest ISR
execution, physical Realtek, HTTPS, or production desktop networking.

That overall workflow nevertheless ended **FAILURE** in a later,
independent **RTL810xE negative diagnostic**: the screen-mode QEMU
serial output had a complete scheduler tick inserted inside the exact
`[NET3095-RTL810XE] BOOT` marker. The previous fail-closed validator
only normalized the same known splice inside the decline marker.
Commit `bc7e682` extends exact-marker reconstruction to BOOT and
adds positive and negative synthetic split-position cases. The
downloaded failure artifact was checked locally against the new
reconstruction, including all six RTL negative serial logs. This is
a validator fix, **not** new physical RTL810xE evidence; the full CI
rerun on that commit must still pass before the regression is closed.

## E1000E BAR and legacy INTx route validation (2026-11-02)

The last **completed** pre-change baseline was GitHub Actions
[#41446116539](https://github.com/dwierzbicki11/ZOnderqOS/actions/runs/41446116539)
at commit `fe5e36d`: the real isolated QEMU workflow passed its N1–N6
Ethernet/PCAP and two-cycle link recovery jobs on stock Cosmos 3.0.95.
It also passed the negative Realtek probe boots. This baseline is not
evidence that the subsequent code changes already passed QEMU.

The read-only `E1000EPciIdentity` decoder now rejects the unassigned
32-bit memory BAR pattern `0xFFFFFFF0`, in addition to zero and the
all-ones 64-bit BAR. A new `TryReadLegacyIrqRoute` check validates the
isolated E1000E's PCI INTx command bit, legacy interrupt line (1–254)
and pin (INTA#–INTD#). The N1 guest samples the route before and after
raw TX; N6 rechecks it after the HTTP/TX/ICMP workload and during both
QMP link recoveries. Synthetic C# tests exercise missing/invalid route
metadata, disabled INTx and malformed BARs; the N1 serial parser checks
the new route markers for consistency, duplication and ordering.

**Important:** A valid PCI line/pin and QEMU device-side IRQ emission
are only routing/notification evidence. They do **not** demonstrate
that the Cosmos guest executed an E1000E ISR or that RX is IRQ-driven;
the stock driver also polls. The new checks require a **fresh successful
real QEMU workflow at the new branch head** before reporting an end-to-end
PASS. Production networking remains disabled, the GUI uses its original
disk-backed root, and the probe never writes to ZONDERQDATA or host disks.
