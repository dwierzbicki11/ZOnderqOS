#if ZONDERQ_NETWORK_N1_PROBE
using System;
using System.Diagnostics;
using System.Threading;
using DiagnosticLog = Cosmos.Kernel.System.Diagnostics.Log;
using Cosmos.Kernel.System.Diagnostics;
using Cosmos.Kernel.System.Network;
using Sys = Cosmos.Kernel.System;

namespace ZonderqOS
{
    /// <summary>
    /// Isolated x64 network probe kernel. Never mounts storage, initializes
    /// user accounts or launches the GUI. Runs only in opted-in QEMU profile.
    /// </summary>
    public sealed class NetworkN1ProbeKernel : Sys.Kernel
    {
        private static void Log(string line)
        {
            DiagnosticLog.WriteString(line + "\n");
        }

        protected override void BeforeRun()
        {
            Log("[NET3095-N1] BOOT");
            if (!NetworkManager.IsEnabled || !NetworkManager.IsInitialized)
            {
                Log("[NET3095-N1][FAIL] Network manager not initialized");
                return;
            }

            int count = NetworkManager.DeviceCount;
            Log("[NET3095-N1] discovered=" + count);
            if (count != 1)
            {
                // Only one E1000E is configured in the isolated QEMU profile.
                Log("[NET3095-N1][FAIL] Expected exactly one E1000E registration");
                return;
            }

            int readyLinks = 0;
            for (int i = 0; i < count; i++)
            {
                NetworkAdapter adapter = NetworkManager.GetAdapter(i);
                if (!adapter.IsValid)
                {
                    Log("[NET3095-N1][FAIL] Invalid device index=" + i);
                    return;
                }

                string name = adapter.Name ?? "unknown";
                string mac = adapter.MacAddress?.ToString() ?? "unknown";
                bool ready = adapter.Ready;
                bool link = adapter.LinkUp;
                Log("[NET3095-N1] cpu=bsp adapter=" + i + " name=" + name +
                    " mac=" + mac + " ready=" + (ready ? "yes" : "no") +
                    " link=" + (link ? "up" : "down"));
                if (name == "e1000e" && ready && link)
                    readyLinks++;
            }

            if (readyLinks != 1)
            {
                Log("[NET3095-N1][FAIL] NIC registered but link not ready");
                return;
            }

            Log("[NET3095-N1] PASS READY_LINK=" + readyLinks);
            // Only an isolated QEMU build reaches this path. This is a raw
            // Ethernet test, not a DHCP request or an IP configuration.
            NetworkAdapter primary = NetworkManager.Primary;
            if (!primary.IsValid || primary.Name != "e1000e" ||
                !primary.Ready || !primary.LinkUp)
            {
                Log("[NET3095-N1][FAIL] Primary E1000E not ready");
                return;
            }

            string sourceMac = primary.MacAddress?.ToString() ?? "";
            byte[] frame = new byte[60]; // Minimum Ethernet frame excluding FCS.
            for (int i = 0; i < 6; i++)
                frame[i] = 0xFF; // Broadcast destination.
            if (!TryFillSourceMac(sourceMac, frame))
            {
                Log("[NET3095-N1][FAIL] Invalid source MAC");
                return;
            }

            frame[12] = 0x88;
            frame[13] = 0xB5; // Local experimental EtherType.
            byte[] payload = { 0x5A, 0x4F, 0x4E, 0x44, 0x45, 0x52, 0x51,
                               0x33, 0x30, 0x39, 0x35, 0x54, 0x58 }; // ZONDERQ3095TX
            for (int i = 0; i < payload.Length; i++)
                frame[14 + i] = payload[i];

            // Cross-check the live DriverKit binding against read-only PCI
            // configuration at QEMU 0000:00:02.0. The driver must still own an
            // Intel 82574L with MMIO and bus-master DMA enabled.
            var pciConfig = new ZonderqOS.Platform.X64.PciConfigIoAccessor();
            if (!ZonderqOS.Hardware.E1000EPciIdentity.TryReadReady(
                    pciConfig, out ushort pciCommand, out ulong pciBar0))
            {
                Log("[NET3095-N1][FAIL] E1000E PCI identity/MMIO/busmaster invalid before TX");
                return;
            }
            Log("[NET3095-N1] PCI_IDENTITY=PASS bdf=00:02.0 vendor=8086 device=10D3 command=0x" +
                pciCommand.ToString("X4") + " (read-only; DMA/ISR unproven)");

            // QEMU's isolated profile expects legacy INTx delivery. A PCI
            // line/pin and enabled INTx are only routing prerequisites, not
            // proof that Cosmos executed the guest ISR.
            if (!ZonderqOS.Hardware.E1000EPciIdentity.TryReadLegacyIrqRoute(
                    pciConfig, out byte pciIrqLine, out byte pciIrqPin))
            {
                Log("[NET3095-N1][FAIL] E1000E legacy PCI IRQ route invalid before TX");
                return;
            }
            Log("[NET3095-N1] PCI_IRQ_ROUTE=PASS line=" + pciIrqLine +
                " pin=" + pciIrqPin + " (read-only; guest ISR unproven)");

            // Prove the live PCI DriverKit binding, not just an adapter name.
            // IRQ source registration is not proof of ISR execution.
            if (!TryGetE1000EDriverKitHealth(out var n1DriverNode))
            {
                Log("[NET3095-N1][FAIL] E1000E PCI DriverKit binding invalid");
                return;
            }
            Log("[NET3095-N1] DRIVER_BOUND=PASS node=" + n1DriverNode.Path +
                " resources=" + n1DriverNode.HeldResourceCount +
                " irq_sources=" + n1DriverNode.InterruptCount +
                " handler_faults=0 isr_executions=unknown");
            bool queued = NetworkManager.Send(frame, frame.Length);
            Log("[NET3095-N1] TX_QUEUED=" + (queued ? "yes" : "no"));
            if (!queued)
            {
                Log("[NET3095-N1][FAIL] E1000E TX queue rejected the frame");
                return;
            }
            // Queueing is not proof of a live interface. Sample the NIC
            // again after Send() before any higher-level RX/TX tests begin.
            // The independent PCAP gate remains the actual frame TX proof.
            NetworkAdapter txAdapter = NetworkManager.Primary;
            if (!NetworkManager.IsEnabled || !NetworkManager.IsInitialized ||
                NetworkManager.DeviceCount != 1 || !txAdapter.IsValid ||
                txAdapter.Name != "e1000e" || !txAdapter.Ready ||
                !txAdapter.LinkUp ||
                (txAdapter.MacAddress?.ToString() ?? "") != sourceMac)
            {
                Log("[NET3095-N1][FAIL] E1000E binding/link/MAC changed during TX");
                return;
            }
            // A stable published adapter alone cannot detect resource/IRQ
            // ownership churn between the pre-TX snapshot and Send().
            if (!TryGetE1000EDriverKitHealth(out var txDriverNode) ||
                txDriverNode.Path != n1DriverNode.Path ||
                txDriverNode.HeldResourceCount != n1DriverNode.HeldResourceCount ||
                txDriverNode.InterruptCount != n1DriverNode.InterruptCount)
            {
                Log("[NET3095-N1][FAIL] E1000E DriverKit binding changed during TX");
                return;
            }
            // A queued frame is not enough: verify that the PCI command
            // and the assigned MMIO BAR0 are unchanged after the TX path.
            // This is read-only PCI configuration, not DMA completion proof.
            if (!ZonderqOS.Hardware.E1000EPciIdentity.TryReadReady(
                    pciConfig, out ushort txPciCommand, out ulong txPciBar0) ||
                txPciCommand != pciCommand || txPciBar0 != pciBar0)
            {
                Log("[NET3095-N1][FAIL] E1000E PCI identity/command changed during TX");
                return;
            }
            if (!ZonderqOS.Hardware.E1000EPciIdentity.TryReadLegacyIrqRoute(
                    pciConfig, out byte txIrqLine, out byte txIrqPin) ||
                txIrqLine != pciIrqLine || txIrqPin != pciIrqPin)
            {
                Log("[NET3095-N1][FAIL] E1000E legacy PCI IRQ route changed during TX");
                return;
            }
            Log("[NET3095-N1] PCI_IRQ_ROUTE_STABLE=PASS line=" + txIrqLine +
                " pin=" + txIrqPin + " (read-only; guest ISR unproven)");
            Log("[NET3095-N1] PCI_TX_STABLE=PASS command=0x" +
                txPciCommand.ToString("X4") + " (read-only; PCAP required for TX proof)");
            Log("[NET3095-N1] DHCP=NOT_ATTEMPTED IP=UNASSIGNED");
#if ZONDERQ_NETWORK_N2_DHCP
            // Opt-in second-stage RX proof. DHCP only runs in the isolated
            // serial QEMU probe, never in the production desktop.
            Log("[NET3095-N2] DHCP_ATTEMPT=START");
            if (!IsE1000EDriverKitSnapshotStable(n1DriverNode))
            {
                Log("[NET3095-N2][FAIL] E1000E DriverKit ownership invalid before DHCP");
                return;
            }
            try
            {
                int elapsed = new DhcpClient().SendDiscoverPacket();
                NetworkAdapter leasedAdapter = NetworkManager.Primary;
                IPConfig? config = leasedAdapter.IPConfig;
                if (elapsed < 0 || config == null ||
                    config.Address.IsZero || config.SubnetMask.IsZero ||
                    config.DefaultGateway.IsZero)
                {
                    Log("[NET3095-N2][FAIL] DHCP timed out or returned an invalid lease");
                    return;
                }
                // This isolated QEMU profile uses the fixed SLIRP 10.0.2.0/24
                // topology. Do not claim DHCP success if the original E1000E
                // disappeared, its MAC changed or the guest accepted an
                // unexpected address/mask/gateway. The independent PCAP gate
                // still proves the actual DORA packets on the wire.
                if (!NetworkManager.IsEnabled || !NetworkManager.IsInitialized ||
                     NetworkManager.DeviceCount != 1 ||
                     !leasedAdapter.IsValid || leasedAdapter.Name != "e1000e" ||
                    !leasedAdapter.Ready || !leasedAdapter.LinkUp ||
                    (leasedAdapter.MacAddress?.ToString() ?? "") != sourceMac)
                {
                    Log("[NET3095-N2][FAIL] E1000E binding/link/MAC changed during DHCP");
                    return;
                }
                if (config.Address.ToString() != "10.0.2.15" ||
                    config.SubnetMask.ToString() != "255.255.255.0" ||
                    config.DefaultGateway.ToString() != "10.0.2.2")
                {
                    Log("[NET3095-N2][FAIL] Unexpected QEMU DHCP lease");
                    return;
                }
                // DHCP can succeed while a NIC is rebound with the same MAC.
                // Require the original PCI DriverKit resource/IRQ ownership,
                // not just the published adapter and mutable IPConfig.
                if (!IsE1000EDriverKitSnapshotStable(n1DriverNode))
                {
                    Log("[NET3095-N2][FAIL] E1000E DriverKit ownership changed during DHCP");
                    return;
                }
#if ZONDERQ_NETWORK_N6_HTTP
                // Snapshot values before stress: IPConfig may be mutable and
                // re-reading the same object later would miss in-place changes.
                string initialIp = config.Address.ToString();
                string initialMask = config.SubnetMask.ToString();
                string initialGateway = config.DefaultGateway.ToString();
                // Snapshot DriverKit resource ownership before any N6 HTTP/TX
                // stress. An after-only "resources >= 1" check cannot detect
                // leaked PCI mappings or duplicate interrupt registrations.
                if (!TryGetE1000EDriverKitHealth(out var baselineDriverNode))
                {
                    Log("[NET3095-N6][FAIL] E1000E DriverKit baseline invalid");
                    return;
                }
                int baselineResources = baselineDriverNode.HeldResourceCount;
                int baselineIrqSources = baselineDriverNode.InterruptCount;
                Log("[NET3095-N6] DRIVER_BASELINE=PASS node=" + baselineDriverNode.Path +
                    " resources=" + baselineResources + " irq_sources=" +
                    baselineIrqSources + " handler_faults=0 isr_executions=unknown");
#endif
                // A successful DHCP exchange must not mask a PCI reconfiguration.
                // Read the actual NIC config again after RX/DMA traffic, rather
                // than trusting only the stable published DriverKit node.
                if (!ZonderqOS.Hardware.E1000EPciIdentity.TryReadReady(
                        pciConfig, out ushort dhcpPciCommand, out ulong dhcpPciBar0) ||
                    dhcpPciCommand != pciCommand || dhcpPciBar0 != pciBar0 ||
                    !ZonderqOS.Hardware.E1000EPciIdentity.TryReadLegacyIrqRoute(
                        pciConfig, out byte dhcpIrqLine, out byte dhcpIrqPin) ||
                    dhcpIrqLine != pciIrqLine || dhcpIrqPin != pciIrqPin)
                {
                    Log("[NET3095-N2][FAIL] E1000E PCI BAR/command/IRQ route changed during DHCP");
                    return;
                }
                Log("[NET3095-N2] DHCP=PASS ip=" + config.Address +
                    " mask=" + config.SubnetMask +
                    " gateway=" + config.DefaultGateway);
#if ZONDERQ_NETWORK_N3_ICMP
                Log("[NET3095-N3] ICMP_ATTEMPT=START");
                if (!IsE1000EDriverKitSnapshotStable(n1DriverNode))
                {
                    Log("[NET3095-N3][FAIL] E1000E DriverKit ownership invalid before ICMP");
                    return;
                }
                IcmpClient ping = new IcmpClient();
                try
                {
                    ping.Connect(new Address4(10, 0, 2, 2));
                    ping.SendEcho(id: 0x3095, sequence: 1);
                    EndPoint remote = new EndPoint(Address4.Zero, 0);
                    int elapsedMs = ping.Receive(ref remote, 5000);
                    if (elapsedMs < 0 || remote.Address.ToString() != "10.0.2.2")
                    {
                        Log("[NET3095-N3][FAIL] ICMP echo timeout or wrong peer");
                        return;
                    }
                    // A valid echo can arrive just before the NIC is rebound,
                    // the link drops or the original DHCP lease is replaced.
                    // Check the live adapter AFTER Receive(), not the mutable
                    // IPConfig object captured before the ICMP exchange.
                    NetworkAdapter icmpAdapter = NetworkManager.Primary;
                    IPConfig? icmpLease = icmpAdapter.IPConfig;
                    if (!NetworkManager.IsEnabled || !NetworkManager.IsInitialized ||
                        NetworkManager.DeviceCount != 1 ||
                         !icmpAdapter.IsValid || icmpAdapter.Name != "e1000e" ||
                        !icmpAdapter.Ready || !icmpAdapter.LinkUp ||
                        (icmpAdapter.MacAddress?.ToString() ?? "") != sourceMac ||
                        icmpLease == null ||
                        icmpLease.Address.ToString() != "10.0.2.15" ||
                        icmpLease.SubnetMask.ToString() != "255.255.255.0" ||
                        icmpLease.DefaultGateway.ToString() != "10.0.2.2")
                    {
                        Log("[NET3095-N3][FAIL] E1000E link/MAC/DHCP lease changed during ICMP echo");
                        return;
                    }
                    if (!IsE1000EDriverKitSnapshotStable(n1DriverNode))
                    {
                        Log("[NET3095-N3][FAIL] E1000E DriverKit ownership changed during ICMP");
                        return;
                    }
                    Log("[NET3095-N3] ICMP=PASS from=" + remote.Address);
                }
                finally
                {
                    ping.Close();
                }
#endif
#if ZONDERQ_NETWORK_N4_TCP
                // Bounded, deterministic TCP echo against a CI-owned host
                // listener reachable through QEMU user networking at 10.0.2.2.
                // This does not enable networking in the production desktop.
                Log("[NET3095-N4] TCP_ATTEMPT=START");
                if (!IsE1000ETcpPciStable(pciConfig, pciCommand, pciBar0, pciIrqLine, pciIrqPin))
                {
                    Log("[NET3095-N4][FAIL] E1000E PCI mapping invalid before TCP");
                    return;
                }
                if (!IsE1000EDriverKitSnapshotStable(n1DriverNode))
                {
                    Log("[NET3095-N4][FAIL] E1000E DriverKit ownership invalid before TCP");
                    return;
                }
                try
                {
                    using (var client = new System.Net.Sockets.TcpClient())
                    {
                        client.Connect(System.Net.IPAddress.Parse("10.0.2.2"), 18095);
                        client.Client.ReceiveTimeout = 5000;
                        client.Client.SendTimeout = 5000;
                        System.Net.Sockets.NetworkStream stream = client.GetStream();
                        byte[] request = System.Text.Encoding.ASCII.GetBytes("ZONDERQ3095-TCP-PING\n");
                        byte[] expected = System.Text.Encoding.ASCII.GetBytes("ZONDERQ3095-TCP-PONG\n");
                        stream.Write(request, 0, request.Length);
                        if (!IsE1000ETcpPciStable(pciConfig, pciCommand, pciBar0, pciIrqLine, pciIrqPin))
                        {
                            Log("[NET3095-N4][FAIL] E1000E PCI mapping changed after TCP TX");
                            return;
                        }
                        if (!IsE1000EDriverKitSnapshotStable(n1DriverNode))
                        {
                            Log("[NET3095-N4][FAIL] E1000E DriverKit ownership changed after TCP TX");
                            return;
                        }
                        byte[] response = new byte[expected.Length];
                        int received = 0;
                        // A five-second per-read socket timeout does not
                        // prevent a slow peer from keeping the TCP echo open
                        // indefinitely. Bound the entire response as well.
                        long echoStart = Stopwatch.GetTimestamp();
                        long echoFrequency = Stopwatch.Frequency;
                        if (echoFrequency <= 0 || echoStart <= 0)
                        {
                            Log("[NET3095-N4][FAIL] TCP echo monotonic clock unavailable");
                            return;
                        }
                        while (received < response.Length)
                        {
                            long echoElapsed = Stopwatch.GetTimestamp() - echoStart;
                            if (echoElapsed < 0 || echoElapsed / echoFrequency >= 20)
                            {
                                Log("[NET3095-N4][FAIL] TCP echo total deadline exceeded");
                                return;
                            }
                            // A 5s per-read timeout could overrun the 20s total
                            // deadline by almost 5s. Clamp the next blocking read
                            // to the remaining monotonic budget instead.
                            client.Client.ReceiveTimeout = RemainingSocketReadTimeoutMs(
                                echoElapsed, echoFrequency, 20);
                            int read = stream.Read(response, received, response.Length - received);
                            if (read <= 0)
                                break;
                            received += read;
                            // Each successful Read may follow another RX DMA
                            // completion. Detect resource/IRQ rebinding before
                            // accepting the next fragment.
                            if (!IsE1000ETcpPciStable(pciConfig, pciCommand, pciBar0, pciIrqLine, pciIrqPin))
                            {
                                Log("[NET3095-N4][FAIL] E1000E PCI mapping changed during TCP RX");
                                return;
                            }
                            if (!IsE1000EDriverKitSnapshotStable(n1DriverNode))
                            {
                                Log("[NET3095-N4][FAIL] E1000E DriverKit ownership changed during TCP RX");
                                return;
                            }
                            echoElapsed = Stopwatch.GetTimestamp() - echoStart;
                            if (echoElapsed < 0 || echoElapsed / echoFrequency >= 20)
                            {
                                Log("[NET3095-N4][FAIL] TCP echo total deadline exceeded");
                                return;
                            }
                        }
                        if (received != expected.Length)
                        {
                            Log("[NET3095-N4][FAIL] TCP reply truncated");
                            return;
                        }
                        for (int i = 0; i < expected.Length; i++)
                        {
                            if (response[i] != expected[i])
                            {
                                Log("[NET3095-N4][FAIL] TCP reply mismatch");
                                return;
                            }
                        }
                        // A correct echo can arrive just before a NIC rebind,
                        // link loss or DHCP reset. Sample live adapter state
                        // after the last Read and byte comparison; never use
                        // the original mutable IPConfig object as the proof.
                        NetworkAdapter echoAdapter = NetworkManager.Primary;
                        IPConfig? echoLease = echoAdapter.IPConfig;
                        if (!NetworkManager.IsEnabled || !NetworkManager.IsInitialized ||
                            NetworkManager.DeviceCount != 1 ||
                         !echoAdapter.IsValid || echoAdapter.Name != "e1000e" ||
                            !echoAdapter.Ready || !echoAdapter.LinkUp ||
                            (echoAdapter.MacAddress?.ToString() ?? "") != sourceMac ||
                            echoLease == null ||
                            echoLease.Address.ToString() != "10.0.2.15" ||
                            echoLease.SubnetMask.ToString() != "255.255.255.0" ||
                            echoLease.DefaultGateway.ToString() != "10.0.2.2")
                        {
                            Log("[NET3095-N4][FAIL] E1000E link/MAC/DHCP lease changed during TCP echo");
                            return;
                        }
                        if (!IsE1000ETcpPciStable(pciConfig, pciCommand, pciBar0, pciIrqLine, pciIrqPin))
                        {
                            Log("[NET3095-N4][FAIL] E1000E PCI mapping changed at TCP completion");
                            return;
                        }
                        if (!IsE1000EDriverKitSnapshotStable(n1DriverNode))
                        {
                            Log("[NET3095-N4][FAIL] E1000E DriverKit ownership changed at TCP completion");
                            return;
                        }
                        Log("[NET3095-N4] TCP=PASS from=10.0.2.2 port=18095 bytes=" + received);
                    }
                }
                catch (Exception tcpException)
                {
                    Log("[NET3095-N4][FAIL] TCP exception " + tcpException.GetType().Name);
                    return;
                }
#endif
#if ZONDERQ_NETWORK_N5_DNS
                // Query the built-in QEMU SLIRP DNS proxy. The 'localhost'
                // answer is deterministic on the CI host, so this proof
                // needs no external DNS connectivity or privileged listener.
                Log("[NET3095-N5] DNS_ATTEMPT=START");
                // DNS is a separate RX path: reject a changed DMA/INTx PCI mapping
                // before sending the query, not just a changed DriverKit node.
                if (!IsE1000ETcpPciStable(pciConfig, pciCommand, pciBar0, pciIrqLine, pciIrqPin))
                {
                    Log("[NET3095-N5][FAIL] E1000E PCI mapping invalid before DNS");
                    return;
                }
                if (!IsE1000EDriverKitSnapshotStable(n1DriverNode))
                {
                    Log("[NET3095-N5][FAIL] E1000E DriverKit ownership invalid before DNS");
                    return;
                }
                try
                {
                    using (var dns = new DnsClient())
                    {
                        dns.Connect(new Address4(10, 0, 2, 3));
                        dns.SendQuery("localhost");
                        Address answer = dns.Receive(5000);
                        if (answer == null || answer.ToString() != "127.0.0.1")
                        {
                            Log("[NET3095-N5][FAIL] DNS response missing or unexpected");
                            return;
                        }
                        // A DNS answer alone is not a stable RX proof: the
                        // adapter could have been removed, rebound or lost its
                        // original DHCP lease while Receive() was blocking.
                        // Re-read live state rather than trusting the mutable
                        // IPConfig object captured before the DNS request.
                        NetworkAdapter dnsAdapter = NetworkManager.Primary;
                        IPConfig? dnsLease = dnsAdapter.IPConfig;
                        if (!NetworkManager.IsEnabled || !NetworkManager.IsInitialized ||
                            NetworkManager.DeviceCount != 1 ||
                         !dnsAdapter.IsValid || dnsAdapter.Name != "e1000e" ||
                            !dnsAdapter.Ready || !dnsAdapter.LinkUp ||
                            (dnsAdapter.MacAddress?.ToString() ?? "") != sourceMac ||
                            dnsLease == null ||
                            dnsLease.Address.ToString() != "10.0.2.15" ||
                            dnsLease.SubnetMask.ToString() != "255.255.255.0" ||
                            dnsLease.DefaultGateway.ToString() != "10.0.2.2")
                        {
                            Log("[NET3095-N5][FAIL] E1000E link/MAC/DHCP lease changed during DNS");
                            return;
                        }
                        // A successful DNS reply does not prove PCI ownership
                        // survived RX DMA. Re-read BAR0, command and INTx route.
                        if (!IsE1000ETcpPciStable(pciConfig, pciCommand, pciBar0, pciIrqLine, pciIrqPin))
                        {
                            Log("[NET3095-N5][FAIL] E1000E PCI mapping changed during DNS");
                            return;
                        }
                        if (!IsE1000EDriverKitSnapshotStable(n1DriverNode))
                        {
                            Log("[NET3095-N5][FAIL] E1000E DriverKit ownership changed during DNS");
                            return;
                        }
                        Log("[NET3095-N5] DNS=PASS host=localhost ip=127.0.0.1 server=10.0.2.3");
                    }
                }
                catch (Exception dnsException)
                {
                    Log("[NET3095-N5][FAIL] DNS exception " + dnsException.GetType().Name);
                    return;
                }
#endif
#if ZONDERQ_NETWORK_N6_HTTP
                // N6 proves HTTP/1.0 request/response over a second TCP socket.
                // This is deliberately cleartext HTTP, not HTTPS/TLS.
                Log("[NET3095-N6] HTTP_ATTEMPT=START");
                try
                {
                    // Two independent TCP connections exercise RX descriptor reuse and socket teardown.
                    for (int attempt = 1; attempt <= 2; attempt++)
                    {
                        if (!TryHttpExchange(attempt, sourceMac, initialIp, initialMask, initialGateway,
                             pciCommand, pciBar0, pciIrqLine, pciIrqPin,
                             baselineResources, baselineIrqSources))
                            return;
                    }
                }
                catch (Exception httpException)
                {
                    Log("[NET3095-N6][FAIL] HTTP exception " + httpException.GetType().Name);
                    return;
                }
                // Opt-in 128-frame raw TX after two completed HTTP RX sessions.
                // Each distinct frame must be independently observed in QEMU
                // filter-dump; successful queueing alone is NOT a DMA proof.
                Log("[NET3095-N6] TX_BURST_ATTEMPT=START count=128");
                if (!TrySendBurst(frame, sourceMac, initialIp, initialMask,
                        initialGateway, baselineResources, baselineIrqSources,
                        pciCommand, pciBar0, pciIrqLine, pciIrqPin))
                {
                    Log("[NET3095-N6][FAIL] E1000E raw TX burst queue rejected");
                    return;
                }
                Log("[NET3095-N6] TX_BURST=QUEUED count=128");
                // Five read-only DriverKit checks (0/32/64/96/128 TX)
                // confirm stable PCI/IRQ ownership during descriptor reuse.
                // QEMU PCAP remains the only evidence of frames on the wire.
                Log("[NET3095-N6] TX_DRIVER_STABLE=PASS count=128 checks=5 resources=" +
                    baselineResources + " irq_sources=" + baselineIrqSources);
                // Verify that RX and the IPv4/ICMP stack still work after the
                // 128 TX submissions, including repeated descriptor reuse. The
                // independent PCAP gate must see this request and its reply
                // strictly after all 128 raw TX frames, not just a serial PASS.
                Log("[NET3095-N6] POST_BURST_ICMP_ATTEMPT=START");
                try
                {
                    // Four independent echo round trips exercise RX after
                    // 128 TX submissions. PCAP validates every sequence.
                    for (ushort sequence = 2; sequence <= 5; sequence++)
                    {
                        if (!CheckE1000ECheckpoint("post_before", sequence, sourceMac,
                            initialIp, initialMask, initialGateway,
                            baselineResources, baselineIrqSources,
                            pciCommand, pciBar0, pciIrqLine, pciIrqPin))
                            return;
                        IcmpClient postBurstPing = new IcmpClient();
                        try
                        {
                            postBurstPing.Connect(new Address4(10, 0, 2, 2));
                            postBurstPing.SendEcho(id: 0x3096, sequence: sequence);
                            EndPoint remote = new EndPoint(Address4.Zero, 0);
                            int elapsedMs = postBurstPing.Receive(ref remote, 5000);
                            if (elapsedMs < 0 || remote.Address.ToString() != "10.0.2.2")
                            {
                                Log("[NET3095-N6][FAIL] post-burst ICMP timeout or wrong peer seq=" + sequence);
                                return;
                            }
                            // Guest-observed receive latency complements PCAP
                            // RTT, but does not establish ISR execution.
                            Log("[NET3095-N6] POST_BURST_ICMP_RTT seq=" + sequence +
                                " elapsed_ms=" + elapsedMs);
                        }
                        finally
                        {
                            postBurstPing.Close();
                        }
                        if (!CheckE1000ECheckpoint("post_after", sequence, sourceMac,
                            initialIp, initialMask, initialGateway,
                            baselineResources, baselineIrqSources,
                            pciCommand, pciBar0, pciIrqLine, pciIrqPin))
                            return;
                    }
                    Log("[NET3095-N6] POST_BURST_ICMP=PASS from=10.0.2.2 count=4");
                }
                catch (Exception postBurstException)
                {
                    Log("[NET3095-N6][FAIL] post-burst ICMP exception " +
                        postBurstException.GetType().Name);
                    return;
                }
                // Four independent full HTTP RX streams must succeed after all TX
                // ring cycles and the four ICMP replies. Repeated fresh TCP
                // sessions exercise E1000E RX descriptor reuse and teardown.
                // QEMU PCAP, not queue/serial success alone, proves each one.
                Log("[NET3095-N6] POST_BURST_HTTP_ATTEMPT=START");
                try
                {
                    for (int attempt = 3; attempt <= 6; attempt++)
                    {
                        if (!TryHttpExchange(attempt, sourceMac, initialIp, initialMask, initialGateway,
                             pciCommand, pciBar0, pciIrqLine, pciIrqPin,
                             baselineResources, baselineIrqSources))
                            return;
                    }
                }
                catch (Exception postHttpException)
                {
                    Log("[NET3095-N6][FAIL] post-burst HTTP exception " +
                        postHttpException.GetType().Name);
                    return;
                }
                // Final independent RX proof after all six HTTP socket closes.
                // The QEMU PCAP gate must correlate this echo and its reply
                // strictly AFTER the sixth TCP FIN/ACK, not merely trust serial.
                Log("[NET3095-N6] FINAL_ICMP_ATTEMPT=START");
                try
                {
                    if (!CheckE1000ECheckpoint("final_before", 1, sourceMac,
                        initialIp, initialMask, initialGateway,
                        baselineResources, baselineIrqSources,
                        pciCommand, pciBar0, pciIrqLine, pciIrqPin))
                        return;
                    IcmpClient finalPing = new IcmpClient();
                    try
                    {
                        finalPing.Connect(new Address4(10, 0, 2, 2));
                        finalPing.SendEcho(id: 0x3097, sequence: 1);
                        EndPoint finalRemote = new EndPoint(Address4.Zero, 0);
                        int finalElapsedMs = finalPing.Receive(ref finalRemote, 5000);
                        if (finalElapsedMs < 0 || finalRemote.Address.ToString() != "10.0.2.2")
                        {
                            Log("[NET3095-N6][FAIL] final ICMP timeout or wrong peer");
                            return;
                        }
                        Log("[NET3095-N6] FINAL_ICMP_RTT seq=1 elapsed_ms=" + finalElapsedMs);
                    }
                    finally
                    {
                        finalPing.Close();
                    }
                    if (!CheckE1000ECheckpoint("final_after", 1, sourceMac,
                        initialIp, initialMask, initialGateway,
                        baselineResources, baselineIrqSources,
                        pciCommand, pciBar0, pciIrqLine, pciIrqPin))
                        return;
                    Log("[NET3095-N6] FINAL_ICMP=PASS from=10.0.2.2 seq=1");
                }
                catch (Exception finalIcmpException)
                {
                    Log("[NET3095-N6][FAIL] final ICMP exception " +
                        finalIcmpException.GetType().Name);
                    return;
                }
                // Read-only post-stress adapter check: catch a dropped link,
                // lost binding or changed MAC before reporting N6 success.
                NetworkAdapter postStress = NetworkManager.Primary;
                if (!NetworkManager.IsEnabled || !NetworkManager.IsInitialized ||
                     NetworkManager.DeviceCount != 1 ||
                     !postStress.IsValid || postStress.Name != "e1000e" ||
                    !postStress.Ready || !postStress.LinkUp ||
                    (postStress.MacAddress?.ToString() ?? "") != sourceMac)
                {
                    Log("[NET3095-N6][FAIL] E1000E link/MAC changed after TX/RX stress");
                    return;
                }
                Log("[NET3095-N6] POST_STRESS_LINK=PASS mac=" + sourceMac);
                // Link/MAC can remain valid while the DHCP lease disappears.
                // Inspect the original address, mask and gateway read-only.
                IPConfig? postStressConfig = postStress.IPConfig;
                if (postStressConfig == null ||
                    postStressConfig.Address.ToString() != initialIp ||
                    postStressConfig.SubnetMask.ToString() != initialMask ||
                    postStressConfig.DefaultGateway.ToString() != initialGateway)
                {
                    Log("[NET3095-N6][FAIL] IPv4 lease changed after TX/RX stress");
                    return;
                }
                Log("[NET3095-N6] POST_STRESS_IPV4=PASS ip=" + postStressConfig.Address +
                    " mask=" + postStressConfig.SubnetMask +
                    " gateway=" + postStressConfig.DefaultGateway);
                // A driver can retain its published MAC, lease and IRQ
                // registrations while its PCI MMIO mapping changes. Compare
                // the actual BAR0 and command register against N1's original
                // pre-TX snapshot after the full N6 TX/RX workload.
                if (!ZonderqOS.Hardware.E1000EPciIdentity.TryReadReady(
                        pciConfig, out ushort stressPciCommand, out ulong stressPciBar0) ||
                    stressPciCommand != pciCommand || stressPciBar0 != pciBar0)
                {
                    Log("[NET3095-N6][FAIL] E1000E PCI command/BAR0 changed after TX/RX stress");
                    return;
                }
                if (!ZonderqOS.Hardware.E1000EPciIdentity.TryReadLegacyIrqRoute(
                        pciConfig, out byte stressIrqLine, out byte stressIrqPin) ||
                    stressIrqLine != pciIrqLine || stressIrqPin != pciIrqPin)
                {
                    Log("[NET3095-N6][FAIL] E1000E legacy PCI IRQ route changed after TX/RX stress");
                    return;
                }
                if (!CheckE1000EDriverKitHealth(baselineResources, baselineIrqSources))
                {
                    Log("[NET3095-N6][FAIL] E1000E DriverKit health invalid after TX/RX stress");
                    return;
                }
                // Only the isolated N6 kernel arms this read-only link-flap
                // diagnostic. The ordinary N6 test never changes QEMU's link;
                // an opt-in host QMP test may drive a down/up transition.
                s_linkCycleMac = sourceMac;
                s_linkCycleIp = initialIp;
                s_linkCycleMask = initialMask;
                s_linkCycleGateway = initialGateway;
                s_linkCycleResources = baselineResources;
                s_linkCycleIrqSources = baselineIrqSources;
                s_linkCyclePciCommand = pciCommand;
                s_linkCyclePciBar0 = pciBar0;
                s_linkCycleIrqLine = pciIrqLine;
                s_linkCycleIrqPin = pciIrqPin;
                s_linkCycleNumber = 1;
                s_linkDownObserved = false;
                s_linkRecoveryAttempted = false;
                s_linkCycleArmed = true;
                Log("[NET3095-N6] LINK_CYCLE_ARMED=YES");
#endif
            }
            catch (Exception ex)
            {
                Log("[NET3095-N2][FAIL] DHCP exception " + ex.GetType().Name);
            }
#endif
        }

#if ZONDERQ_NETWORK_N4_TCP
        // Socket ReceiveTimeout applies to ONE Read(), not a whole response.
        // The caller checks the monotonic deadline before and after Read().
        // This helper only limits how long the next blocking call may wait.
        // Return at least 1ms near expiry: Socket timeout 0 means infinite.
        private static int RemainingSocketReadTimeoutMs(long elapsedTicks,
            long frequency, int deadlineSeconds)
        {
            long seconds = elapsedTicks / frequency;
            long fractionalTicks = elapsedTicks % frequency;
            long elapsedMs = seconds * 1000 + (fractionalTicks * 1000) / frequency;
            long remainingMs = (long)deadlineSeconds * 1000 - elapsedMs;
            if (remainingMs <= 1)
                return 1;
            return (int)(remainingMs < 5000 ? remainingMs : 5000);
        }
#endif

        // Read-only PCI identity, DMA command and INTx route check for TCP.
        // This is not evidence of a guest ISR invocation.
        private static bool IsE1000ETcpPciStable(
            ZonderqOS.Hardware.IPciConfigAccessor config,
            ushort expectedCommand, ulong expectedBar0,
            byte expectedIrqLine, byte expectedIrqPin)
        {
            return ZonderqOS.Hardware.E1000EPciIdentity.TryReadReady(
                    config, out ushort command, out ulong bar0) &&
                command == expectedCommand && bar0 == expectedBar0 &&
                ZonderqOS.Hardware.E1000EPciIdentity.TryReadLegacyIrqRoute(
                    config, out byte irqLine, out byte irqPin) &&
                irqLine == expectedIrqLine && irqPin == expectedIrqPin;
        }

        // Stock Cosmos 3.0.95 exposes DriverKit node snapshots, but NOT the
        // internal E1000EState.InterruptCount. A zero handler-fault count
        // therefore cannot prove that an ISR ran; the driver also polls.
        // Keep this check read-only and confined to isolated N1-N6 kernels.
        private static bool TryGetE1000EDriverKitHealth(out DeviceNodeInfo candidate)
        {
            candidate = default;
            bool found = false;
            for (int i = 0; i < DriverDiagnostics.NodeCount; i++)
            {
                if (!DriverDiagnostics.TryGetNode(i, out var node))
                    return false;
                if (node.DriverName != "E1000EDriver")
                    continue;
                if (found)
                    return false; // An ambiguous second E1000E is not a proof.
                candidate = node;
                found = true;
            }

            return found && candidate.BusName == "pci" &&
                candidate.State == DeviceNodeState.Bound &&
                candidate.Path == "pci:0000:00:02.0" &&
                candidate.FaultCount == 0 && candidate.HeldResourceCount >= 1 &&
                candidate.PublishedDeviceCount == 1 && candidate.InterruptCount >= 1;
        }

        // Snapshot comparison is read-only. DriverDiagnostics.InterruptCount
        // counts registered IRQ sources, NOT actual guest ISR invocations.
        // Use the N1 binding as the immutable baseline for N2-N5.
        private static bool IsE1000EDriverKitSnapshotStable(DeviceNodeInfo expected)
        {
            return TryGetE1000EDriverKitHealth(out var current) &&
                current.Path == expected.Path &&
                current.HeldResourceCount == expected.HeldResourceCount &&
                current.InterruptCount == expected.InterruptCount;
        }

#if ZONDERQ_NETWORK_N6_HTTP
        private static bool CheckE1000EDriverKitHealth(int expectedResources, int expectedIrqSources)
        {
            if (!TryGetE1000EDriverKitHealth(out var candidate) ||
                candidate.HeldResourceCount != expectedResources ||
                candidate.InterruptCount != expectedIrqSources)
                return false;

            // InterruptCount here is the number of PCI interrupt SOURCES,
            // not E1000EState.InterruptCount (actual ISR invocations).
            Log("[NET3095-N6] DRIVER_HEALTH=PASS node=" + candidate.Path +
                " driver=E1000EDriver resources=" + candidate.HeldResourceCount +
                " published=" + candidate.PublishedDeviceCount +
                " irq_sources=" + candidate.InterruptCount +
                " handler_faults=0 isr_executions=unknown");
            return true;
        }

        // Check the live, published NIC and the ORIGINAL DHCP values during
        // each HTTP RX stream, not just after all stress has completed. A
        // mutable IPConfig instance must not be used as its own baseline.
        private static bool IsE1000ELeaseStable(string expectedMac, string expectedIp,
            string expectedMask, string expectedGateway)
        {
            if (!NetworkManager.IsEnabled || !NetworkManager.IsInitialized ||
                NetworkManager.DeviceCount != 1)
                return false;
            NetworkAdapter adapter = NetworkManager.Primary;
            if (!adapter.IsValid || adapter.Name != "e1000e" ||
                !adapter.Ready || !adapter.LinkUp ||
                (adapter.MacAddress?.ToString() ?? "") != expectedMac)
                return false;
            IPConfig? lease = adapter.IPConfig;
            return lease != null && lease.Address.ToString() == expectedIp &&
                lease.SubnetMask.ToString() == expectedMask &&
                lease.DefaultGateway.ToString() == expectedGateway;
        }

        private static bool IsE1000EDriverKitStable(int expectedResources, int expectedIrqSources)
        {
            return TryGetE1000EDriverKitHealth(out var node) &&
                node.HeldResourceCount == expectedResources &&
                node.InterruptCount == expectedIrqSources;
        }

        // Verify the SAME PCI MMIO/DMA assignment and legacy INTx route
        // throughout an opt-in N6 receive. A stable DriverKit source count
        // cannot detect a BAR remap or a switch to MSI mid-stream. This only
        // reads PCI config; it does not access NIC MMIO or prove guest ISR.
        private static bool IsE1000EProbePciMappingStable(
            ZonderqOS.Hardware.IPciConfigAccessor config,
            ushort expectedCommand, ulong expectedBar0,
            byte expectedIrqLine, byte expectedIrqPin)
        {
            return ZonderqOS.Hardware.E1000EPciIdentity.TryReadReady(
                    config, out ushort command, out ulong bar0) &&
                command == expectedCommand && bar0 == expectedBar0 &&
                ZonderqOS.Hardware.E1000EPciIdentity.TryReadLegacyIrqRoute(
                    config, out byte irqLine, out byte irqPin) &&
                irqLine == expectedIrqLine && irqPin == expectedIrqPin;
        }

        // Verify read-only PCI/IRQ ownership AND the original DHCP lease
        // around each post-burst ICMP exchange. This catches transient
        // unbind/link/lease churn that a final snapshot could miss. Neither
        // the source count nor a successful ICMP reply proves ISR execution.
        private static bool CheckE1000ECheckpoint(string phase, ushort sequence,
            string expectedMac, string expectedIp, string expectedMask,
            string expectedGateway, int expectedResources, int expectedIrqSources,
            ushort expectedPciCommand, ulong expectedPciBar0,
            byte expectedIrqLine, byte expectedIrqPin)
        {
            var pciConfig = new ZonderqOS.Platform.X64.PciConfigIoAccessor();
            if (!IsE1000EProbePciMappingStable(pciConfig, expectedPciCommand,
                    expectedPciBar0, expectedIrqLine, expectedIrqPin) ||
                !IsE1000EDriverKitStable(expectedResources, expectedIrqSources) ||
                !IsE1000ELeaseStable(expectedMac, expectedIp, expectedMask, expectedGateway))
            {
                Log("[NET3095-N6][FAIL] ICMP driver/link/lease unstable phase=" +
                    phase + " seq=" + sequence);
                return false;
            }
            Log("[NET3095-N6] ICMP_DRIVER_CHECK=PASS phase=" + phase +
                " seq=" + sequence + " resources=" + expectedResources +
                " irq_sources=" + expectedIrqSources);
            return true;
        }

        private static bool TryHttpExchange(int attempt, string expectedMac,
            string expectedIp, string expectedMask, string expectedGateway,
            ushort expectedPciCommand, ulong expectedPciBar0,
            byte expectedIrqLine, byte expectedIrqPin,
            int expectedResources, int expectedIrqSources)
        {
            var pciConfig = new ZonderqOS.Platform.X64.PciConfigIoAccessor();
            if (!IsE1000EProbePciMappingStable(pciConfig, expectedPciCommand,
                    expectedPciBar0, expectedIrqLine, expectedIrqPin))
            {
                Log("[NET3095-N6][FAIL] HTTP PCI mapping invalid before attempt=" + attempt);
                return false;
            }
            if (!IsE1000EDriverKitStable(expectedResources, expectedIrqSources))
            {
                Log("[NET3095-N6][FAIL] HTTP DriverKit ownership invalid before attempt=" + attempt);
                return false;
            }
            if (!IsE1000ELeaseStable(expectedMac, expectedIp, expectedMask, expectedGateway))
            {
                Log("[NET3095-N6][FAIL] HTTP NIC/lease invalid before attempt=" + attempt);
                return false;
            }
            using (var http = new System.Net.Sockets.TcpClient())
            {
                http.Connect(System.Net.IPAddress.Parse("10.0.2.2"), 18096);
                http.Client.ReceiveTimeout = 5000;
                http.Client.SendTimeout = 5000;
                System.Net.Sockets.NetworkStream stream = http.GetStream();
                byte[] request = System.Text.Encoding.ASCII.GetBytes(
                    "GET /health HTTP/1.0\r\nHost: 10.0.2.2\r\nConnection: close\r\n" +
                    "X-Zonderq-Attempt: " + attempt + "\r\n\r\n");
                byte[] prefix = System.Text.Encoding.ASCII.GetBytes(
                    "HTTP/1.0 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 4116\r\nConnection: close\r\n\r\nZONDERQ3095-HTTP-OK\n");
                byte[] expected = new byte[prefix.Length + 4096];
                for (int i = 0; i < prefix.Length; i++)
                    expected[i] = prefix[i];
                // Match the loopback-only host fixture byte for byte. Vary
                // every offset within each 1 KiB segment, not just the
                // segment's first byte: a shifted/stale RX DMA fragment
                // must not pass simply because its block was uniform.
                for (int offset = 0; offset < 4096; offset++)
                    expected[prefix.Length + offset] = (byte)(33 +
                        ((offset * 37 + (offset / 90) * 11 +
                          (offset / 1024) * 23 + attempt * 29) % 90));
                stream.Write(request, 0, request.Length);
                if (!IsE1000EProbePciMappingStable(pciConfig, expectedPciCommand,
                        expectedPciBar0, expectedIrqLine, expectedIrqPin))
                {
                    Log("[NET3095-N6][FAIL] HTTP PCI mapping changed after TX attempt=" + attempt);
                    return false;
                }
                if (!IsE1000EDriverKitStable(expectedResources, expectedIrqSources))
                {
                    Log("[NET3095-N6][FAIL] HTTP DriverKit ownership changed after TX attempt=" + attempt);
                    return false;
                }
                if (!IsE1000ELeaseStable(expectedMac, expectedIp, expectedMask, expectedGateway))
                {
                    Log("[NET3095-N6][FAIL] HTTP NIC/lease changed after TX attempt=" + attempt);
                    return false;
                }
                byte[] response = new byte[expected.Length];
                int received = 0;
                int readCalls = 0;
                int largestRead = 0;
                long firstByteTicks = -1;
                // ReceiveTimeout bounds each individual blocking Read, but a
                // peer sending one byte every few seconds can keep resetting
                // that timeout. Bound the complete response independently.
                // Stopwatch is monotonic; use division rather than multiplying
                // an arbitrary runtime frequency by the deadline.
                long rxStart = Stopwatch.GetTimestamp();
                long rxFrequency = Stopwatch.Frequency;
                if (rxFrequency <= 0 || rxStart <= 0)
                {
                    Log("[NET3095-N6][FAIL] HTTP RX monotonic clock unavailable");
                    return false;
                }
                while (received < response.Length)
                {
                    long rxElapsed = Stopwatch.GetTimestamp() - rxStart;
                    if (rxElapsed < 0 || rxElapsed / rxFrequency >= 45)
                    {
                        Log("[NET3095-N6][FAIL] HTTP RX total deadline exceeded attempt=" + attempt);
                        return false;
                    }
                    // An isolated probe must not wait indefinitely for a peer
                    // trickling one byte per timed Read(). Real QEMU typically
                    // completes this 4202-byte fixture in about six reads.
                    // Cap successful chunks as well as the socket timeout.
                    if (readCalls >= 64)
                    {
                        Log("[NET3095-N6][FAIL] HTTP response fragmented into too many reads");
                        return false;
                    }
                    // Keep each blocking read within the remaining 45s RX
                    // budget; successful short reads must not reset it.
                    http.Client.ReceiveTimeout = RemainingSocketReadTimeoutMs(
                        rxElapsed, rxFrequency, 45);
                    int read = stream.Read(response, received, response.Length - received);
                    if (read <= 0)
                        break;
                    readCalls++;
                    if (firstByteTicks < 0)
                        firstByteTicks = Stopwatch.GetTimestamp() - rxStart;
                    if (read > largestRead)
                        largestRead = read;
                    received += read;
                    // A single blocking Read may cross the total deadline.
                    // Recheck after the read as well as before the next one.
                    rxElapsed = Stopwatch.GetTimestamp() - rxStart;
                    if (rxElapsed < 0 || rxElapsed / rxFrequency >= 45)
                    {
                        Log("[NET3095-N6][FAIL] HTTP RX total deadline exceeded attempt=" + attempt);
                        return false;
                    }
                    // Catch PCI BAR/INTx changes even if DriverKit still reports the
                    // original resources and the DHCP lease remains stable.
                    if (!IsE1000EProbePciMappingStable(pciConfig, expectedPciCommand,
                            expectedPciBar0, expectedIrqLine, expectedIrqPin))
                    {
                        Log("[NET3095-N6][FAIL] HTTP PCI mapping changed during RX attempt=" + attempt);
                        return false;
                    }
                    // Source registration is NOT an ISR execution counter.
                    if (!IsE1000EDriverKitStable(expectedResources, expectedIrqSources))
                    {
                        Log("[NET3095-N6][FAIL] HTTP DriverKit ownership changed during RX attempt=" + attempt);
                        return false;
                    }
                    if (!IsE1000ELeaseStable(expectedMac, expectedIp, expectedMask, expectedGateway))
                    {
                        Log("[NET3095-N6][FAIL] HTTP NIC/lease changed during RX attempt=" + attempt);
                        return false;
                    }
                }
                if (received != expected.Length)
                {
                    Log("[NET3095-N6][FAIL] HTTP response truncated");
                    return false;
                }
                for (int i = 0; i < expected.Length; i++)
                {
                    if (response[i] != expected[i])
                    {
                        Log("[NET3095-N6][FAIL] HTTP status/headers/body mismatch");
                        return false;
                    }
                }
                // A byte-exact TCP response is not sufficient if the PCI MMIO BAR or
                // INTx route changed before the per-stream PASS markers.
                if (!IsE1000EProbePciMappingStable(pciConfig, expectedPciCommand,
                        expectedPciBar0, expectedIrqLine, expectedIrqPin))
                {
                    Log("[NET3095-N6][FAIL] HTTP PCI mapping changed at completion attempt=" + attempt);
                    return false;
                }
                // Sample DriverKit ownership after EACH RX stream.
                if (!IsE1000EDriverKitStable(expectedResources, expectedIrqSources))
                {
                    Log("[NET3095-N6][FAIL] HTTP DriverKit ownership changed during attempt=" + attempt);
                    return false;
                }
                // The last RX completion may race a link drop, NIC rebind or
                // DHCP lease mutation before the per-stream PASS markers.
                // Recheck the original MAC/link/lease AFTER exact payload
                // verification, even when the response arrived in one read.
                if (!IsE1000ELeaseStable(expectedMac, expectedIp, expectedMask, expectedGateway))
                {
                    Log("[NET3095-N6][FAIL] HTTP NIC/lease changed at completion attempt=" + attempt);
                    return false;
                }
                Log("[NET3095-N6] HTTP_PCI_STABLE=PASS attempt=" + attempt +
                    " command=0x" + expectedPciCommand.ToString("X4") +
                    " bar0=0x" + expectedPciBar0.ToString("X") +
                    " irq_line=" + expectedIrqLine + " irq_pin=" + expectedIrqPin +
                    " (read-only; ISR unproven)");
                Log("[NET3095-N6] HTTP_DRIVER_STABLE=PASS attempt=" + attempt +
                    " resources=" + expectedResources + " irq_sources=" + expectedIrqSources);
                // Only claim per-stream stability after all RX chunks and
                // exact payload verification. This is not a hardware IRQ proof.
                Log("[NET3095-N6] HTTP_NIC_STABLE=PASS attempt=" + attempt);
                // Guest-side socket-read telemetry supplements the independent
                // PCAP evidence. It measures actual managed Read() completions,
                // not IRQ/ISR delivery or physical NIC DMA ownership.
                Log("[NET3095-N6] HTTP_RX_STATS attempt=" + attempt +
                    " reads=" + readCalls + " max_chunk=" + largestRead +
                    " bytes=" + received);
                // Measure actual managed socket RX latency on the guest. This
                // helps compare polling/IRQ experiments in real QEMU, but is
                // not evidence that an ISR ran or that a physical NIC works.
                // Divide before multiplying to avoid overflowing tick counts.
                long totalTicks = Stopwatch.GetTimestamp() - rxStart;
                if (firstByteTicks < 0 || totalTicks < firstByteTicks ||
                    totalTicks < 0 || totalTicks / rxFrequency >= 45)
                {
                    Log("[NET3095-N6][FAIL] HTTP RX timing invalid attempt=" + attempt);
                    return false;
                }
                long firstByteMs = (firstByteTicks / rxFrequency) * 1000 +
                    ((firstByteTicks % rxFrequency) * 1000) / rxFrequency;
                long totalMs = (totalTicks / rxFrequency) * 1000 +
                    ((totalTicks % rxFrequency) * 1000) / rxFrequency;
                Log("[NET3095-N6] HTTP_RX_TIMING attempt=" + attempt +
                    " first_byte_ms=" + firstByteMs + " total_ms=" + totalMs +
                    " (socket Read; ISR unproven)");
                string marker = attempt == 1 ? "HTTP=PASS" :
                    attempt == 2 ? "HTTP_REPEAT=PASS" :
                    attempt == 3 ? "POST_BURST_HTTP=PASS" :
                    "POST_BURST_HTTP_REPEAT=PASS attempt=" + attempt;
                Log("[NET3095-N6] " + marker +
                    " from=10.0.2.2 port=18096 bytes=" + received);
                return true;
            }
        }
#endif

#if ZONDERQ_NETWORK_N6_HTTP
        private static bool TrySendBurst(byte[] template,
            string expectedMac, string expectedIp, string expectedMask,
            string expectedGateway, int expectedResources, int expectedIrqSources,
            ushort expectedPciCommand, ulong expectedPciBar0,
            byte expectedIrqLine, byte expectedIrqPin)
        {
            // 128 paced frames exercise four complete 32-descriptor TX ring cycles
            // without exposing the NIC in the production GUI.
            // The first 14 bytes are the already validated N1 L2 header.
            byte[] tag = System.Text.Encoding.ASCII.GetBytes("ZONDERQ3095-BURST-");
            // Stock 3.0.95 E1000E.Transmit copies the caller's span into
            // its DMA slot before returning, so this one buffer is reusable.
            // Test minimum, intermediate and full-size Ethernet frames.
            // Stock 3.0.95 copies the requested prefix to the TX DMA slot
            // before Send returns, so the caller buffer can be reused.
            int[] frameLengths = { 60, 128, 512, 1024, 1514 };
            byte[] burst = new byte[1514];
            for (int i = 0; i < 14; i++)
                burst[i] = template[i];
            for (int i = 0; i < tag.Length; i++)
                burst[14 + i] = tag[i];
            // Re-read the published adapter every 32 TX descriptors, including
            // before the first frame. A queued packet is not wire/DMA proof;
            // this only detects a disappearing, unready or rebound NIC while
            // the burst is in progress. The PCAP gate proves actual TX.
            byte[] liveMacFrame = new byte[60];
            var pciConfig = new ZonderqOS.Platform.X64.PciConfigIoAccessor();
            for (int sequence = 0; sequence < 128; sequence++)
            {
                if ((sequence & 31) == 0)
                {
                    // A stable MAC/lease and DriverKit resource count cannot
                    // detect a transient BAR remap or INTx route change.
                    // Read PCI config only, never write or rebind the device.
                    if (!ZonderqOS.Hardware.E1000EPciIdentity.TryReadReady(
                            pciConfig, out ushort command, out ulong bar0) ||
                        command != expectedPciCommand || bar0 != expectedPciBar0 ||
                        !ZonderqOS.Hardware.E1000EPciIdentity.TryReadLegacyIrqRoute(
                            pciConfig, out byte irqLine, out byte irqPin) ||
                        irqLine != expectedIrqLine || irqPin != expectedIrqPin)
                        return false;
                    if (!IsE1000EDriverKitStable(expectedResources, expectedIrqSources))
                        return false;
                    NetworkAdapter current = NetworkManager.Primary;
                    if (!current.IsValid || current.Name != "e1000e" ||
                        !current.Ready || !current.LinkUp ||
                        !TryFillSourceMac(current.MacAddress?.ToString() ?? "", liveMacFrame))
                        return false;
                    for (int macByte = 0; macByte < 6; macByte++)
                    {
                        if (liveMacFrame[6 + macByte] != template[6 + macByte])
                            return false;
                    }
                    // The link can stay up while the DHCP lease disappears.
                    // Detect this at each ring boundary, not just after stress.
                    if (!IsE1000ELeaseStable(expectedMac, expectedIp,
                            expectedMask, expectedGateway))
                        return false;
                    // Only report PASS after the live NIC/lease checks.
                    Log("[NET3095-N6] TX_DRIVER_CHECK=PASS sent=" + sequence +
                        " resources=" + expectedResources +
                        " irq_sources=" + expectedIrqSources);
                    Log("[NET3095-N6] TX_NIC_CHECK=PASS sent=" + sequence);
                }
                // Fixed-width decimal tag survives 099 -> 100 and 127.
                burst[14 + tag.Length] = (byte)('0' + sequence / 100);
                burst[15 + tag.Length] = (byte)('0' + (sequence / 10) % 10);
                burst[16 + tag.Length] = (byte)('0' + sequence % 10);
                int frameLength = frameLengths[sequence % frameLengths.Length];
                // The verifier checks every byte, not just the sequence tag:
                // stale DMA content, truncation and wrong lengths must fail.
                for (int offset = 17 + tag.Length; offset < frameLength; offset++)
                    burst[offset] = (byte)((sequence * 17 + offset) & 0xFF);
                if (!NetworkManager.Send(burst, frameLength))
                    return false;
                Thread.Sleep(5);
            }
            // Include the final descriptor cycle, not just the pre-send
            // snapshots taken at sequence 0, 32, 64 and 96.
            if (!ZonderqOS.Hardware.E1000EPciIdentity.TryReadReady(
                    pciConfig, out ushort finalCommand, out ulong finalBar0) ||
                finalCommand != expectedPciCommand || finalBar0 != expectedPciBar0 ||
                !ZonderqOS.Hardware.E1000EPciIdentity.TryReadLegacyIrqRoute(
                    pciConfig, out byte finalIrqLine, out byte finalIrqPin) ||
                finalIrqLine != expectedIrqLine || finalIrqPin != expectedIrqPin ||
                !IsE1000EDriverKitStable(expectedResources, expectedIrqSources) ||
                !IsE1000ELeaseStable(expectedMac, expectedIp,
                    expectedMask, expectedGateway))
                return false;
            // Recheck the published NIC after the last DMA submission as well:
            // the ring-boundary snapshot at sent=96 cannot catch an adapter
            // disappearing or losing link during the final 32 transmissions.
            NetworkAdapter finalAdapter = NetworkManager.Primary;
            if (!finalAdapter.IsValid || finalAdapter.Name != "e1000e" ||
                !finalAdapter.Ready || !finalAdapter.LinkUp ||
                !TryFillSourceMac(finalAdapter.MacAddress?.ToString() ?? "", liveMacFrame))
                return false;
            for (int macByte = 0; macByte < 6; macByte++)
            {
                if (liveMacFrame[6 + macByte] != template[6 + macByte])
                    return false;
            }
            Log("[NET3095-N6] TX_DRIVER_CHECK=PASS sent=128 resources=" +
                expectedResources + " irq_sources=" + expectedIrqSources);
            Log("[NET3095-N6] TX_NIC_CHECK=PASS sent=128");
            return true;
        }
#endif

        private static int HexDigit(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        private static bool TryFillSourceMac(string mac, byte[] frame)
        {
            if (mac.Length != 17)
                return false;
            for (int i = 0; i < 6; i++)
            {
                int pos = i * 3;
                if (i > 0 && mac[pos - 1] != ':')
                    return false;
                int hi = HexDigit(mac[pos]);
                int lo = HexDigit(mac[pos + 1]);
                if (hi < 0 || lo < 0)
                    return false;
                frame[6 + i] = (byte)((hi << 4) | lo);
            }
            return (frame[6] & 1) == 0 &&
                   (frame[6] | frame[7] | frame[8] |
                    frame[9] | frame[10] | frame[11]) != 0;
        }

        private static int s_heartbeat;

#if ZONDERQ_NETWORK_N6_HTTP
        private static bool s_linkCycleArmed;
        private static int s_linkCycleNumber = 1;
        private static bool s_linkDownObserved;
        private static bool s_linkRecoveryAttempted;
        private static string s_linkCycleMac = "";
        private static string s_linkCycleIp = "";
        private static string s_linkCycleMask = "";
        private static string s_linkCycleGateway = "";
        private static int s_linkCycleResources;
        private static int s_linkCycleIrqSources;
        private static ushort s_linkCyclePciCommand;
        private static ulong s_linkCyclePciBar0;
        private static byte s_linkCycleIrqLine;
        private static byte s_linkCycleIrqPin;

        // Revalidate the original physical MMIO assignment after each QMP
        // link transition. Stable MAC/lease/DriverKit source counts alone
        // cannot detect a PCI BAR remap. This does not prove DMA/ISR execution.
        private static bool IsE1000EPciMappingStable()
        {
            var config = new ZonderqOS.Platform.X64.PciConfigIoAccessor();
            return ZonderqOS.Hardware.E1000EPciIdentity.TryReadReady(
                config, out ushort command, out ulong bar0) &&
                command == s_linkCyclePciCommand && bar0 == s_linkCyclePciBar0 &&
                ZonderqOS.Hardware.E1000EPciIdentity.TryReadLegacyIrqRoute(
                    config, out byte irqLine, out byte irqPin) &&
                irqLine == s_linkCycleIrqLine && irqPin == s_linkCycleIrqPin;
        }

        // This is a diagnostic of the *published* E1000E link state, not
        // direct access to PCI registers or a claim that its ISR executed.
        // The host independently checks QMP replies and the post-up PCAP.
        // New TCP handshake and RX/TX after each QMP link recovery.
        // This is only an isolated N6 diagnostic; production GUI stays offline.
        private static bool TryTcpReconnect(int cycle)
        {
            Log("[NET3095-N6] LINK_TCP_ATTEMPT=START cycle=" + cycle);
            try
            {
                if (cycle != 1 && cycle != 2)
                    return false;
                if (!IsE1000EPciMappingStable() ||
                    !IsE1000EDriverKitStable(s_linkCycleResources, s_linkCycleIrqSources) ||
                    !IsE1000ELeaseStable(s_linkCycleMac, s_linkCycleIp,
                        s_linkCycleMask, s_linkCycleGateway))
                {
                    Log("[NET3095-N6][FAIL] Link TCP preflight PCI/lease changed");
                    return false;
                }
                using (var client = new System.Net.Sockets.TcpClient())
                {
                    client.Connect(System.Net.IPAddress.Parse("10.0.2.2"), 18095);
                    client.Client.SendTimeout = 5000;
                    client.Client.ReceiveTimeout = 5000;
                    System.Net.Sockets.NetworkStream stream = client.GetStream();
                    byte[] request = System.Text.Encoding.ASCII.GetBytes(
                        "ZONDERQ3095-TCP-RECONNECT-" + cycle + "\n");
                    byte[] expected = System.Text.Encoding.ASCII.GetBytes(
                        "ZONDERQ3095-TCP-RESTORED-" + cycle + "\n");
                    stream.Write(request, 0, request.Length);
                    byte[] response = new byte[expected.Length];
                    int received = 0;
                    long firstByteTicks = -1;
                    long start = Stopwatch.GetTimestamp();
                    long frequency = Stopwatch.Frequency;
                    if (frequency <= 0 || start <= 0)
                    {
                        Log("[NET3095-N6][FAIL] Link TCP monotonic clock unavailable");
                        return false;
                    }
                    while (received < response.Length)
                    {
                        long elapsed = Stopwatch.GetTimestamp() - start;
                        if (elapsed < 0 || elapsed / frequency >= 10)
                        {
                            Log("[NET3095-N6][FAIL] Link TCP total RX deadline exceeded");
                            return false;
                        }
                        client.Client.ReceiveTimeout = RemainingSocketReadTimeoutMs(
                            elapsed, frequency, 10);
                        int read = stream.Read(response, received, response.Length - received);
                        if (read <= 0)
                            break;
                        received += read;
                        if (firstByteTicks < 0)
                            firstByteTicks = Stopwatch.GetTimestamp() - start;
                        // A partial TCP response may span multiple DMA/RX
                        // completions. Detect a transient NIC withdrawal,
                        // link loss or lease change at each successful Read,
                        // not only after the final byte is received.
                        if (!IsE1000EPciMappingStable() ||
                            !IsE1000EDriverKitStable(s_linkCycleResources, s_linkCycleIrqSources) ||
                            !IsE1000ELeaseStable(s_linkCycleMac, s_linkCycleIp,
                                s_linkCycleMask, s_linkCycleGateway))
                        {
                            Log("[NET3095-N6][FAIL] Link TCP PCI/lease changed during RX");
                            return false;
                        }
                    }
                    // A final successful Read() can cross the ten-second
                    // budget; checking only before Read() accepted slow peers.
                    long completedTicks = Stopwatch.GetTimestamp() - start;
                    if (completedTicks < 0 || completedTicks / frequency >= 10)
                    {
                        Log("[NET3095-N6][FAIL] Link TCP total RX deadline exceeded");
                        return false;
                    }
                    if (received != expected.Length)
                    {
                        Log("[NET3095-N6][FAIL] Link TCP reply truncated");
                        return false;
                    }
                    for (int i = 0; i < expected.Length; i++)
                    {
                        if (response[i] != expected[i])
                        {
                            Log("[NET3095-N6][FAIL] Link TCP reply bytes differ");
                            return false;
                        }
                    }
                    if (!IsE1000EPciMappingStable() ||
                        !IsE1000EDriverKitStable(s_linkCycleResources, s_linkCycleIrqSources) ||
                        !IsE1000ELeaseStable(s_linkCycleMac, s_linkCycleIp,
                            s_linkCycleMask, s_linkCycleGateway))
                    {
                        Log("[NET3095-N6][FAIL] Link TCP completion PCI/lease changed");
                        return false;
                    }
                    // Guest-side socket latency, not a DMA or ISR counter.
                    // Emit before PASS so the independent serial gate can
                    // reject a missing, duplicated or over-budget response.
                    if (firstByteTicks < 0 || firstByteTicks > completedTicks)
                    {
                        Log("[NET3095-N6][FAIL] Link TCP RX timing invalid");
                        return false;
                    }
                    long firstByteMs = (firstByteTicks / frequency) * 1000 +
                        ((firstByteTicks % frequency) * 1000) / frequency;
                    long totalMs = (completedTicks / frequency) * 1000 +
                        ((completedTicks % frequency) * 1000) / frequency;
                    Log("[NET3095-N6] LINK_TCP_RX_TIMING cycle=" + cycle +
                        " first_byte_ms=" + firstByteMs + " total_ms=" + totalMs);
                    Log("[NET3095-N6] LINK_TCP=PASS cycle=" + cycle +
                        " bytes=" + received);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log("[NET3095-N6][FAIL] Link TCP exception " + ex.GetType().Name);
                return false;
            }
        }

        private static void PollLinkCycle()
        {
            if (!s_linkCycleArmed || s_linkRecoveryAttempted)
                return;
            if (!NetworkManager.IsEnabled || !NetworkManager.IsInitialized ||
                NetworkManager.DeviceCount != 1)
            {
                s_linkRecoveryAttempted = true;
                Log("[NET3095-N6][FAIL] Link-cycle network manager disappeared");
                return;
            }
            NetworkAdapter adapter = NetworkManager.Primary;
            if (!adapter.IsValid || adapter.Name != "e1000e" ||
                (adapter.MacAddress?.ToString() ?? "") != s_linkCycleMac)
            {
                s_linkRecoveryAttempted = true;
                Log("[NET3095-N6][FAIL] Link-cycle PCI binding/MAC disappeared");
                return;
            }
            if (!adapter.LinkUp)
            {
                if (!s_linkDownObserved)
                {
                    s_linkDownObserved = true;
                    Log(s_linkCycleNumber == 1
                        ? "[NET3095-N6] LINK_DOWN_OBSERVED=YES"
                        : "[NET3095-N6] LINK2_DOWN_OBSERVED=YES");
                }
                return;
            }
            if (!s_linkDownObserved)
                return;
            s_linkRecoveryAttempted = true;
            Log(s_linkCycleNumber == 1
                ? "[NET3095-N6] LINK_UP_OBSERVED=YES"
                : "[NET3095-N6] LINK2_UP_OBSERVED=YES");
            if (!IsE1000EPciMappingStable() ||
                !IsE1000EDriverKitStable(s_linkCycleResources, s_linkCycleIrqSources) ||
                !IsE1000ELeaseStable(s_linkCycleMac, s_linkCycleIp,
                    s_linkCycleMask, s_linkCycleGateway))
            {
                Log("[NET3095-N6][FAIL] Link restored without stable PCI/IPv4 state");
                return;
            }
            try
            {
                IcmpClient ping = new IcmpClient();
                try
                {
                    ping.Connect(new Address4(10, 0, 2, 2));
                    if (s_linkCycleNumber == 1)
                    {
                        Log("[NET3095-N6] LINK_RECOVERY_ICMP_ATTEMPT=START");
                        ping.SendEcho(id: 0x3098, sequence: 1);
                    }
                    else
                    {
                        Log("[NET3095-N6] LINK2_RECOVERY_ICMP_ATTEMPT=START");
                        ping.SendEcho(id: 0x3099, sequence: 2);
                    }
                    EndPoint remote = new EndPoint(Address4.Zero, 0);
                    int elapsedMs = ping.Receive(ref remote, 5000);
                    if (elapsedMs < 0 || remote.Address.ToString() != "10.0.2.2" ||
                        !IsE1000EPciMappingStable() ||
                        !IsE1000EDriverKitStable(s_linkCycleResources, s_linkCycleIrqSources) ||
                        !IsE1000ELeaseStable(s_linkCycleMac, s_linkCycleIp,
                            s_linkCycleMask, s_linkCycleGateway))
                    {
                        Log("[NET3095-N6][FAIL] Link recovery ICMP or PCI/lease check failed");
                        return;
                    }
                    if (s_linkCycleNumber == 1)
                    {
                        Log("[NET3095-N6] LINK_RECOVERY_ICMP_RTT seq=1 elapsed_ms=" + elapsedMs);
                        Log("[NET3095-N6] LINK_RECOVERY_ICMP=PASS from=10.0.2.2 seq=1");
                        if (!TryTcpReconnect(1))
                            return;
                        // Re-arm only after a complete first recovery proof.
                        s_linkCycleNumber = 2;
                        s_linkDownObserved = false;
                        s_linkRecoveryAttempted = false;
                        Log("[NET3095-N6] LINK2_ARMED=YES");
                    }
                    else
                    {
                        Log("[NET3095-N6] LINK2_RECOVERY_ICMP_RTT seq=2 elapsed_ms=" + elapsedMs);
                        Log("[NET3095-N6] LINK2_RECOVERY_ICMP=PASS from=10.0.2.2 seq=2");
                        if (!TryTcpReconnect(2))
                            return;
                    }
                }
                finally
                {
                    ping.Close();
                }
            }
            catch (Exception ex)
            {
                Log("[NET3095-N6][FAIL] Link recovery exception " + ex.GetType().Name);
            }
        }
#endif

        protected override void Run()
        {
            // A live QEMU process is not proof of managed guest progress.
            Thread.Sleep(1000);
#if ZONDERQ_NETWORK_N6_HTTP
            PollLinkCycle();
#endif
            s_heartbeat++;
            Log("[NET3095-N1] HEARTBEAT=" + s_heartbeat);
        }
    }
}
#endif
