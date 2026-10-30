#if ZONDERQ_NETWORK_N1_PROBE
using System;
using System.Threading;
using DiagnosticLog = Cosmos.Kernel.System.Diagnostics.Log;
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
            if (count <= 0 || count > 8)
            {
                Log("[NET3095-N1][FAIL] No valid NIC registration");
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

            if (readyLinks == 0)
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

            bool queued = NetworkManager.Send(frame, frame.Length);
            Log("[NET3095-N1] TX_QUEUED=" + (queued ? "yes" : "no"));
            if (!queued)
            {
                Log("[NET3095-N1][FAIL] E1000E TX queue rejected the frame");
                return;
            }
            Log("[NET3095-N1] DHCP=NOT_ATTEMPTED IP=UNASSIGNED");
#if ZONDERQ_NETWORK_N2_DHCP
            // Opt-in second-stage RX proof. DHCP only runs in the isolated
            // serial QEMU probe, never in the production desktop.
            Log("[NET3095-N2] DHCP_ATTEMPT=START");
            try
            {
                int elapsed = new DhcpClient().SendDiscoverPacket();
                IPConfig? config = NetworkManager.Primary.IPConfig;
                if (elapsed < 0 || config == null ||
                    config.Address.IsZero || config.SubnetMask.IsZero ||
                    config.DefaultGateway.IsZero)
                {
                    Log("[NET3095-N2][FAIL] DHCP timed out or returned an invalid lease");
                    return;
                }
                Log("[NET3095-N2] DHCP=PASS ip=" + config.Address +
                    " mask=" + config.SubnetMask +
                    " gateway=" + config.DefaultGateway);
#if ZONDERQ_NETWORK_N3_ICMP
                Log("[NET3095-N3] ICMP_ATTEMPT=START");
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
                        byte[] response = new byte[expected.Length];
                        int received = 0;
                        while (received < response.Length)
                        {
                            int read = stream.Read(response, received, response.Length - received);
                            if (read <= 0)
                                break;
                            received += read;
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
                        Log("[NET3095-N5] DNS=PASS host=localhost ip=127.0.0.1 server=10.0.2.3");
                    }
                }
                catch (Exception dnsException)
                {
                    Log("[NET3095-N5][FAIL] DNS exception " + dnsException.GetType().Name);
                    return;
                }
#endif
            }
            catch (Exception ex)
            {
                Log("[NET3095-N2][FAIL] DHCP exception " + ex.GetType().Name);
            }
#endif
        }

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

        protected override void Run()
        {
            // Prove the managed guest is still scheduling after a successful
            // network exchange. A live QEMU process alone is not enough:
            // -no-shutdown can leave a frozen or halted guest behind.
            Thread.Sleep(1000);
            s_heartbeat++;
            Log("[NET3095-N1] HEARTBEAT=" + s_heartbeat);
        }
    }
}
#endif
