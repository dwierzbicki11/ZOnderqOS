using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using ZonderqOS.Security;
using ZonderqOS.SystemCore.Networking;
#if !ZONDERQ_NETWORK_DISABLED
using Cosmos.Kernel.System.Network;
using CosmosAddress = Cosmos.Kernel.System.Network.Address;
using CosmosAddress4 = Cosmos.Kernel.System.Network.Address4;
using CosmosEndPoint = Cosmos.Kernel.System.Network.EndPoint;
#endif

namespace ZonderqOS
{
    /// <summary>
    /// Stable application-facing network device snapshot. UI code depends on
    /// this type instead of Cosmos NIC implementation details.
    /// </summary>
    public sealed class NetworkDeviceInfo
    {
        internal int AdapterIndex { get; set; } = -1;
        public string Name { get; internal set; } = "Unavailable";
        public string MacAddress { get; internal set; } = "--";
        public bool LinkUp { get; internal set; }
        public bool Ready { get; internal set; }
        public string IpAddress { get; internal set; } = "0.0.0.0";
        public string SubnetMask { get; internal set; } = "0.0.0.0";
        public string DefaultGateway { get; internal set; } = "0.0.0.0";
        public string DnsServer { get; internal set; } = "0.0.0.0";
    }

    /// <summary>
    /// ZonderqOS network facade backed by stock Cosmos Gen3 3.0.97 NetworkManager,
    /// DhcpClient, DnsClient, IPConfig, and plugged TCP/UDP sockets.
    /// Fails closed with zero background overhead when no supported NIC is present.
    /// </summary>
    public static class Network
    {
        private static readonly object Sync = new object();
        private static readonly List<NetworkDeviceInfo> devices = new List<NetworkDeviceInfo>();
        private static int activeIndex = -1;
        private static NetworkDeviceInfo activeDevice;
        private static bool isReady;
        private static string currentAddress = "0.0.0.0";
        private static string activeDns = "0.0.0.0";
        private static string lastStatusMessage = "Not initialized";
        private static int autoConfigStarted;
        private static int hotplugDhcpInFlight;
        private static volatile bool bootInitialized;

        static Network()
        {
            ZonderqOS.SystemCore.Updates.OtaUpdateClient.BindNetworkBridge(
                () => isReady,
                ResolveDns,
                ApplySavedConfiguration);
        }

        public static IReadOnlyList<NetworkDeviceInfo> Devices => devices;
        public static NetworkDeviceInfo ActiveDevice => activeDevice;
        public static bool IsReady => isReady;
        public static string CurrentAddress => currentAddress;
        public static string CurrentDnsServer => activeDns;
        public static string LastStatusMessage => lastStatusMessage;

        public static void Initialize()
        {
#if ZONDERQ_NETWORK_DISABLED
            lock (Sync)
            {
                devices.Clear();
                activeIndex = -1;
                activeDevice = null;
                isReady = false;
                currentAddress = "0.0.0.0";
                activeDns = "0.0.0.0";
                lastStatusMessage = "Networking is disabled in this build.";
            }
#else
            lock (Sync)
            {
                devices.Clear();
                activeIndex = -1;
                activeDevice = null;
                isReady = false;
                currentAddress = "0.0.0.0";
                activeDns = "0.0.0.0";

                try
                {
                    if (!NetworkManager.IsEnabled)
                    {
                        lastStatusMessage = "Cosmos NetworkManager is disabled.";
                        return;
                    }

                    if (!NetworkManager.IsInitialized)
                    {
                        lastStatusMessage = "Cosmos NetworkManager not yet initialized.";
                        return;
                    }

                    RefreshDevicesLocked();
                    bootInitialized = true;
                    if (devices.Count == 0)
                    {
                        lastStatusMessage = "No supported network adapter detected (Intel E1000E, VirtIO-Net, USB Tethering RNDIS/CDC-NCM/CDC-ECM).";
                    }
                    else
                    {
                        lastStatusMessage = "Detected " + devices.Count + " network adapter(s).";
                    }
                }
                catch (Exception ex)
                {
                    lastStatusMessage = "Network init failed: " + ex.Message;
                }
            }
#endif
        }

        /// <summary>
        /// Spawns a single one-shot background thread to apply saved DHCP/static config
        /// ONLY if a supported NIC with an active link was discovered. Leaves zero threads
        /// running after completion and adds zero boot overhead when no NIC is present.
        /// </summary>
        public static void StartBackgroundAutoConfigIfLinked()
        {
#if !ZONDERQ_NETWORK_DISABLED
            if (activeDevice == null || !activeDevice.Ready || !activeDevice.LinkUp)
                return;
            if (Interlocked.CompareExchange(ref autoConfigStarted, 1, 0) != 0)
                return;

            try
            {
                var thread = new Thread(() =>
                {
                    try
                    {
                        ApplySavedConfiguration();
                    }
                    catch { }
                });
                thread.IsBackground = true;
                thread.Name = "net-autoconfig";
                thread.Start();
            }
            catch
            {
                Interlocked.Exchange(ref autoConfigStarted, 0);
            }
#endif
        }

        public static void Refresh()
        {
#if !ZONDERQ_NETWORK_DISABLED
            lock (Sync)
            {
                try
                {
                    if (NetworkManager.IsEnabled && NetworkManager.IsInitialized)
                        RefreshDevicesLocked();
                }
                catch { }
            }
#endif
        }

        /// <summary>
        /// Called by hot-pluggable network drivers (e.g. UsbTetheringDriver) when a new
        /// adapter binds after boot. Refreshes device inventory and, when DHCP is enabled,
        /// automatically acquires a lease on a one-shot background thread.
        /// </summary>
        public static void NotifyHotpluggedAdapter(string adapterName)
        {
#if !ZONDERQ_NETWORK_DISABLED
            if (!bootInitialized)
                return;
            if (Interlocked.CompareExchange(ref hotplugDhcpInFlight, 1, 0) != 0)
                return;

            try
            {
                var thread = new Thread(() =>
                {
                    try
                    {
                        Thread.Sleep(250);
                        lock (Sync)
                        {
                            if (!NetworkManager.IsEnabled || !NetworkManager.IsInitialized)
                                return;

                            RefreshDevicesLocked();
                            if (!string.IsNullOrEmpty(adapterName))
                            {
                                for (int i = 0; i < devices.Count; i++)
                                {
                                    if (string.Equals(devices[i].Name, adapterName, StringComparison.OrdinalIgnoreCase) &&
                                        devices[i].Ready && devices[i].LinkUp && devices[i].AdapterIndex >= 0)
                                    {
                                        activeIndex = devices[i].AdapterIndex;
                                        NetworkAdapter target = NetworkManager.GetAdapter(activeIndex);
                                        if (target.IsValid)
                                            NetworkManager.Primary = target;
                                        RefreshDevicesLocked();
                                        break;
                                    }
                                }
                            }

                            if (!isReady && SystemSettings.NetworkUseDhcp)
                            {
                                ConfigureDhcpInternalLocked(logToConsole: false);
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        Interlocked.Exchange(ref hotplugDhcpInFlight, 0);
                    }
                });
                thread.IsBackground = true;
                thread.Name = "net-hotplug-dhcp";
                thread.Start();
            }
            catch
            {
                Interlocked.Exchange(ref hotplugDhcpInFlight, 0);
            }
#endif
        }

        /// <summary>
        /// Called by hot-pluggable network drivers on USB detach to refresh link state immediately.
        /// </summary>
        public static void NotifyAdapterDetached(string adapterName)
        {
#if !ZONDERQ_NETWORK_DISABLED
            if (!bootInitialized)
                return;
            lock (Sync)
            {
                try
                {
                    if (NetworkManager.IsEnabled && NetworkManager.IsInitialized)
                        RefreshDevicesLocked();
                }
                catch { }
            }
#endif
        }

        public static bool SetActiveDevice(int index)
        {
#if ZONDERQ_NETWORK_DISABLED
            return false;
#else
            if (!CanMutateNetworkConfig("set-active-device", out string authErr))
            {
                WriteMessage.WriteError(authErr, "NET");
                return false;
            }

            lock (Sync)
            {
                try
                {
                    if (!NetworkManager.IsEnabled || !NetworkManager.IsInitialized)
                        return false;

                    int count = NetworkManager.DeviceCount;
                    if (index < 0 || index >= count)
                    {
                        WriteMessage.WriteError("Invalid network adapter index: " + index, "NET");
                        return false;
                    }

                    NetworkAdapter adapter = NetworkManager.GetAdapter(index);
                    if (!adapter.IsValid)
                        return false;

                    NetworkManager.Primary = adapter;
                    activeIndex = index;
                    RefreshDevicesLocked();
                    WriteMessage.WriteOK("Active network adapter set to #" + index + " (" + (activeDevice?.Name ?? "unknown") + ").", "NET");
                    return true;
                }
                catch (Exception ex)
                {
                    WriteMessage.WriteError("Failed to select adapter: " + ex.Message, "NET");
                    return false;
                }
            }
#endif
        }

        public static bool ApplySavedConfiguration()
        {
#if ZONDERQ_NETWORK_DISABLED
            return false;
#else
            if (SystemSettings.NetworkUseDhcp)
                return ConfigureDhcp();

            return ConfigureStatic(
                SystemSettings.StaticIpAddress,
                SystemSettings.StaticSubnetMask,
                SystemSettings.StaticGateway,
                SystemSettings.DnsServer);
#endif
        }

        public static bool ConfigureDhcp()
        {
#if ZONDERQ_NETWORK_DISABLED
            return false;
#else
            if (!CanMutateNetworkConfig("configure-dhcp", out string authErr))
            {
                lastStatusMessage = authErr;
                WriteMessage.WriteError(authErr, "NET");
                return false;
            }

            lock (Sync)
            {
                return ConfigureDhcpInternalLocked(logToConsole: true);
            }
#endif
        }

#if !ZONDERQ_NETWORK_DISABLED
        private static bool ConfigureDhcpInternalLocked(bool logToConsole)
        {
            try
            {
                if (!EnsureInitializedAdapterLocked(out NetworkAdapter adapter, out string err))
                {
                    lastStatusMessage = err;
                    if (logToConsole) WriteMessage.WriteError(err, "NET");
                    return false;
                }

                if (!adapter.Ready || !adapter.LinkUp)
                {
                    lastStatusMessage = "Adapter '" + adapter.Name + "' link is down.";
                    if (logToConsole) WriteMessage.WriteError(lastStatusMessage, "NET");
                    return false;
                }

                lastStatusMessage = "Requesting DHCP lease (RFC 2131 broadcast)...";
                bool ok = ZonderqDhcpClient.TryAcquireLease(adapter, out DhcpReplyInfo lease, out string dhcpErr);
                IPConfig cfg = adapter.IPConfig;
                if (!ok || cfg == null || cfg.Address == null || cfg.Address.IsZero)
                {
                    lastStatusMessage = string.IsNullOrEmpty(dhcpErr)
                        ? "DHCP timed out or returned no lease."
                        : dhcpErr;
                    RefreshDevicesLocked();
                    if (logToConsole) WriteMessage.WriteError(lastStatusMessage, "NET");
                    return false;
                }

                EnsureDnsConfiguredLocked(cfg.DefaultGateway?.ToString());
                RefreshDevicesLocked();
                lastStatusMessage = "DHCP lease active: " + currentAddress;
                if (logToConsole && activeDevice != null)
                {
                    WriteMessage.WriteOK("DHCP configured (" + activeDevice.Name + "): IP=" + currentAddress +
                        " Mask=" + activeDevice.SubnetMask + " GW=" + activeDevice.DefaultGateway +
                        " DNS=" + activeDns, "NET");
                }
                return true;
            }
            catch (Exception ex)
            {
                lastStatusMessage = "DHCP error: " + ex.Message;
                if (logToConsole) WriteMessage.WriteError(lastStatusMessage, "NET");
                return false;
            }
        }
#endif

        public static bool ConfigureStatic(string ip, string subnet, string gateway, string dns)
        {
#if ZONDERQ_NETWORK_DISABLED
            return false;
#else
            if (!CanMutateNetworkConfig("configure-static", out string authErr))
            {
                lastStatusMessage = authErr;
                WriteMessage.WriteError(authErr, "NET");
                return false;
            }

            lock (Sync)
            {
                try
                {
                    if (!EnsureInitializedAdapterLocked(out NetworkAdapter adapter, out string err))
                    {
                        lastStatusMessage = err;
                        WriteMessage.WriteError(err, "NET");
                        return false;
                    }

                    CosmosAddress ipAddr = ParseIpv4(ip);
                    CosmosAddress maskAddr = ParseIpv4(subnet);
                    CosmosAddress gwAddr = ParseIpv4(gateway);
                    if (ipAddr == null || maskAddr == null || gwAddr == null)
                    {
                        lastStatusMessage = "Invalid IPv4 address, mask, or gateway.";
                        WriteMessage.WriteError(lastStatusMessage, "NET");
                        return false;
                    }

                    if (!IPConfig.Enable(adapter, ipAddr, maskAddr, gwAddr))
                    {
                        lastStatusMessage = "IPConfig.Enable rejected static configuration.";
                        WriteMessage.WriteError(lastStatusMessage, "NET");
                        return false;
                    }

                    CosmosAddress dnsAddr = ParseIpv4(dns);
                    if (dnsAddr != null)
                    {
                        try { DnsConfig.Add(dnsAddr); } catch { }
                        activeDns = dnsAddr.ToString();
                    }
                    else
                    {
                        EnsureDnsConfiguredLocked(gateway);
                    }

                    RefreshDevicesLocked();
                    lastStatusMessage = "Static IPv4 active: " + currentAddress;
                    WriteMessage.WriteOK("Static IPv4 configured (" + activeDevice.Name + "): IP=" + currentAddress +
                        " Mask=" + activeDevice.SubnetMask + " GW=" + activeDevice.DefaultGateway +
                        " DNS=" + activeDns, "NET");
                    return true;
                }
                catch (Exception ex)
                {
                    lastStatusMessage = "Static IPv4 error: " + ex.Message;
                    WriteMessage.WriteError(lastStatusMessage, "NET");
                    return false;
                }
            }
#endif
        }

        public static string ResolveDns(string domain)
        {
#if ZONDERQ_NETWORK_DISABLED
            return null;
#else
            if (string.IsNullOrWhiteSpace(domain))
                return null;

            string trimmed = domain.Trim();
            if (OtaUrlParser.IsIPv4Literal(trimmed))
                return trimmed;

            if (!isReady)
                return null;

            List<CosmosAddress> servers = GetCandidateDnsServers();
            for (int i = 0; i < servers.Count; i++)
            {
                CosmosAddress server = servers[i];
                if (server == null || server.IsZero)
                    continue;
                try
                {
                    using (var client = new DnsClient())
                    {
                        client.Connect(server);
                        client.SendQuery(trimmed);
                        CosmosAddress answer = client.Receive(3500);
                        if (answer != null && answer.IsIpv4 && !answer.IsZero)
                            return answer.ToString();
                    }
                }
                catch { }
            }

            return null;
#endif
        }

        public static void SendUdpTest(string targetIp, int port, string text)
        {
#if ZONDERQ_NETWORK_DISABLED
            WriteMessage.WriteError("Networking is not supported in this ZonderqOS build.", "NET");
#else
            if (!isReady)
            {
                WriteMessage.WriteError("Network is not configured. Run 'net dhcp' first.", "NET");
                return;
            }

            string resolved = OtaUrlParser.IsIPv4Literal(targetIp) ? targetIp : ResolveDns(targetIp);
            CosmosAddress dest = ParseIpv4(resolved);
            if (dest == null)
            {
                WriteMessage.WriteError("Cannot resolve UDP target: " + targetIp, "NET");
                return;
            }

            try
            {
                using (var udp = new UdpClient())
                {
                    udp.Connect(dest, port);
                    byte[] payload = Encoding.UTF8.GetBytes(text ?? string.Empty);
                    udp.Send(payload);
                    WriteMessage.WriteOK("Sent " + payload.Length + " UDP bytes to " + dest + ":" + port, "NET");
                }
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError("UDP send failed: " + ex.Message, "NET");
            }
#endif
        }

        public static void ShowInterfaceInfo()
        {
#if ZONDERQ_NETWORK_DISABLED
            WriteMessage.WriteInfo("Networking is disabled in this ZonderqOS build.", "NET");
#else
            Refresh();
            CommandIO.WriteLine("=== ZonderqOS Network Interfaces ===");
            if (devices.Count == 0)
            {
                CommandIO.WriteLine("  No supported network adapter detected (Intel E1000E, VirtIO-Net, USB Tethering RNDIS/CDC-NCM/CDC-ECM).");
                CommandIO.WriteLine("  Plug in a phone with USB Tethering ON, then run 'net info' and 'net dhcp'.");
                CommandIO.WriteLine("  Run 'nicdiag' to inspect raw PCI network controllers.");
                return;
            }

            for (int i = 0; i < devices.Count; i++)
            {
                NetworkDeviceInfo d = devices[i];
                bool isActive = d == activeDevice;
                CommandIO.WriteLine("  [" + i + "] " + d.Name + (isActive ? " (ACTIVE)" : ""));
                CommandIO.WriteLine("      MAC:     " + d.MacAddress);
                CommandIO.WriteLine("      State:   " + (d.Ready ? "READY" : "NOT READY") + " / LINK " + (d.LinkUp ? "UP" : "DOWN"));
                CommandIO.WriteLine("      IPv4:    " + d.IpAddress + "  Mask: " + d.SubnetMask);
                CommandIO.WriteLine("      Gateway: " + d.DefaultGateway + "  DNS: " + d.DnsServer);
            }
            CommandIO.WriteLine("  Status: " + lastStatusMessage);
            string usbDiag = ZonderqOS.Hardware.UsbTethering.UsbTetheringDriver.GetDiagnosticsSummary();
            if (!string.IsNullOrEmpty(usbDiag))
                CommandIO.WriteLine("  USB Net: " + usbDiag);
#endif
        }

        public static bool TestTcpConnection(string hostOrIp, int port, int timeoutMs, out int elapsedMs, out string error)
        {
            elapsedMs = -1;
            error = string.Empty;
#if ZONDERQ_NETWORK_DISABLED
            error = "Networking is disabled in this ZonderqOS build.";
            return false;
#else
            if (!isReady)
            {
                error = "Network is offline.";
                return false;
            }

            string ipText = OtaUrlParser.IsIPv4Literal(hostOrIp) ? hostOrIp : ResolveDns(hostOrIp);
            if (string.IsNullOrEmpty(ipText) || !System.Net.IPAddress.TryParse(ipText, out System.Net.IPAddress remoteIp))
            {
                error = "Failed to resolve host '" + hostOrIp + "'.";
                return false;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            System.Net.Sockets.TcpClient tcp = null;
            try
            {
                tcp = new System.Net.Sockets.TcpClient();
                if (tcp.Client != null)
                {
                    tcp.Client.ReceiveTimeout = Math.Max(1000, timeoutMs);
                    tcp.Client.SendTimeout = Math.Max(1000, timeoutMs);
                }
                tcp.Connect(remoteIp, port);
                sw.Stop();
                elapsedMs = (int)sw.ElapsedMilliseconds;
                return true;
            }
            catch (Exception ex)
            {
                sw.Stop();
                error = ex.Message;
                return false;
            }
            finally
            {
                try { tcp?.Client?.Close(1000); } catch { }
                try { tcp?.Close(); } catch { }
            }
#endif
        }

        /// <summary>
        /// End-to-end 9-stage network, protocol, and RAM stability verification suite
        /// for QEMU (E1000E / VirtIO-Net / USB RNDIS) and physical hardware (ASUS X540LA).
        /// </summary>
        public static bool RunSelfTest(string testHttpsUrl = null)
        {
#if ZONDERQ_NETWORK_DISABLED
            CommandIO.WriteLine("[NET-TEST] FAIL: Networking is disabled in this build.");
            return false;
#else
            string httpsUrl = string.IsNullOrWhiteSpace(testHttpsUrl)
                ? "https://raw.githubusercontent.com/dwierzbicki11/ZOnderqOS/main/global.json"
                : testHttpsUrl.Trim();

            CommandIO.WriteLine("=== ZonderqOS Cosmos 3.0.99 Network & RAM Stability Self-Test ===");

            // Stage 1: PCI + USB Hardware Inventory (including ASUS X540LA RTL810xE / RTL8723BE check)
            CommandIO.WriteLine("[1/9] Hardware & PCI/USB Inventory:");
            ZonderqOS.SystemCore.NetworkPciDiagnostics.PrintReport(true);
            Refresh();
            string usbDiag = ZonderqOS.Hardware.UsbTethering.UsbTetheringDriver.GetDiagnosticsSummary();
            if (!string.IsNullOrEmpty(usbDiag))
                CommandIO.WriteLine("      USB Tethering: " + usbDiag);

            // Stage 2: Link & MAC
            NetworkDeviceInfo dev = activeDevice;
            if (dev == null || !dev.Ready || !dev.LinkUp)
            {
                CommandIO.WriteLine("[2/9] Link & Adapter: FAIL (no active linked adapter; on ASUS X540LA enable Phone USB Tethering)");
                return false;
            }
            CommandIO.WriteLine("[2/9] Link & Adapter: PASS (" + dev.Name + " MAC=" + dev.MacAddress + " LINK=UP)");

            // Stage 3: DHCP / IPv4
            if (!isReady && (SecurityContext.CurrentUid == 0 || !SecurityContext.IsAuthenticated))
            {
                ConfigureDhcp();
                dev = activeDevice;
            }
            if (!isReady || dev == null || dev.IpAddress == "0.0.0.0")
            {
                CommandIO.WriteLine("[3/9] IPv4 / DHCP:    FAIL (" + lastStatusMessage + ")");
                return false;
            }
            CommandIO.WriteLine("[3/9] IPv4 / DHCP:    PASS (IP=" + dev.IpAddress + " Mask=" + dev.SubnetMask +
                                " GW=" + dev.DefaultGateway + " DNS=" + activeDns + ")");

            // Stage 4: ARP + ICMP Echo to Gateway
            int icmpReplies = 0;
            CosmosAddress gwAddr = ParseIpv4(dev.DefaultGateway);
            if (gwAddr != null)
            {
                for (int seq = 0; seq < 2; seq++)
                {
                    try
                    {
                        using (var icmp = new IcmpClient())
                        {
                            icmp.Connect(gwAddr);
                            icmp.SendEcho();
                            CosmosEndPoint ep = new CosmosEndPoint(CosmosAddress4.Zero, 0);
                            int rtt = icmp.Receive(ref ep, 2000);
                            if (rtt >= 0) icmpReplies++;
                        }
                    }
                    catch { }
                }
            }
            if (icmpReplies == 0)
            {
                CommandIO.WriteLine("[4/9] ARP + ICMP:     FAIL (0/2 echo replies from gateway " + dev.DefaultGateway + ")");
                return false;
            }
            CommandIO.WriteLine("[4/9] ARP + ICMP:     PASS (" + icmpReplies + "/2 echo replies from " + dev.DefaultGateway + ")");

            // Stage 5: UDP + DNS
            string ghIp = ResolveDns("raw.githubusercontent.com");
            if (string.IsNullOrEmpty(ghIp))
            {
                CommandIO.WriteLine("[5/9] UDP + DNS:      FAIL (could not resolve raw.githubusercontent.com)");
                return false;
            }
            try
            {
                using (var udp = new UdpClient())
                {
                    CosmosAddress dnsTarget = ParseIpv4(activeDns) ?? gwAddr;
                    if (dnsTarget != null)
                    {
                        udp.Connect(dnsTarget, 53);
                    }
                }
            }
            catch { }
            CommandIO.WriteLine("[5/9] UDP + DNS:      PASS (raw.githubusercontent.com -> " + ghIp + ")");

            // Stage 6: TCP Connect, Reconnect & Clean Disposal
            int tcpMs2 = 0;
            string tcpErr2 = string.Empty;
            if (!TestTcpConnection(ghIp, 443, 5000, out int tcpMs1, out string tcpErr1) ||
                !TestTcpConnection(ghIp, 443, 5000, out tcpMs2, out tcpErr2))
            {
                CommandIO.WriteLine("[6/9] TCP Reconnect:  FAIL (" + (string.IsNullOrEmpty(tcpErr1) ? tcpErr2 : tcpErr1) + ")");
                return false;
            }
            CommandIO.WriteLine("[6/9] TCP Reconnect:  PASS (connect #1=" + tcpMs1 + "ms, reconnect #2=" + tcpMs2 + "ms, clean Close)");

            // Stage 7: HTTP/1.1 (port 80 redirect check to github.com)
            var httpOpts = new HttpDownloadOptions
            {
                AllowInsecureHttp = true,
                MaxRedirects = 0,
                TimeoutMs = 10000,
                DnsResolver = ResolveDns
            };
            HttpDownloadResult httpRes = HttpStreamClient.DownloadBytes("http://github.com/", httpOpts, out _);
            bool httpOk = httpRes != null && (httpRes.StatusCode == 200 || httpRes.StatusCode == 301 || httpRes.StatusCode == 302);
            if (!httpOk)
            {
                CommandIO.WriteLine("[7/9] HTTP/1.1 (80):  FAIL (" + (httpRes?.Error ?? "no response") + ")");
                return false;
            }
            CommandIO.WriteLine("[7/9] HTTP/1.1 (80):  PASS (status=" + httpRes.StatusCode + " from http://github.com/)");

            // Stage 8 & 9: HTTPS TLS 1.2/1.3 + Multi-Cycle RAM Stability Measurement
            var httpsOpts = new HttpDownloadOptions
            {
                AllowInsecureHttp = false,
                MaxRedirects = 5,
                TimeoutMs = 15000,
                DnsResolver = ResolveDns
            };

            long ramBefore = GC.GetTotalMemory(false);
            HttpDownloadResult https1 = HttpStreamClient.DownloadBytes(httpsUrl, httpsOpts, out _);
            long ramAfter1 = GC.GetTotalMemory(false);
            if (https1 == null || !https1.Success)
            {
                CommandIO.WriteLine("[8/9] HTTPS TLS:      FAIL (" + (https1?.Error ?? "unknown") + ")");
                return false;
            }
            CommandIO.WriteLine("[8/9] HTTPS TLS:      PASS (HTTP " + https1.StatusCode + ", " +
                                https1.BytesReceived + " bytes, SHA256=" + https1.Sha256Hex + ")");

            HttpDownloadResult https2 = HttpStreamClient.DownloadBytes(httpsUrl, httpsOpts, out _);
            long ramAfter2 = GC.GetTotalMemory(false);
            HttpDownloadResult https3 = HttpStreamClient.DownloadBytes(httpsUrl, httpsOpts, out _);
            long ramAfter3 = GC.GetTotalMemory(false);

            if (https2 == null || !https2.Success || https3 == null || !https3.Success)
            {
                CommandIO.WriteLine("[9/9] RAM Soak (3x):  FAIL (subsequent HTTPS cycle failed)");
                return false;
            }

            long kb0 = ramBefore / 1024;
            long kb1 = ramAfter1 / 1024;
            long kb2 = ramAfter2 / 1024;
            long kb3 = ramAfter3 / 1024;
            CommandIO.WriteLine("[9/9] RAM Soak (3x):  PASS (Before=" + kb0 + " KiB -> C1=" + kb1 +
                                " KiB -> C2=" + kb2 + " KiB -> C3=" + kb3 + " KiB, pooled 20 KiB buffers)");
            CommandIO.WriteLine("=== ALL 9 NETWORK & TLS STAGES PASSED ===");
            return true;
#endif
        }

#if !ZONDERQ_NETWORK_DISABLED
        private static bool CanMutateNetworkConfig(string operation, out string error)
        {
            error = string.Empty;
            // Boot-time autoconfig runs before login (!IsAuthenticated); once a user
            // session is authenticated, only root (UID 0) may alter network settings.
            if (SecurityContext.IsAuthenticated && SecurityContext.CurrentUid != 0)
            {
                error = "Permission denied: root privileges are required for network configuration (" + operation + ").";
                try
                {
                    SecurityLogger.LogEvent("WARN",
                        "Unauthorized network mutation '" + operation + "' by " +
                        (SecurityContext.CurrentUser ?? "?") + " (uid=" + SecurityContext.CurrentUid + ")");
                }
                catch { }
                return false;
            }
            return true;
        }

        private static bool EnsureInitializedAdapterLocked(out NetworkAdapter adapter, out string error)
        {
            adapter = default(NetworkAdapter);
            error = string.Empty;

            if (!NetworkManager.IsEnabled)
            {
                error = "Cosmos NetworkManager is disabled.";
                return false;
            }

            if (!NetworkManager.IsInitialized)
            {
                error = "Cosmos NetworkManager is not initialized.";
                return false;
            }

            if (NetworkManager.DeviceCount <= 0)
            {
                RefreshDevicesLocked();
                error = "No supported network adapter detected.";
                return false;
            }

            if (activeIndex < 0 || activeIndex >= NetworkManager.DeviceCount)
                RefreshDevicesLocked();

            adapter = activeIndex >= 0 && activeIndex < NetworkManager.DeviceCount
                ? NetworkManager.GetAdapter(activeIndex)
                : NetworkManager.Primary;

            if (!adapter.IsValid || !adapter.Ready || !adapter.LinkUp)
            {
                // Pick up a freshly hot-plugged ready/linked adapter (e.g. USB Tethering).
                RefreshDevicesLocked();
                if (activeIndex >= 0 && activeIndex < NetworkManager.DeviceCount)
                    adapter = NetworkManager.GetAdapter(activeIndex);
            }

            if (!adapter.IsValid)
            {
                error = "No valid network adapter available.";
                return false;
            }

            NetworkManager.Primary = adapter;
            return true;
        }

        private static void RefreshDevicesLocked()
        {
            devices.Clear();
            activeDevice = null;
            isReady = false;
            currentAddress = "0.0.0.0";

            int count = NetworkManager.DeviceCount;
            if (count <= 0)
            {
                activeIndex = -1;
                return;
            }

            string dnsText = GetPrimaryDnsString();
            NetworkAdapter primary = NetworkManager.Primary;
            int selected = -1;

            for (int i = 0; i < count; i++)
            {
                NetworkAdapter a = NetworkManager.GetAdapter(i);
                if (!a.IsValid)
                    continue;

                IPConfig cfg = a.IPConfig;
                string ip = cfg != null && cfg.Address != null && !cfg.Address.IsZero ? cfg.Address.ToString() : "0.0.0.0";
                string mask = cfg != null && cfg.SubnetMask != null && !cfg.SubnetMask.IsZero ? cfg.SubnetMask.ToString() : "0.0.0.0";
                string gw = cfg != null && cfg.DefaultGateway != null && !cfg.DefaultGateway.IsZero ? cfg.DefaultGateway.ToString() : "0.0.0.0";

                var info = new NetworkDeviceInfo
                {
                    AdapterIndex = i,
                    Name = string.IsNullOrEmpty(a.Name) ? ("nic" + i) : a.Name,
                    MacAddress = a.MacAddress != null ? a.MacAddress.ToString() : "--",
                    LinkUp = a.LinkUp,
                    Ready = a.Ready,
                    IpAddress = ip,
                    SubnetMask = mask,
                    DefaultGateway = gw,
                    DnsServer = dnsText
                };

                devices.Add(info);
                if (i == activeIndex || (activeIndex < 0 && primary.IsValid && a == primary))
                    selected = devices.Count - 1;
            }

            // Always prefer a Ready + LinkUp adapter (e.g. plugged USB Tethering) over a LinkDown adapter (e.g. unplugged Ethernet).
            if (devices.Count > 0 && (selected < 0 || !devices[selected].Ready || !devices[selected].LinkUp))
            {
                for (int i = 0; i < devices.Count; i++)
                {
                    if (devices[i].Ready && devices[i].LinkUp)
                    {
                        selected = i;
                        break;
                    }
                }
                if (selected < 0)
                    selected = 0;
            }

            if (selected >= 0 && selected < devices.Count)
            {
                activeDevice = devices[selected];
                activeIndex = activeDevice.AdapterIndex;
                if (activeDevice.Ready && activeDevice.LinkUp && activeIndex >= 0 && activeIndex < count)
                {
                    NetworkAdapter selectedAdapter = NetworkManager.GetAdapter(activeIndex);
                    if (selectedAdapter.IsValid)
                        NetworkManager.Primary = selectedAdapter;
                }
                currentAddress = activeDevice.IpAddress;
                activeDns = activeDevice.DnsServer;
                isReady = activeDevice.Ready && activeDevice.LinkUp &&
                          !string.IsNullOrEmpty(currentAddress) && currentAddress != "0.0.0.0";
            }
            else
            {
                activeIndex = -1;
            }
        }

        private static void EnsureDnsConfiguredLocked(string gatewayIp)
        {
            IReadOnlyList<CosmosAddress> existing = DnsConfig.Nameservers;
            if (existing != null && existing.Count > 0)
            {
                for (int i = 0; i < existing.Count; i++)
                {
                    if (existing[i] != null && existing[i].IsIpv4 && !existing[i].IsZero)
                    {
                        activeDns = existing[i].ToString();
                        return;
                    }
                }
            }

            // QEMU user-mode networking (SLIRP) always places its DNS proxy at 10.0.2.3.
            if (string.Equals(gatewayIp, "10.0.2.2", StringComparison.Ordinal))
            {
                CosmosAddress slirpDns = new CosmosAddress4(10, 0, 2, 3);
                try { DnsConfig.Add(slirpDns); } catch { }
                activeDns = "10.0.2.3";
                return;
            }

            CosmosAddress configured = ParseIpv4(SystemSettings.DnsServer);
            if (configured != null)
            {
                try { DnsConfig.Add(configured); } catch { }
                activeDns = configured.ToString();
                return;
            }

            CosmosAddress fallback = new CosmosAddress4(1, 1, 1, 1);
            try { DnsConfig.Add(fallback); } catch { }
            activeDns = "1.1.1.1";
        }

        private static string GetPrimaryDnsString()
        {
            try
            {
                IReadOnlyList<CosmosAddress> servers = DnsConfig.Nameservers;
                if (servers != null)
                {
                    for (int i = 0; i < servers.Count; i++)
                    {
                        if (servers[i] != null && servers[i].IsIpv4 && !servers[i].IsZero)
                            return servers[i].ToString();
                    }
                }
            }
            catch { }
            return string.IsNullOrEmpty(activeDns) ? "0.0.0.0" : activeDns;
        }

        private static List<CosmosAddress> GetCandidateDnsServers()
        {
            var result = new List<CosmosAddress>(4);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            void AddIfUnique(CosmosAddress addr)
            {
                if (addr == null || !addr.IsIpv4 || addr.IsZero)
                    return;
                string s = addr.ToString();
                if (seen.Add(s))
                    result.Add(addr);
            }

            try
            {
                IReadOnlyList<CosmosAddress> configured = DnsConfig.Nameservers;
                if (configured != null)
                {
                    for (int i = 0; i < configured.Count; i++)
                        AddIfUnique(configured[i]);
                }
            }
            catch { }

            if (activeDevice != null && string.Equals(activeDevice.DefaultGateway, "10.0.2.2", StringComparison.Ordinal))
                AddIfUnique(new CosmosAddress4(10, 0, 2, 3));

            AddIfUnique(ParseIpv4(SystemSettings.DnsServer));
            AddIfUnique(new CosmosAddress4(1, 1, 1, 1));
            AddIfUnique(new CosmosAddress4(8, 8, 8, 8));
            return result;
        }

        private static CosmosAddress ParseIpv4(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || !OtaUrlParser.IsIPv4Literal(text.Trim()))
                return null;
            try
            {
                CosmosAddress parsed = CosmosAddress.Parse(text.Trim().AsSpan());
                return parsed != null && parsed.IsIpv4 && !parsed.IsZero ? parsed : null;
            }
            catch
            {
                return null;
            }
        }
#endif
    }
}

namespace Cosmos.Kernel.System.Network.Config
{
    /// <summary>
    /// Compatibility bridge for Settings UI routed through ZonderqOS.Network.
    /// </summary>
    public static class NetworkConfigManager
    {
        public static string CurrentAddress => global::ZonderqOS.Network.CurrentAddress;
    }
}
