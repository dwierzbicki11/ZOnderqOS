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
    def test_n1_requires_readonly_pci_identity_and_busmaster_before_tx(self):
        # Source contract only: QEMU PCAP remains the actual frame TX proof.
        pci = (ROOT / "source/Hardware/E1000EPciIdentity.cs").read_text()
        self.assertIn("config.Read16(bus, slot, function, 0x00) != 0x8086", pci)
        self.assertIn("config.Read16(bus, slot, function, 0x02) != 0x10D3", pci)
        self.assertIn("(observed & 0x0006) != 0x0006", pci)
        self.assertIn("config.Read16(bus, slot, function, 0x10)", pci)
        self.assertIn("config.Read16(bus, slot, function, 0x12)", pci)
        self.assertIn("config.Read16(bus, slot, function, 0x14)", pci)
        self.assertIn("config.Read16(bus, slot, function, 0x16)", pci)
        self.assertIn("type == 0 && address == 0xFFFFFFF0UL", pci)
        self.assertIn("(low & 0x09u) != 0", pci)  # register BAR cannot be prefetchable
        self.assertIn("TryReadLegacyInterruptMode(", pci)
        self.assertIn("(status & 0x0010) == 0", pci)
        self.assertIn("(control & 0x0001) != 0", pci)  # MSI enabled
        self.assertIn("(control & 0x8000) != 0", pci)  # MSI-X enabled
        self.assertIn("finalMode != initialMode", pci)
        self.assertIn("address == 0xFFFFFFFFFFFFFFF0UL", pci)
        # Read-only legacy INTx metadata is a prerequisite for QEMU's
        # legacy IRQ trace, never a guest ISR invocation counter.
        self.assertIn("TryReadLegacyIrqRoute(", pci)
        self.assertIn("(command & 0x0400) != 0", pci)
        self.assertIn("config.Read8(bus, slot, function, 0x3C)", pci)
        self.assertIn("config.Read8(bus, slot, function, 0x3D)", pci)
        self.assertIn("line == 0 || line == 0xFF || pin < 1 || pin > 4", pci)
        self.assertIn("PCI_IRQ_ROUTE=PASS line=", SOURCE)
        self.assertIn("PCI_IRQ_ROUTE_STABLE=PASS line=", SOURCE)
        self.assertIn("txIrqLine != pciIrqLine || txIrqPin != pciIrqPin", SOURCE)
        self.assertIn("stressIrqLine != pciIrqLine || stressIrqPin != pciIrqPin", SOURCE)
        self.assertIn("s_linkCycleIrqLine = pciIrqLine;", SOURCE)
        self.assertIn("s_linkCycleIrqPin = pciIrqPin;", SOURCE)
        self.assertIn("irqLine == s_linkCycleIrqLine", SOURCE)
        self.assertIn("irqPin == s_linkCycleIrqPin", SOURCE)
        self.assertNotIn("Write16(", pci)
        self.assertNotIn("Write8(", pci)
        self.assertIn("out ulong pciBar0", SOURCE)
        self.assertIn("txPciBar0 != pciBar0", SOURCE)
        # N6 must also check the original MMIO BAR after all HTTP, burst TX
        # and post-burst ICMP activity, not merely at initial N1 raw TX.
        stress_start = SOURCE.index('POST_STRESS_IPV4=PASS ip=')
        stress_bar = SOURCE.index('stressPciBar0 != pciBar0', stress_start)
        stress_health = SOURCE.index('CheckE1000EDriverKitHealth(baselineResources', stress_bar)
        self.assertLess(stress_start, stress_bar)
        self.assertLess(stress_bar, stress_health)
        # QMP link down/up and fresh TCP reconnections must not silently
        # accept a remapped PCI BAR0 with unchanged MAC/lease metadata.
        self.assertIn("s_linkCyclePciCommand = pciCommand;", SOURCE)
        self.assertIn("s_linkCyclePciBar0 = pciBar0;", SOURCE)
        helper = SOURCE.split("private static bool IsE1000EPciMappingStable()", 1)[1].split(
            "private static bool TryTcpReconnect(", 1)[0]
        self.assertIn("TryReadReady(", helper)
        self.assertIn("command == s_linkCyclePciCommand", helper)
        self.assertIn("bar0 == s_linkCyclePciBar0", helper)
        link = SOURCE.split("private static void PollLinkCycle()", 1)[1]
        self.assertGreaterEqual(link.count("IsE1000EPciMappingStable()"), 2)
        reconnect = SOURCE.split("private static bool TryTcpReconnect(", 1)[1].split(
            "private static void PollLinkCycle()", 1)[0]
        self.assertGreaterEqual(reconnect.count("IsE1000EPciMappingStable()"), 2)
        self.assertNotIn("Write16(", pci)
        self.assertNotIn("Write8(", pci)
        before = SOURCE.index("E1000E PCI identity/MMIO/busmaster invalid before TX")
        bound = SOURCE.index("DRIVER_BOUND=PASS node=", before)
        send = SOURCE.index("bool queued = NetworkManager.Send(frame, frame.Length);")
        after = SOURCE.index("E1000E PCI identity/command changed during TX", send)
        self.assertLess(before, bound)
        self.assertLess(bound, send)
        self.assertLess(send, after)
        self.assertIn("txPciCommand != pciCommand", SOURCE[send:after])

    def test_n2_to_n5_preserve_original_driverkit_binding(self):
        # These checks are source contracts, not proof of ISR execution.
        # A rebind to the same MAC with a new resource/IRQ registration must
        # invalidate a DHCP/ICMP/TCP/DNS success claim.
        helper = SOURCE.split(
            "private static bool IsE1000EDriverKitSnapshotStable(", 1)[1].split(
            "#if ZONDERQ_NETWORK_N6_HTTP", 1)[0]
        for token in (
            "DeviceNodeInfo expected", "TryGetE1000EDriverKitHealth(out var current)",
            "current.Path == expected.Path",
            "current.HeldResourceCount == expected.HeldResourceCount",
            "current.InterruptCount == expected.InterruptCount",
        ):
            self.assertIn(token, helper)
        self.assertNotIn("TryRequestInterrupt", helper)
        self.assertNotIn("NetworkManager.Send", helper)

        n2 = SOURCE.split("#if ZONDERQ_NETWORK_N2_DHCP", 1)[1].split(
            "#if ZONDERQ_NETWORK_N3_ICMP", 1)[0]
        n3 = SOURCE.split("#if ZONDERQ_NETWORK_N3_ICMP", 1)[1].split(
            "#if ZONDERQ_NETWORK_N4_TCP", 1)[0]
        n4 = SOURCE.split("#if ZONDERQ_NETWORK_N4_TCP", 1)[1].split(
            "#if ZONDERQ_NETWORK_N5_DNS", 1)[0]
        n5 = SOURCE.split("#if ZONDERQ_NETWORK_N5_DNS", 1)[1].split(
            "#if ZONDERQ_NETWORK_N6_HTTP", 1)[0]

        for stage, section, after, fail, success in (
            ("DHCP", n2, 'config.DefaultGateway.ToString() != "10.0.2.2"',
             "DriverKit ownership changed during DHCP", "DHCP=PASS ip="),
            ("ICMP", n3, "ping.Receive(ref remote, 5000)",
             "DriverKit ownership changed during ICMP", "ICMP=PASS from="),
            ("TCP", n4, "if (response[i] != expected[i])",
             "DriverKit ownership changed at TCP completion", "TCP=PASS from="),
            ("DNS", n5, "dns.Receive(5000)",
             "DriverKit ownership changed during DNS", "DNS=PASS host="),
        ):
            with self.subTest(stage=stage):
                self.assertLess(section.index(after),
                                section.index("IsE1000EDriverKitSnapshotStable(n1DriverNode)", section.index(after)))
                self.assertLess(section.index(fail), section.index(success))

        # Each protocol checks the N1 DriverKit snapshot before opening
        # its first receive path, and again after receiving data.
        for stage, section, start, operation in (
            ("DHCP", n2, "DHCP_ATTEMPT=START", "SendDiscoverPacket()"),
            ("ICMP", n3, "ICMP_ATTEMPT=START", "ping.SendEcho("),
            ("TCP", n4, "TCP_ATTEMPT=START", "client.Connect("),
            ("DNS", n5, "DNS_ATTEMPT=START", "dns.SendQuery("),
        ):
            with self.subTest(preflight=stage):
                marker = section.index(start)
                check = section.index(
                    "IsE1000EDriverKitSnapshotStable(n1DriverNode)", marker)
                failure = section.index(
                    "DriverKit ownership invalid before " + stage, check)
                action = section.index(operation, marker)
                self.assertLess(marker, check)
                self.assertLess(check, failure)
                self.assertLess(failure, action)

        # TCP is multi-read: verify ownership immediately after TX and
        # every successful Read, not only after the final response.
        sent = n4.index("stream.Write(request, 0, request.Length);")
        tx_check = n4.index("DriverKit ownership changed after TCP TX", sent)
        read = n4.index("int read = stream.Read(", tx_check)
        received = n4.index("received += read;", read)
        rx_check = n4.index("DriverKit ownership changed during TCP RX", received)
        deadline = n4.index("echoElapsed = Stopwatch.GetTimestamp() - echoStart;", received)
        completed = n4.index("DriverKit ownership changed at TCP completion", rx_check)
        passed = n4.index("TCP=PASS from=", completed)
        self.assertLess(tx_check, read)
        self.assertLess(received, rx_check)
        self.assertLess(rx_check, deadline)
        self.assertLess(completed, passed)
        self.assertEqual(n4.count("IsE1000EDriverKitSnapshotStable(n1DriverNode)"), 4)

    def test_n1_requires_real_driverkit_pci_binding_before_raw_tx(self):
        source = SOURCE
        helper = source.index("private static bool TryGetE1000EDriverKitHealth(")
        health = source.index("candidate.Path == \"pci:0000:00:02.0\"", helper)
        self.assertLess(helper, health)
        n1 = source.index("E1000E PCI DriverKit binding invalid")
        marker = source.index("DRIVER_BOUND=PASS node=", n1)
        send = source.index("bool queued = NetworkManager.Send(frame, frame.Length);")
        self.assertLess(n1, marker)
        self.assertLess(marker, send)
        self.assertIn("TryGetE1000EDriverKitHealth(out var n1DriverNode)", source[:send])
        self.assertIn("irq_sources=", source[marker:send])
        after_send = source.index("E1000E DriverKit binding changed during TX", send)
        end = source.index("DHCP=NOT_ATTEMPTED IP=UNASSIGNED", after_send)
        self.assertLess(send, after_send)
        self.assertLess(after_send, end)
        self.assertIn("TryGetE1000EDriverKitHealth(out var txDriverNode)", source[send:end])
        self.assertIn("txDriverNode.HeldResourceCount != n1DriverNode.HeldResourceCount", source[send:end])
        self.assertIn("txDriverNode.InterruptCount != n1DriverNode.InterruptCount", source[send:end])
        self.assertIn("handler_faults=0 isr_executions=unknown", source[marker:send])
        verifier = (ROOT / "tests/verify_3095_network_n1.py").read_text()
        self.assertIn("missing, duplicated or malformed E1000E DriverKit binding", verifier)
        self.assertIn("invalid E1000E PCI resources or IRQ source registration", verifier)

    def test_n1_rechecks_live_e1000e_after_raw_tx(self):
        start = SOURCE.index("bool queued = NetworkManager.Send(frame, frame.Length);")
        check = SOURCE.index("NetworkAdapter txAdapter = NetworkManager.Primary;", start)
        fail = SOURCE.index("E1000E binding/link/MAC changed during TX", check)
        end = SOURCE.index("DHCP=NOT_ATTEMPTED IP=UNASSIGNED", fail)
        self.assertLess(start, check)
        self.assertLess(check, fail)
        self.assertLess(fail, end)
        for condition in (
            "NetworkManager.IsEnabled", "NetworkManager.IsInitialized",
            "NetworkManager.DeviceCount != 1", "txAdapter.IsValid",
            'txAdapter.Name != "e1000e"', "txAdapter.Ready",
            "txAdapter.LinkUp", "!= sourceMac",
        ):
            self.assertIn(condition, SOURCE[check:end])

    def test_isolated_nic_registration_remains_unique(self):
        self.assertIn("if (count != 1)", SOURCE)
        self.assertIn("if (readyLinks != 1)", SOURCE)
        self.assertGreaterEqual(SOURCE.count("NetworkManager.DeviceCount != 1"), 6)
        helper = SOURCE.split("private static bool IsE1000ELeaseStable(", 1)[1]
        self.assertIn("NetworkManager.IsEnabled", helper)
        self.assertIn("NetworkManager.IsInitialized", helper)
        self.assertIn("NetworkManager.DeviceCount != 1", helper)


    def test_n3_icmp_rechecks_live_nic_and_dhcp_after_rx(self):
        # The PCAP proves an ICMP exchange, but the guest must not claim
        # a healthy NIC after a concurrent unbind, link loss or lease reset.
        n3 = SOURCE.split("#if ZONDERQ_NETWORK_N3_ICMP", 1)[1].split(
            "#if ZONDERQ_NETWORK_N4_TCP", 1)[0]
        received = n3.index("ping.Receive(ref remote, 5000)")
        live = n3.index("NetworkAdapter icmpAdapter = NetworkManager.Primary;", received)
        lease = n3.index("IPConfig? icmpLease = icmpAdapter.IPConfig;", live)
        failed = n3.index("E1000E link/MAC/DHCP lease changed during ICMP echo", lease)
        passed = n3.index("ICMP=PASS from=", failed)
        self.assertLess(received, live)
        self.assertLess(live, lease)
        self.assertLess(lease, failed)
        self.assertLess(failed, passed)
        for condition in (
            "NetworkManager.IsEnabled", "NetworkManager.IsInitialized",
            "icmpAdapter.IsValid", 'icmpAdapter.Name != "e1000e"',
            "icmpAdapter.Ready", "icmpAdapter.LinkUp",
            'icmpAdapter.MacAddress?.ToString() ?? ""',
            "!= sourceMac", "icmpLease == null",
            'icmpLease.Address.ToString() != "10.0.2.15"',
            'icmpLease.SubnetMask.ToString() != "255.255.255.0"',
            'icmpLease.DefaultGateway.ToString() != "10.0.2.2"',
        ):
            self.assertIn(condition, n3[live:passed])

    def test_n4_tcp_rechecks_live_nic_and_dhcp_after_rx(self):
        # A valid TCP payload does not establish that the original E1000E
        # remained bound and configured at the moment of the PASS marker.
        n4 = SOURCE.split("#if ZONDERQ_NETWORK_N4_TCP", 1)[1].split(
            "#if ZONDERQ_NETWORK_N5_DNS", 1)[0]
        payload = n4.index("if (response[i] != expected[i])")
        live = n4.index("NetworkAdapter echoAdapter = NetworkManager.Primary;", payload)
        lease = n4.index("IPConfig? echoLease = echoAdapter.IPConfig;", live)
        failure = n4.index("E1000E link/MAC/DHCP lease changed during TCP echo", lease)
        passed = n4.index("TCP=PASS from=10.0.2.2", failure)
        self.assertLess(payload, live)
        self.assertLess(live, lease)
        self.assertLess(lease, failure)
        self.assertLess(failure, passed)
        for condition in (
            "NetworkManager.IsEnabled", "NetworkManager.IsInitialized",
            "echoAdapter.IsValid", 'echoAdapter.Name != "e1000e"',
            "echoAdapter.Ready", "echoAdapter.LinkUp",
            'echoAdapter.MacAddress?.ToString() ?? ""',
            "!= sourceMac", "echoLease == null",
            'echoLease.Address.ToString() != "10.0.2.15"',
            'echoLease.SubnetMask.ToString() != "255.255.255.0"',
            'echoLease.DefaultGateway.ToString() != "10.0.2.2"',
        ):
            self.assertIn(condition, n4[live:passed])

    def test_n5_dns_rechecks_live_nic_and_dhcp_after_rx(self):
        # Source contract: the DNS response alone is insufficient when the
        # E1000E link, MAC or DHCP lease changed while Receive() blocked.
        n5 = SOURCE.split("#if ZONDERQ_NETWORK_N5_DNS", 1)[1].split(
            "#if ZONDERQ_NETWORK_N6_HTTP", 1)[0]
        response = n5.index('answer.ToString() != "127.0.0.1"')
        live = n5.index("NetworkAdapter dnsAdapter = NetworkManager.Primary;", response)
        lease = n5.index("IPConfig? dnsLease = dnsAdapter.IPConfig;", live)
        checks = n5.index('dnsLease.DefaultGateway.ToString() != "10.0.2.2"', lease)
        failure = n5.index("E1000E link/MAC/DHCP lease changed during DNS", checks)
        passed = n5.index("DNS=PASS host=localhost", failure)
        self.assertLess(response, live)
        self.assertLess(live, lease)
        self.assertLess(lease, checks)
        self.assertLess(checks, failure)
        self.assertLess(failure, passed)
        for condition in (
            "NetworkManager.IsEnabled", "NetworkManager.IsInitialized",
            "dnsAdapter.IsValid", 'dnsAdapter.Name != "e1000e"',
            "dnsAdapter.Ready", "dnsAdapter.LinkUp",
            'dnsAdapter.MacAddress?.ToString() ?? ""',
            "!= sourceMac", "dnsLease == null",
            'dnsLease.Address.ToString() != "10.0.2.15"',
            'dnsLease.SubnetMask.ToString() != "255.255.255.0"',
        ):
            self.assertIn(condition, n5[live:passed])

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

    def test_n6_tcp_reconnect_deadline_checked_after_final_read(self):
        # A peer may return the last byte after the total deadline even if
        # the timeout was clamped before the final blocking Read().
        reconnect = SOURCE.split("private static bool TryTcpReconnect(int cycle)", 1)[1].split(
            "private static void PollLinkCycle()", 1)[0]
        start = reconnect.index("long start = Stopwatch.GetTimestamp();")
        read = reconnect.index("int read = stream.Read(", start)
        completed = reconnect.index("long completedTicks = Stopwatch.GetTimestamp() - start;", read)
        deadline = reconnect.index("completedTicks / frequency >= 10", completed)
        timing = reconnect.index("LINK_TCP_RX_TIMING cycle=", deadline)
        success = reconnect.index("LINK_TCP=PASS cycle=", timing)
        self.assertLess(start, read)
        self.assertLess(read, completed)
        self.assertLess(completed, deadline)
        self.assertLess(deadline, timing)
        self.assertLess(timing, success)
        self.assertIn("RemainingSocketReadTimeoutMs(\n                            elapsed, frequency, 10)", reconnect)
        self.assertIn("firstByteTicks = Stopwatch.GetTimestamp() - start;", reconnect)
        per_read = reconnect.index("Link TCP PCI/lease changed during RX", read)
        self.assertLess(read, per_read)
        self.assertLess(per_read, completed)
        # A transient PCI BAR remap or INTx route change between fragments
        # must not be hidden by a restored final snapshot.
        self.assertIn("IsE1000EPciMappingStable()", reconnect[read:per_read])
        self.assertIn("IsE1000EDriverKitStable(s_linkCycleResources, s_linkCycleIrqSources)",
                      reconnect[read:completed])
        self.assertIn("IsE1000ELeaseStable(s_linkCycleMac, s_linkCycleIp,",
                      reconnect[read:completed])
        self.assertEqual(reconnect.count("Link TCP total RX deadline exceeded"), 2)

    def test_n6_http_rx_latency_is_measured_not_claimed_as_irq(self):
        # Runtime telemetry must be measured on the guest using the same
        # monotonic clock as the bounded HTTP receive loop. It is NOT a
        # hardware ISR counter, and the PCAP gate remains mandatory.
        method = SOURCE.split("private static bool TryHttpExchange(", 1)[1]
        start = method.index("long rxStart = Stopwatch.GetTimestamp();")
        first = method.index("firstByteTicks = Stopwatch.GetTimestamp() - rxStart;", start)
        completed = method.index("long totalTicks = Stopwatch.GetTimestamp() - rxStart;", first)
        marker = method.index("HTTP_RX_TIMING attempt=", completed)
        success = method.index('string marker = attempt == 1', marker)
        self.assertLess(start, first)
        self.assertLess(first, completed)
        self.assertLess(completed, marker)
        self.assertLess(marker, success)
        self.assertIn("firstByteTicks < 0 || totalTicks < firstByteTicks", method)
        self.assertIn("totalTicks / rxFrequency >= 45", method)
        self.assertIn("(socket Read; ISR unproven)", method)
        validator = (ROOT / "tests/verify_3095_network_n6_post_http.py").read_text()
        self.assertIn("RX_TIMING = re.compile(", validator)
        self.assertIn("len(timings) != 6", validator)
        self.assertIn("first_byte_ms > total_ms or total_ms >= 45000", validator)
        self.assertIn("entries[attempt - 1][0] < index < lines.index(markers[attempt - 1])", validator)

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
        # The read-only DriverKit health snapshot is shared by N1-N6.
        # Only the N6 stress helpers remain behind the N6 preprocessor gate.
        self.assertIn("#endif\n\n        // Stock Cosmos", SOURCE)
        self.assertIn("#if ZONDERQ_NETWORK_N6_HTTP\n"
                      "        private static bool CheckE1000EDriverKitHealth", SOURCE)

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
        self.assertIn('NetworkAdapter leasedAdapter = NetworkManager.Primary;', SOURCE)
        self.assertIn('IPConfig? config = leasedAdapter.IPConfig;', SOURCE)
        self.assertIn('E1000E binding/link/MAC changed during DHCP', SOURCE)
        self.assertIn('Unexpected QEMU DHCP lease', SOURCE)
        dhcp = SOURCE.split('new DhcpClient().SendDiscoverPacket()', 1)[1].split(
            '#if ZONDERQ_NETWORK_N3_ICMP', 1)[0]
        for check in ('leasedAdapter.IsValid', 'leasedAdapter.Ready',
                      'leasedAdapter.LinkUp', 'leasedAdapter.MacAddress',
                      'config.Address.ToString() != "10.0.2.15"',
                      'config.SubnetMask.ToString() != "255.255.255.0"',
                      'config.DefaultGateway.ToString() != "10.0.2.2"'):
            self.assertIn(check, dhcp)
        self.assertLess(dhcp.index('Unexpected QEMU DHCP lease'),
                        dhcp.index('[NET3095-N2] DHCP=PASS ip='))
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
        self.assertIn('int[] frameLengths = { 60, 128, 512, 1024, 1514 };', SOURCE)
        self.assertIn('int frameLength = frameLengths[sequence % frameLengths.Length];', SOURCE)
        self.assertIn('burst[offset] = (byte)((sequence * 17 + offset) & 0xFF);', SOURCE)
        self.assertIn('expected_burst_frame(seen)',
                      (ROOT / 'tests/verify_3095_network_n6_burst.py').read_text())
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
            "if (!NetworkManager.Send(burst, frameLength))",
            "Thread.Sleep(5);",
        ):
            self.assertIn(term, burst)
        self.assertLess(burst.index("if ((sequence & 31) == 0)"),
                        burst.index("if (!NetworkManager.Send(burst, frameLength))"))
        # Five read-only PCI BAR/INTx checks at 0/32/64/96/128 TX.
        self.assertIn("ushort expectedPciCommand, ulong expectedPciBar0", burst)
        self.assertIn("byte expectedIrqLine, byte expectedIrqPin", burst)
        self.assertEqual(burst.count("E1000EPciIdentity.TryReadReady("), 2)
        self.assertEqual(burst.count("E1000EPciIdentity.TryReadLegacyIrqRoute("), 2)
        self.assertIn("command != expectedPciCommand || bar0 != expectedPciBar0", burst)
        self.assertIn("irqLine != expectedIrqLine || irqPin != expectedIrqPin", burst)
        self.assertIn("finalCommand != expectedPciCommand || finalBar0 != expectedPciBar0", burst)
        self.assertLess(burst.index("E1000EPciIdentity.TryReadReady("),
                        burst.index("if (!NetworkManager.Send(burst, frameLength))"))
        self.assertIn("IsE1000EDriverKitStable(expectedResources, expectedIrqSources)", burst)
        self.assertLess(burst.index("IsE1000EDriverKitStable(expectedResources, expectedIrqSources)"),
                        burst.index("if (!NetworkManager.Send(burst, frameLength))"))
        self.assertIn('TX_NIC_CHECK=PASS sent=128', burst)
        self.assertIn('IsE1000ELeaseStable(expectedMac, expectedIp,', burst)
        self.assertLess(burst.index('IsE1000ELeaseStable(expectedMac, expectedIp,'),
                        burst.index('TX_NIC_CHECK=PASS sent='))
        self.assertLess(burst.index('TX_NIC_CHECK=PASS sent='),
                        burst.index('if (!NetworkManager.Send(burst, frameLength))'))
        self.assertIn("verify_3095_network_n6_burst.py", SMOKE)
        self.assertIn("verify_3095_e1000e_dma_trace.py", SMOKE)
        self.assertIn("e1000e_tx_descr", SMOKE)
        self.assertIn("e1000e_rx_desc_buff_write", SMOKE)
        self.assertIn("e1000e_rx_written_to_guest", SMOKE)



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


    def test_n6_opt_in_link_recovery_requires_live_guest_and_wire_proof(self):
        # Host QMP acceptance alone is insufficient. The isolated kernel
        # must see both link transitions and successfully RX an ICMP reply.
        cycle = SOURCE.split("private static void PollLinkCycle()", 1)[1]
        cycle = cycle.split("protected override void Run()", 1)[0]
        for term in (
            "if (!adapter.LinkUp)", "LINK_DOWN_OBSERVED=YES",
            "if (!s_linkDownObserved)", "LINK_UP_OBSERVED=YES",
            "IsE1000EDriverKitStable(", "IsE1000ELeaseStable(",
            "ping.SendEcho(id: 0x3098, sequence: 1)",
            "ping.Receive(ref remote, 5000)",
            "LINK_RECOVERY_ICMP=PASS",
        ):
            self.assertIn(term, cycle)
        self.assertLess(cycle.index("LINK_DOWN_OBSERVED=YES"),
                        cycle.index("LINK_UP_OBSERVED=YES"))
        self.assertLess(cycle.index("LINK_UP_OBSERVED=YES"),
                        cycle.index("ping.SendEcho(id: 0x3098"))
        self.assertIn("s_linkCycleArmed = true;", SOURCE)
        self.assertIn("LINK2_DOWN_OBSERVED=YES", cycle)
        self.assertIn("LINK2_UP_OBSERVED=YES", cycle)
        self.assertIn("ping.SendEcho(id: 0x3099, sequence: 2)", cycle)
        self.assertIn("LINK2_RECOVERY_ICMP=PASS", cycle)
        self.assertIn("NET3095_LINK_CYCLES", SMOKE)
        self.assertIn("TryTcpReconnect(1)", SOURCE)
        self.assertIn("TryTcpReconnect(2)", SOURCE)
        self.assertIn("LINK_TCP=PASS cycle=", SOURCE)
        self.assertIn("verify_3095_network_n6_tcp_recovery.py", SMOKE)
        self.assertIn("--reconnects \"$LINK_CYCLES\"", SMOKE)
        self.assertIn("PollLinkCycle();", SOURCE)
        self.assertIn('NET3095_LINK_CYCLE', SMOKE)
        self.assertIn('network_3095_qmp_link.py', SMOKE)
        self.assertIn('verify_3095_network_n6_link_recovery.py', SMOKE)
        validator = (ROOT / "tests/verify_3095_network_n6_link_recovery.py").read_text()
        self.assertIn("validate_final(serial, pcap)", validator)
        self.assertIn("check_link_pair(list(pcap_frames(pcap)), guest)", validator)
        self.assertIn("len(recovery) != 2", validator)


    def test_n6_http_and_icmp_revalidate_pci_mapping(self):
        # Static gate only: actual RX/TX still requires independent QEMU PCAP.
        # PCI COMMAND/BAR0/INTx must remain unchanged at each HTTP read
        # and at all pre/post ICMP checkpoints, even if DriverKit is stable.
        helper = SOURCE.split("private static bool IsE1000EProbePciMappingStable(", 1)[1]
        helper = helper.split("private static bool CheckE1000ECheckpoint(", 1)[0]
        self.assertIn("E1000EPciIdentity.TryReadReady(", helper)
        self.assertIn("command == expectedCommand && bar0 == expectedBar0", helper)
        self.assertIn("E1000EPciIdentity.TryReadLegacyIrqRoute(", helper)
        self.assertIn("irqLine == expectedIrqLine && irqPin == expectedIrqPin", helper)
        self.assertNotIn("Write", helper)
        checkpoint = SOURCE.split("private static bool CheckE1000ECheckpoint(", 1)[1]
        checkpoint = checkpoint.split("private static bool TryHttpExchange(", 1)[0]
        self.assertLess(checkpoint.index("IsE1000EProbePciMappingStable("),
                        checkpoint.index("IsE1000EDriverKitStable("))
        for phase in ("post_before", "post_after", "final_before", "final_after"):
            self.assertIn(f'CheckE1000ECheckpoint("{phase}"', SOURCE)
        self.assertEqual(SOURCE.count("pciCommand, pciBar0, pciIrqLine, pciIrqPin))"), 5)
        http = SOURCE.split("private static bool TryHttpExchange(", 1)[1]
        http = http.split("private static bool TrySendBurst(", 1)[0]
        self.assertIn("HTTP PCI mapping invalid before attempt=", http)
        self.assertIn("HTTP PCI mapping changed after TX attempt=", http)
        self.assertIn("HTTP PCI mapping changed during RX attempt=", http)
        self.assertIn("HTTP PCI mapping changed at completion attempt=", http)
        self.assertIn("HTTP_PCI_STABLE=PASS attempt=", http)
        read = http.index("int read = stream.Read(")
        during = http.index("HTTP PCI mapping changed during RX attempt=", read)
        driver = http.index("HTTP DriverKit ownership changed during RX attempt=", during)
        complete = http.index("HTTP PCI mapping changed at completion attempt=", driver)
        passed = http.index("HTTP_PCI_STABLE=PASS attempt=", complete)
        self.assertLess(read, during)
        self.assertLess(during, driver)
        self.assertLess(driver, complete)
        self.assertLess(complete, passed)
        self.assertEqual(http.count("IsE1000EProbePciMappingStable("), 4)
        self.assertEqual(SOURCE.count("pciCommand, pciBar0, pciIrqLine, pciIrqPin,\n                             baselineResources, baselineIrqSources)"), 2)


if __name__ == "__main__":
    unittest.main(verbosity=2)
