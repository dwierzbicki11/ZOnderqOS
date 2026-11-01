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
        self.assertIn('TryHttpExchange(attempt)', SOURCE)
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
                        SOURCE.index('if (!CheckE1000EDriverKitHealth())'))
        self.assertIn('postStress.LinkUp', SOURCE)
        self.assertIn('postStress.MacAddress', SOURCE)
        self.assertIn('verify_3095_network_n6_post_http.py', SMOKE)
        self.assertIn('POST_BURST_PASS HTTP/1.0 GET /health 200', SMOKE)
        self.assertIn('HTTP_REPEAT=PASS', SOURCE)
        self.assertIn('TrySendBurst(frame)', SOURCE)
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
        self.assertIn('trap cleanup EXIT INT TERM', SMOKE)

    def test_n6_driverkit_health_is_read_only_and_not_an_isr_claim(self):
        # Public stock 3.0.95 snapshots expose node binding and handler
        # exceptions, but NOT E1000EState.InterruptCount (ISR invocations).
        # Require the real N6 runner to check the guest snapshot even when
        # QEMU IRQ tracing is disabled.
        health = (ROOT / 'tests/verify_3095_e1000e_irq_binding.py').read_text()
        for token in (
            'DriverDiagnostics.TryGetNode(i, out var node)',
            'candidate.State != DeviceNodeState.Bound',
            'candidate.FaultCount != 0',
            'candidate.PublishedDeviceCount != 1',
            'candidate.InterruptCount < 1',
            'handler_faults=0 isr_executions=unknown',
            'if (!CheckE1000EDriverKitHealth())',
        ):
            self.assertIn(token, SOURCE)
        self.assertLess(SOURCE.index('POST_STRESS_LINK=PASS'),
                        SOURCE.index('if (!CheckE1000EDriverKitHealth())'))
        self.assertIn('check_driver_health(serial)', health)
        self.assertIn('isr_executions=unknown$', health)
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
        self.assertIn(
            'check_rx_stats(serial)',
            (ROOT / 'tests/verify_3095_network_n6_post_http.py').read_text())


    def test_n6_tx_ring_checks_live_nic_at_each_32_descriptor_boundary(self):
        # Queueing 128 frames is not DMA proof. While the isolated guest
        # reuses its TX buffer, reject link loss or a rebound NIC at each
        # ring boundary; the separate QEMU PCAP remains mandatory.
        burst = SOURCE.split("private static bool TrySendBurst(byte[] template)", 1)[1]
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
        self.assertIn("verify_3095_network_n6_burst.py", SMOKE)


if __name__ == "__main__":
    unittest.main(verbosity=2)
