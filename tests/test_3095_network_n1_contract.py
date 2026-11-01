#!/usr/bin/env python3
"""Source-only, fail-closed regression checks for the opt-in 3.0.95 E1000E probe."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
PROJECT = (ROOT / "ZonderqOS.csproj").read_text()
SOURCE = (ROOT / "source/Core/NetworkN1ProbeKernel.cs").read_text()
BUILD = (ROOT / "tools/build-cosmos-3095-local.sh").read_text()
SMOKE = (ROOT / "tools/smoke-network-n1-3095-qemu.sh").read_text()
HOST = (ROOT / "tests/network_3095_tcp_host.py").read_text()
HTTP_HOST = (ROOT / "tests/network_3095_http_host.py").read_text()
HTTP_VALIDATOR = (ROOT / "tests/verify_3095_network_n6.py").read_text()
MAIN = (ROOT / "Kernel.cs").read_text()

class NetworkN1Isolation(unittest.TestCase):
    def test_n4_tcp_echo_total_rx_deadline(self):
        # The 21-byte echo must not take 21 * ReceiveTimeout seconds
        # when a malicious peer trickles one byte per successful read.
        n4 = SOURCE.split("#if ZONDERQ_NETWORK_N4_TCP", 1)[1].split(
            "#if ZONDERQ_NETWORK_N5_DNS", 1)[0]
        self.assertIn("long echoStart = Stopwatch.GetTimestamp();", n4)
        self.assertIn("long echoFrequency = Stopwatch.Frequency;", n4)
        self.assertIn("echoFrequency <= 0 || echoStart <= 0", n4)
        self.assertEqual(n4.count("echoElapsed / echoFrequency >= 20"), 2)
        self.assertEqual(n4.count("TCP echo total deadline exceeded"), 2)
        before = n4.index("while (received < response.Length)")
        first = n4.index("echoElapsed / echoFrequency >= 20", before)
        read = n4.index("int read = stream.Read(", before)
        second = n4.index("echoElapsed / echoFrequency >= 20", read)
        completed = n4.index("if (received != expected.Length)", read)
        self.assertLess(first, read)
        self.assertLess(read, second)
        self.assertLess(second, completed)
        self.assertIn("client.Client.ReceiveTimeout = 5000;", n4)

    def test_n6_http_total_rx_deadline(self):
        # Socket ReceiveTimeout is per read, not a complete-stream limit.
        # The N6 opt-in guest must reject a trickle peer even when every
        # individual read succeeds within five seconds.
        method = SOURCE.split("private static bool TryHttpExchange(", 1)[1]
        self.assertIn("using System.Diagnostics;", SOURCE)
        self.assertIn("long rxStart = Stopwatch.GetTimestamp();", method)
        self.assertIn("long rxFrequency = Stopwatch.Frequency;", method)
        self.assertIn("rxFrequency <= 0 || rxStart <= 0", method)
        self.assertEqual(method.count("rxElapsed / rxFrequency >= 45"), 2)
        self.assertEqual(method.count("HTTP RX total deadline exceeded"), 2)
        before = method.index("while (received < response.Length)")
        guard1 = method.index("rxElapsed / rxFrequency >= 45", before)
        read = method.index("int read = stream.Read(", before)
        guard2 = method.index("rxElapsed / rxFrequency >= 45", read)
        check = method.index("if (!IsE1000EDriverKitStable(", read)
        self.assertLess(guard1, read)
        self.assertLess(read, guard2)
        self.assertLess(guard2, check)
        self.assertIn("http.Client.ReceiveTimeout = 5000;", method)
        self.assertIn("if (readCalls >= 64)", method)

    def test_n6_http_rechecks_live_lease_before_stream_pass(self):
        # Per-chunk checks alone miss link/lease churn after the final Read.
        # This is a source contract; only QEMU PCAP and guest logs prove RX.
        method = SOURCE.split("private static bool TryHttpExchange(", 1)[1]
        final_bytes = method.index("if (received != expected.Length)")
        exact_payload = method.index("if (response[i] != expected[i])", final_bytes)
        final_driver = method.index(
            'HTTP DriverKit ownership changed during attempt=', exact_payload)
        final_lease = method.index(
            'HTTP NIC/lease changed at completion attempt=', final_driver)
        driver_pass = method.index('HTTP_DRIVER_STABLE=PASS attempt=', final_lease)
        nic_pass = method.index('HTTP_NIC_STABLE=PASS attempt=', driver_pass)
        self.assertLess(exact_payload, final_driver)
        self.assertLess(final_driver, final_lease)
        self.assertLess(final_lease, driver_pass)
        self.assertLess(driver_pass, nic_pass)
        guard = method[final_driver:final_lease]
        self.assertIn(
            "IsE1000ELeaseStable(expectedMac, expectedIp, expectedMask, expectedGateway)",
            guard)

    def test_n4_n6_blocking_reads_use_remaining_monotonic_budget(self):
        # A post-read deadline check rejects late success, but a fixed 5s
        # blocking timeout can still make the probe finish up to 5s late.
        # Both opt-in paths must bound each Read to the remaining budget.
        helper = SOURCE.split("private static int RemainingSocketReadTimeoutMs(", 1)[1]
        helper = helper.split("#endif", 1)[0]
        self.assertIn("long seconds = elapsedTicks / frequency;", helper)
        self.assertIn("long fractionalTicks = elapsedTicks % frequency;", helper)
        self.assertIn("long remainingMs = (long)deadlineSeconds * 1000 - elapsedMs;", helper)
        self.assertIn("if (remainingMs <= 1)", helper)
        self.assertIn("return 1;", helper)  # Socket timeout 0 means infinite.
        self.assertIn("remainingMs < 5000 ? remainingMs : 5000", helper)
        n4 = SOURCE.split("#if ZONDERQ_NETWORK_N4_TCP", 1)[1].split(
            "#if ZONDERQ_NETWORK_N5_DNS", 1)[0]
        self.assertIn("client.Client.ReceiveTimeout = RemainingSocketReadTimeoutMs(\n"
                      "                                echoElapsed, echoFrequency, 20);", n4)
        n6 = SOURCE.split("private static bool TryHttpExchange(", 1)[1]
        self.assertIn("http.Client.ReceiveTimeout = RemainingSocketReadTimeoutMs(\n"
                      "                        rxElapsed, rxFrequency, 45);", n6)
        self.assertLess(n4.index("ReceiveTimeout = RemainingSocketReadTimeoutMs("),
                        n4.index("int read = stream.Read("))
        self.assertLess(n6.index("ReceiveTimeout = RemainingSocketReadTimeoutMs("),
                        n6.index("int read = stream.Read("))
        self.assertIn("#if ZONDERQ_NETWORK_N4_TCP\n"
                      "        // Socket ReceiveTimeout", SOURCE)
        self.assertIn("#endif\n\n#if ZONDERQ_NETWORK_N6_HTTP\n"
                      "        // Stock Cosmos", SOURCE)

        # Arithmetic reference for 1GHz Stopwatch ticks, including the
        # final millisecond. This tests the intended timeout contract, not
        # a Cosmos socket implementation or real guest execution.
        def timeout_ms(elapsed_ticks, frequency, deadline_seconds):
            seconds, fractional_ticks = divmod(elapsed_ticks, frequency)
            elapsed_ms = seconds * 1000 + fractional_ticks * 1000 // frequency
            remaining = deadline_seconds * 1000 - elapsed_ms
            return 1 if remaining <= 1 else min(remaining, 5000)

        for deadline in (20, 45):
            self.assertEqual(timeout_ms(0, 1_000_000_000, deadline), 5000)
            self.assertEqual(timeout_ms((deadline - 3) * 1_000_000_000,
                                        1_000_000_000, deadline), 3000)
            self.assertEqual(timeout_ms(deadline * 1_000_000_000 - 500_000,
                                        1_000_000_000, deadline), 1)
            self.assertEqual(timeout_ms(deadline * 1_000_000_000 - 2_000_000,
                                        1_000_000_000, deadline), 2)

    def test_upstream_3095_and_off_by_default(self):
        self.assertIn('Sdk="Cosmos.Sdk/3.0.95"', PROJECT)
        self.assertIn('Version="3.0.95"', PROJECT)
        self.assertIn('<CosmosEnableNetwork>false</CosmosEnableNetwork>', PROJECT)
        self.assertIn('>false</ZonderqNetworkN1Probe>', PROJECT)
        self.assertIn('>ZonderqOS.NetworkN1ProbeKernel</CosmosKernelClass>', PROJECT)
        self.assertIn('ZONDERQ_NETWORK_N1_PROBE', PROJECT)
        self.assertIn('GuardNetworkN1Probe', PROJECT)
        self.assertIn('>false</ZonderqNetworkN2Dhcp>', PROJECT)
        self.assertIn('ZONDERQ_NETWORK_N2_DHCP', PROJECT)
        self.assertIn('>false</ZonderqNetworkN3Icmp>', PROJECT)
        self.assertIn('ZONDERQ_NETWORK_N3_ICMP', PROJECT)
        self.assertIn('Network N3 ICMP proof requires the isolated N2 DHCP probe.', PROJECT)
        self.assertIn('Network N2 DHCP proof requires the isolated N1 probe.', PROJECT)
        for feature in ("Storage", "Mouse", "Usb", "Keyboard", "Graphics"):
            self.assertIn(
                f'<CosmosEnable{feature} Condition="\'$(ZonderqNetworkN1Probe)\' == \'true\'">false</CosmosEnable{feature}>',
                PROJECT)
        self.assertIn('<CosmosEnableUART>true</CosmosEnableUART>', PROJECT)
        self.assertIn('Network N1 probe is x64-only', PROJECT)

    def test_no_storage_or_gui_in_probe(self):
        self.assertTrue(SOURCE.startswith("#if ZONDERQ_NETWORK_N1_PROBE"))
        self.assertIn("NetworkManager.IsInitialized", SOURCE)
        self.assertIn("Cosmos.Kernel.System.Diagnostics.Log", SOURCE)
        self.assertNotIn("Cosmos.Kernel.Core.IO", SOURCE)
        self.assertNotIn("Serial.WriteString", SOURCE)
        self.assertIn("NetworkManager.GetAdapter(i)", SOURCE)
        self.assertIn("adapter.MacAddress", SOURCE)
        self.assertIn("adapter.LinkUp", SOURCE)
        # The N1 build compiles out the N2-only DHCP block. Keep the N1
        # isolation contract strict without forbidding the explicit N2 mode.
        n1_source = SOURCE.split("#if ZONDERQ_NETWORK_N2_DHCP", 1)[0]
        for forbidden in ("Disk.Initialize(", "VfsManager.TryMount(", "WriteBlock(",
                          "File.Write", "UserManager.Initialize(", "DhcpClient(",
                          "TryFormat(", "GuiManager("):
            self.assertNotIn(forbidden, n1_source)
        self.assertIn("if (!Disk.Initialize())", MAIN)

    def test_exact_tag_and_real_qemu_gate(self):
        self.assertIn('VERSION="3.0.95"', BUILD)
        self.assertIn('git clone --depth 1 --branch "v$VERSION"', BUILD)
        self.assertIn('Wrong Cosmos package origin', BUILD)
        self.assertIn('network-n1)', BUILD)
        self.assertIn('-p:ZonderqNetworkN1Probe="$network_n1"', BUILD)
        self.assertIn('ZonderqOS-3095-network-n1.iso', BUILD)
        self.assertIn('-smp 1', SMOKE)
        self.assertIn('-device e1000e,netdev=net0', SMOKE)
        self.assertIn('tests/verify_3095_network_n1.py', SMOKE)
        self.assertIn('captured != original',
                      (ROOT / 'tests/verify_3095_network_n1.py').read_text())
        self.assertIn('sleep 8', SMOKE)
        self.assertIn('filter-dump', SMOKE)
        self.assertIn('Refusing to overwrite existing evidence', SMOKE)
        self.assertIn('Evidence paths alias', SMOKE)
        self.assertIn('realpath -m -- "$evidence"', SMOKE)
        self.assertIn('( set -C; : > "$LOG" )', SMOKE)
        self.assertNotIn('rm -f -- "$PCAP"', SMOKE)
        self.assertIn('"$LOG" "$PCAP"', SMOKE)
        self.assertIn('NetworkManager.Send(frame, frame.Length)', SOURCE)
        self.assertIn('TX_QUEUED=', SOURCE)
        self.assertIn('0x88', SOURCE)
        self.assertIn('0xB5', SOURCE)
        self.assertIn('DHCP=NOT_ATTEMPTED IP=UNASSIGNED', SOURCE)
        self.assertIn('new DhcpClient().SendDiscoverPacket()', SOURCE)
        self.assertIn('NetworkManager.Primary.IPConfig', SOURCE)
        self.assertIn('[NET3095-N2] DHCP=PASS ip=', SOURCE)
        self.assertIn('network-n2)', BUILD)
        self.assertIn('-p:ZonderqNetworkN2Dhcp="$network_n2"', BUILD)
        self.assertIn('ZonderqOS-3095-network-n2.iso', BUILD)
        self.assertIn('tests/verify_3095_network_n2.py', SMOKE)
        self.assertIn('No correlated DHCP RX/TX capture', SMOKE)
        self.assertIn('new IcmpClient()', SOURCE)
        self.assertIn('ping.SendEcho(id: 0x3095, sequence: 1)', SOURCE)
        self.assertIn('ping.Receive(ref remote, 5000)', SOURCE)
        self.assertIn('[NET3095-N3] ICMP=PASS from=', SOURCE)
        self.assertIn('network-n3)', BUILD)
        self.assertIn('-p:ZonderqNetworkN3Icmp="$network_n3"', BUILD)
        self.assertIn('ZonderqOS-3095-network-n3.iso', BUILD)
        self.assertIn('tests/verify_3095_network_n3.py', SMOKE)
        self.assertIn('No correlated ICMP echo RX/TX capture', SMOKE)
        self.assertIn('>false</ZonderqNetworkN4Tcp>', PROJECT)
        self.assertIn('ZONDERQ_NETWORK_N4_TCP', PROJECT)
        self.assertIn('Network N4 TCP proof requires the isolated N3 ICMP probe.', PROJECT)
        self.assertIn('network-n4)', BUILD)
        self.assertIn('-p:ZonderqNetworkN4Tcp="$network_n4"', BUILD)
        self.assertIn('ZonderqOS-3095-network-n4.iso', BUILD)
        self.assertIn('new System.Net.Sockets.TcpClient()', SOURCE)
        self.assertIn('client.Client.ReceiveTimeout = 5000', SOURCE)
        self.assertIn('[NET3095-N4] TCP=PASS from=', SOURCE)
        self.assertIn('tests/verify_3095_network_n4.py', SMOKE)
        self.assertIn('tests/network_3095_tcp_host.py', SMOKE)
        self.assertIn('No correlated TCP RX/TX capture', SMOKE)
        self.assertIn('>false</ZonderqNetworkN5Dns>', PROJECT)
        self.assertIn('ZONDERQ_NETWORK_N5_DNS', PROJECT)
        self.assertIn('Network N5 DNS proof requires the isolated N4 TCP probe.', PROJECT)
        self.assertIn('network-n5)', BUILD)
        self.assertIn('-p:ZonderqNetworkN5Dns="$network_n5"', BUILD)
        self.assertIn('ZonderqOS-3095-network-n5.iso', BUILD)
        self.assertIn('dns.Connect(new Address4(10, 0, 2, 3))', SOURCE)
        self.assertIn('dns.SendQuery("localhost")', SOURCE)
        self.assertIn('[NET3095-N5] DNS=PASS host=localhost', SOURCE)
        self.assertIn('tests/verify_3095_network_n5.py', SMOKE)
        self.assertIn('No correlated DNS query/answer capture', SMOKE)
        self.assertIn('HEARTBEAT=', SOURCE)
        self.assertIn('tests/verify_3095_network_heartbeat.py', SMOKE)
        self.assertIn('--count', SMOKE)
        self.assertIn('--after-count "$heartbeat_before"', SMOKE)
        self.assertNotIn('heartbeat_after - heartbeat_before < 6', SMOKE)
        self.assertIn('Guest fault after network proof', SMOKE)
        self.assertIn('socket.SO_REUSEADDR, 1', HOST)
        self.assertNotIn('socket.SO_REUSEPORT', HOST.split('def self_test', 1)[0].split('listener.setsockopt', 1)[-1])
        self.assertIn('def self_test()', HOST)
        self.assertIn('>false</ZonderqNetworkN6Http>', PROJECT)
        self.assertIn('ZONDERQ_NETWORK_N6_HTTP', PROJECT)
        self.assertIn('Network N6 HTTP proof requires the isolated N5 DNS probe.', PROJECT)
        self.assertIn('network-n6)', BUILD)
        self.assertIn('-p:ZonderqNetworkN6Http="$network_n6"', BUILD)
        self.assertIn('ZonderqOS-3095-network-n6.iso', BUILD)
        self.assertIn('[NET3095-N6] HTTP_ATTEMPT=START', SOURCE)
        self.assertIn('GET /health HTTP/1.0', SOURCE)
        self.assertIn('HTTP/1.0 200 OK', SOURCE)
        self.assertIn('tests/network_3095_http_host.py', SMOKE)
        self.assertIn('tests/verify_3095_network_n6.py', SMOKE)
        self.assertIn('No correlated HTTP TCP capture', SMOKE)
        self.assertIn('NET3095-N[1-6]', SMOKE)
        self.assertIn('for (int attempt = 1; attempt <= 2; attempt++)', SOURCE)
        self.assertIn('TryHttpExchange(attempt, sourceMac, initialIp, initialMask, initialGateway,', SOURCE)
        self.assertIn('baselineResources, baselineIrqSources)', SOURCE)
        self.assertIn('IsE1000EDriverKitStable(expectedResources, expectedIrqSources)', SOURCE)
        self.assertIn('HTTP_DRIVER_STABLE=PASS attempt=', SOURCE)
        self.assertIn('for (int attempt = 3; attempt <= 6; attempt++)', SOURCE)
        self.assertIn('POST_BURST_HTTP_REPEAT=PASS attempt=', SOURCE)
        self.assertIn('POST_BURST_HTTP_ATTEMPT=START', SOURCE)
        self.assertIn('POST_BURST_HTTP=PASS', SOURCE)
        self.assertIn('POST_STRESS_LINK=PASS', SOURCE)
        # Final RX liveness must follow all six TCP closes and precede
        # the post-stress NIC/lease snapshot; serial alone is insufficient.
        self.assertIn('finalPing.SendEcho(id: 0x3097, sequence: 1)', SOURCE)
        self.assertIn('FINAL_ICMP_ATTEMPT=START', SOURCE)
        self.assertIn('FINAL_ICMP=PASS from=10.0.2.2 seq=1', SOURCE)
        self.assertLess(SOURCE.index('for (int attempt = 3; attempt <= 6; attempt++)'),
                        SOURCE.index('FINAL_ICMP_ATTEMPT=START'))
        self.assertLess(SOURCE.index('FINAL_ICMP=PASS from=10.0.2.2 seq=1'),
                        SOURCE.index('POST_STRESS_LINK=PASS'))
        self.assertIn('verify_3095_network_n6_final_icmp.py', SMOKE)
        self.assertIn('No final ICMP RX/TX after six HTTP teardowns', SMOKE)
        self.assertIn('check_final_pair(frames, last_http_fin_ack(frames, guest), guest)',
                      (ROOT / 'tests/verify_3095_network_n6_final_icmp.py').read_text())
        self.assertIn('if (readCalls >= 64)', SOURCE)
        self.assertIn('HTTP response fragmented into too many reads', SOURCE)
        n6_http = SOURCE.split('private static bool TryHttpExchange(', 1)[1]
        self.assertLess(n6_http.index('if (readCalls >= 64)'),
                        n6_http.index('int read = stream.Read('))
        self.assertIn('POST_STRESS_IPV4=PASS', SOURCE)
        self.assertIn('postStress.IPConfig', SOURCE)
        for field, snapshot in (('Address', 'initialIp'),
                                ('SubnetMask', 'initialMask'),
                                ('DefaultGateway', 'initialGateway')):
            self.assertIn(f'string {snapshot} = config.{field}.ToString();', SOURCE)
            self.assertIn(f'postStressConfig.{field}.ToString() != {snapshot}', SOURCE)
        self.assertLess(SOURCE.index('string initialIp = config.Address.ToString();'),
                        SOURCE.index('POST_BURST_HTTP_ATTEMPT=START'))
        self.assertLess(SOURCE.index('POST_STRESS_LINK=PASS'),
                        SOURCE.index('POST_STRESS_IPV4=PASS'))
        self.assertLess(SOURCE.index('POST_STRESS_IPV4=PASS'),
                        SOURCE.index('if (!CheckE1000EDriverKitHealth(baselineResources, baselineIrqSources))'))
        self.assertIn('TryGetE1000EDriverKitHealth(out var baselineDriverNode)', SOURCE)
        self.assertIn('candidate.HeldResourceCount != expectedResources', SOURCE)
        self.assertIn('candidate.InterruptCount != expectedIrqSources', SOURCE)
        self.assertLess(SOURCE.index('DRIVER_BASELINE=PASS'),
                        SOURCE.index('POST_BURST_HTTP_ATTEMPT=START'))
        self.assertLess(SOURCE.index('DRIVER_BASELINE=PASS'),
                        SOURCE.index('DRIVER_HEALTH=PASS'))
        self.assertIn('postStress.LinkUp', SOURCE)
        self.assertIn('postStress.MacAddress', SOURCE)
        self.assertIn('verify_3095_network_n6_post_http.py', SMOKE)
        self.assertIn('POST_BURST_PASS HTTP/1.0 GET /health 200', SMOKE)
        self.assertIn('HTTP_REPEAT=PASS', SOURCE)
        self.assertIn('TrySendBurst(frame, sourceMac, initialIp, initialMask,', SOURCE)
        self.assertIn('TX_DRIVER_STABLE=PASS count=128 checks=5', SOURCE)
        self.assertIn('if (!IsE1000EDriverKitStable(expectedResources, expectedIrqSources))', SOURCE)
        self.assertIn('TX_DRIVER_CHECK=PASS sent=128 resources=', SOURCE)
        self.assertIn('TX_NIC_CHECK=PASS sent=128', SOURCE)
        self.assertIn('TX_NIC_CHECK = re.compile',
                      (ROOT / 'tests/verify_3095_e1000e_irq_binding.py').read_text())
        self.assertIn('for (int sequence = 0; sequence < 128; sequence++)', SOURCE)
        self.assertIn("burst[14 + tag.Length] = (byte)('0' + sequence / 100);", SOURCE)
        self.assertIn("burst[15 + tag.Length] = (byte)('0' + (sequence / 10) % 10);", SOURCE)
        self.assertIn("burst[16 + tag.Length] = (byte)('0' + sequence % 10);", SOURCE)
        self.assertIn('TX_BURST_ATTEMPT=START count=128', SOURCE)
        self.assertIn('TX_BURST=QUEUED count=128', SOURCE)
        self.assertIn('POST_BURST_ICMP_ATTEMPT=START', SOURCE)
        self.assertIn('for (ushort sequence = 2; sequence <= 5; sequence++)', SOURCE)
        self.assertIn('postBurstPing.SendEcho(id: 0x3096, sequence: sequence)', SOURCE)
        self.assertIn('POST_BURST_ICMP=PASS from=10.0.2.2 count=4', SOURCE)
        # Read-only NIC/lease and DriverKit checks bracket every ICMP echo,
        # including the final echo after all six HTTP TCP teardowns.
        for phase in ("post_before", "post_after", "final_before", "final_after"):
            self.assertIn(f'CheckE1000ECheckpoint("{phase}"', SOURCE)
        self.assertIn('ICMP_DRIVER_CHECK=PASS phase=', SOURCE)
        self.assertLess(SOURCE.index('CheckE1000ECheckpoint("post_before"'),
                        SOURCE.index('postBurstPing.SendEcho(id: 0x3096'))
        self.assertLess(SOURCE.index('postBurstPing.Close();'),
                        SOURCE.index('CheckE1000ECheckpoint("post_after"'))
        self.assertLess(SOURCE.index('CheckE1000ECheckpoint("final_before"'),
                        SOURCE.index('finalPing.SendEcho(id: 0x3097'))
        self.assertLess(SOURCE.index('finalPing.Close();'),
                        SOURCE.index('CheckE1000ECheckpoint("final_after"'))
        irq_gate = (ROOT / 'tests/verify_3095_e1000e_irq_binding.py').read_text()
        self.assertIn('ICMP_CHECK = re.compile', irq_gate)
        self.assertIn('missing or duplicate ICMP DriverKit checkpoints', irq_gate)
        self.assertIn('final ICMP did not follow all six HTTP sessions', irq_gate)

        self.assertIn('POST_BURST_ICMP=PASS from=10.0.2.2', SOURCE)
        self.assertIn('verify_3095_network_n6_burst.py', SMOKE)
        self.assertIn('check_post_burst_icmp(frames, last_burst, guest)',
                      (ROOT / 'tests/verify_3095_network_n6_burst.py').read_text())
        self.assertIn('No 128-frame ordered raw TX PCAP proof', SMOKE)

        self.assertIn('for attempt in range(1, 7):', HTTP_HOST)
        self.assertIn('POST_BURST_REPEAT_PASS attempt=', HTTP_HOST)
        self.assertIn('for attempt in 4 5 6; do', SMOKE)
        self.assertIn('else "REPEAT_PASS"', HTTP_HOST)
        self.assertIn('POST_BURST_PASS', HTTP_HOST)
        self.assertIn('HTTP_REPEAT_PASS', HTTP_VALIDATOR)
        self.assertIn('second independent HTTP TCP exchange incomplete', HTTP_VALIDATOR)
        self.assertIn('REPEAT_PASS HTTP/1.0', SMOKE)
        self.assertIn('http_pid', SMOKE)
        self.assertIn('NET3095_IRQ_TRACE', SMOKE)
        self.assertIn('e1000e_irq_legacy_notify', SMOKE)
        self.assertIn('e1000e_irq_msi_notify', SMOKE)
        self.assertIn('e1000e_irq_msix_notify_vec', SMOKE)
        self.assertIn('tests/verify_3095_e1000e_irq_trace.py', SMOKE)
        self.assertIn('"$LOG.stderr" --require-notify', SMOKE)
        self.assertIn('tests/verify_3095_e1000e_irq_binding.py', SMOKE)
        self.assertIn('guest handler/IRQ delivery UNPROVEN',
                      (ROOT / 'tests/verify_3095_e1000e_irq_trace.py').read_text())
        # A stuck-high or repeated legacy assertion is not four separate
        # IRQ cycles. The QEMU trace must show low/high transitions.
        irq_trace = (ROOT / 'tests/verify_3095_e1000e_irq_trace.py').read_text()
        irq_binding = (ROOT / 'tests/verify_3095_e1000e_irq_binding.py').read_text()
        self.assertIn('def count_legacy_cycles(', irq_trace)
        self.assertIn('cycles = count_legacy_cycles(qemu_stderr)', irq_binding)
        self.assertIn('if legacy < 4 or cycles < 4:', irq_binding)
        self.assertIn('qemu-legacy-cycles=', irq_binding)

        self.assertIn('trap cleanup EXIT INT TERM', SMOKE)

    def test_n6_driverkit_health_is_read_only_and_not_an_isr_claim(self):
        # Public stock 3.0.95 snapshots expose node binding and handler
        # exceptions, but NOT E1000EState.InterruptCount (ISR invocations).
        # Require the real N6 runner to check the guest snapshot even when
        # QEMU IRQ tracing is disabled.
        health = (ROOT / 'tests/verify_3095_e1000e_irq_binding.py').read_text()
        for token in (
            'DriverDiagnostics.TryGetNode(i, out var node)',
            'candidate.State == DeviceNodeState.Bound',
            'candidate.FaultCount == 0',
            'candidate.HeldResourceCount >= 1',
            'candidate.PublishedDeviceCount == 1',
            'candidate.InterruptCount >= 1',
            'handler_faults=0 isr_executions=unknown',
            'if (!CheckE1000EDriverKitHealth(baselineResources, baselineIrqSources))',
        ):
            self.assertIn(token, SOURCE)
        self.assertLess(SOURCE.index('POST_STRESS_LINK=PASS'),
                        SOURCE.index('if (!CheckE1000EDriverKitHealth(baselineResources, baselineIrqSources))'))
        self.assertIn('check_driver_health(serial)', health)
        # Five individually observed TX ring checkpoints, not just a
        # synthetic summary claiming that five checks occurred.
        self.assertIn('TX_DRIVER_CHECK=PASS sent=', SOURCE)
        self.assertIn('TX_DRIVER_CHECK=PASS sent=128', SOURCE)
        self.assertIn('for expected_sent, claim in zip((0, 32, 64, 96, 128), tx_checks):',
                      health)
        self.assertIn('HTTP DriverKit snapshot on wrong side of TX burst', health)
        self.assertIn('HTTP DriverKit ownership changed after TX attempt=', SOURCE)
        self.assertIn('HTTP DriverKit ownership changed during RX attempt=', SOURCE)
        http_body = SOURCE.split('private static bool TryHttpExchange(', 1)[1]
        self.assertLess(http_body.index('received += read;'),
                        http_body.index('HTTP DriverKit ownership changed during RX attempt='))
        self.assertLess(http_body.index('HTTP DriverKit ownership changed during RX attempt='),
                        http_body.index('HTTP NIC/lease changed during RX attempt='))
        self.assertIn('isr_executions=unknown' + chr(36), health)
        self.assertIn('ISR-UNPROVEN', health)
        self.assertIn('python3 "$ROOT/tests/verify_3095_e1000e_irq_binding.py" "$LOG"', SMOKE)
        self.assertNotIn('binding.TryRequestInterrupt(', SOURCE)

    def test_n6_all_rx_segments_and_guest_read_telemetry(self):
        # Require identical byte-varying formulas in the C# guest and
        # Python host, including connection and position dependence.
        # Uniform 1 KiB blocks can hide shifted/stale DMA fragments.
        for term in ("offset * 37", "(offset / 90) * 11",
                     "(offset / 1024) * 23", "attempt * 29"):
            self.assertIn(term, SOURCE)
        for term in ("offset * 37", "(offset // 90) * 11",
                     "(offset // 1024) * 23", "attempt * 29"):
            self.assertIn(term, HTTP_HOST)
        self.assertIn("expected[prefix.Length + offset]", SOURCE)
        self.assertIn("patterned_body(attempt)", HTTP_HOST)
        # All six requests must have distinct bytes, checked independently
        # by the loopback host and PCAP verifier (not only serial markers).
        self.assertIn('"X-Zonderq-Attempt: " + attempt', SOURCE)
        self.assertIn("def request_for_attempt(attempt: int)", HTTP_HOST)
        self.assertIn("request_for_attempt(attempt)", HTTP_HOST)
        self.assertIn("request_for_attempt(request_index + 1)", HTTP_HOST)
        self.assertIn("request_for_attempt(attempt), server_mac", HTTP_VALIDATOR)
        self.assertIn("request_for_attempt(2)", HTTP_VALIDATOR)
        self.assertIn("accepted stale third-session HTTP TX bytes",
                      (ROOT / "tests/verify_3095_network_n6_post_http.py").read_text())
        self.assertIn("accepted stale post-burst HTTP TX attempt=",
                      (ROOT / "tests/verify_3095_network_n6_post_http.py").read_text())
        self.assertIn("len(set(chunk)) > 50", HTTP_HOST)
        self.assertNotIn("new string((char)(", SOURCE)
        self.assertIn('readCalls++', SOURCE)
        self.assertIn('if (read > largestRead)', SOURCE)
        self.assertIn('HTTP_RX_STATS attempt=', SOURCE)
        # Every HTTP RX chunk must recheck the live adapter against the
        # original DHCP string snapshots; no mutable IPConfig self-baseline.
        self.assertIn('private static bool IsE1000ELeaseStable(', SOURCE)
        self.assertIn('lease.Address.ToString() == expectedIp', SOURCE)
        self.assertIn('lease.SubnetMask.ToString() == expectedMask', SOURCE)
        self.assertIn('lease.DefaultGateway.ToString() == expectedGateway', SOURCE)
        self.assertIn('HTTP NIC/lease changed during RX attempt=', SOURCE)
        self.assertIn('HTTP_NIC_STABLE=PASS attempt=', SOURCE)
        self.assertIn('HTTP_NIC_STABLE=PASS attempt=', (ROOT / 'tests/verify_3095_network_n6_post_http.py').read_text())
        self.assertLess(SOURCE.index('received += read;'),
                        SOURCE.index('HTTP NIC/lease changed during RX attempt='))
        self.assertLess(SOURCE.index('HTTP NIC/lease changed during RX attempt='),
                        SOURCE.index('HTTP_NIC_STABLE=PASS attempt='))

        self.assertIn(
            'check_rx_stats(serial)',
            (ROOT / 'tests/verify_3095_network_n6_post_http.py').read_text())


    def test_n6_tx_ring_checks_live_nic_at_each_32_descriptor_boundary(self):
        # Queueing 128 frames is not DMA proof. While the isolated guest
        # reuses its TX buffer, reject link loss or a rebound NIC at each
        # ring boundary; the separate QEMU PCAP remains mandatory.
        burst = SOURCE.split("private static bool TrySendBurst(byte[] template,", 1)[1]
        burst = burst.split("private static int HexDigit", 1)[0]
        for term in (
            "for (int sequence = 0; sequence < 128; sequence++)",
            "if ((sequence & 31) == 0)",
            "NetworkAdapter current = NetworkManager.Primary;",
            '!current.IsValid || current.Name != "e1000e"',
            "!current.Ready || !current.LinkUp",
            'TryFillSourceMac(current.MacAddress?.ToString() ?? "", liveMacFrame)',
            "liveMacFrame[6 + macByte] != template[6 + macByte]",
            "if (!NetworkManager.Send(burst, burst.Length))",
            "Thread.Sleep(5);",
        ):
            self.assertIn(term, burst)
        self.assertLess(burst.index("if ((sequence & 31) == 0)"),
                        burst.index("if (!NetworkManager.Send(burst, burst.Length))"))
        self.assertIn("IsE1000EDriverKitStable(expectedResources, expectedIrqSources)", burst)
        self.assertLess(burst.index("IsE1000EDriverKitStable(expectedResources, expectedIrqSources)"),
                        burst.index("if (!NetworkManager.Send(burst, burst.Length))"))
        self.assertIn('TX_NIC_CHECK=PASS sent=128', burst)
        self.assertIn('IsE1000ELeaseStable(expectedMac, expectedIp,', burst)
        self.assertLess(burst.index('IsE1000ELeaseStable(expectedMac, expectedIp,'),
                        burst.index('TX_NIC_CHECK=PASS sent='))
        self.assertLess(burst.index('TX_NIC_CHECK=PASS sent='),
                        burst.index('if (!NetworkManager.Send(burst, burst.Length))'))
        self.assertIn("verify_3095_network_n6_burst.py", SMOKE)



    def test_dns_reply_mac_correlates_with_real_icmp_gateway(self):
        # The QEMU DNS proxy ARP address and the Ethernet source of its
        # UDP answer differ. Trust the N3-proven gateway MAC for RX, not
        # an arbitrary source address or the DNS proxy's ARP MAC.
        dns = (ROOT / "tests/verify_3095_network_n5.py").read_text()
        self.assertIn("packet := icmp_frame(frame)", dns)
        self.assertIn('packet["src"] == SERVER', dns)
        self.assertIn('packet["dst_mac"] == MAC', dns)
        self.assertIn("gateway_mac = next(iter(gateway_macs))", dns)
        self.assertIn('reply["src_mac"] == gateway_mac', dns)
        self.assertIn("reply_src_mac=b", dns)
        self.assertIn("verify_arp_for_dns(frames, guest)", dns)


if __name__ == "__main__":
    unittest.main(verbosity=2)
