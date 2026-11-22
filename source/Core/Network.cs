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
                    if (devices.Count == 0)
                    {
                        lastStatusMessage = "No supported network adapter detected (Intel E1000E / VirtIO-Net).";
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
                try
                {
                    if (!EnsureInitializedAdapterLocked(out NetworkAdapter adapter, out string err))
                    {
                        lastStatusMessage = err;
                        WriteMessage.WriteError(err, "NET");
                        return false;
                    }

                    if (!adapter.Ready || !adapter.LinkUp)
                    {
                        lastStatusMessage = "Adapter '" + adapter.Name + "' link is down.";
                        WriteMessage.WriteError(lastStatusMessage, "NET");
                        return false;
                    }

                    lastStatusMessage = "Requesting DHCP lease...";
                    int elapsed = new DhcpClient(adapter).SendDiscoverPacket();
                    IPConfig cfg = adapter.IPConfig;
                    if (elapsed < 0 || cfg == null || cfg.Address == null || cfg.Address.IsZero)
                    {
                        lastStatusMessage = "DHCP timed out or returned no lease.";
                        RefreshDevicesLocked();
                        WriteMessage.WriteError(lastStatusMessage, "NET");
                        return false;
                    }

                    EnsureDnsConfiguredLocked(cfg.DefaultGateway?.ToString());
                    RefreshDevicesLocked();
                    lastStatusMessage = "DHCP lease active: " + currentAddress;
                    WriteMessage.WriteOK("DHCP configured (" + activeDevice.Name + "): IP=" + currentAddress +
                        " Mask=" + activeDevice.SubnetMask + " GW=" + activeDevice.DefaultGateway +
                        " DNS=" + activeDns, "NET");
                    return true;
                }
                catch (Exception ex)
                {
                    lastStatusMessage = "DHCP error: " + ex.Message;
                    WriteMessage.WriteError(lastStatusMessage, "NET");
                    return false;
                }
            }
#endif
        }

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
                CommandIO.WriteLine("  No supported network adapter detected (supported: Intel E1000E, VirtIO-Net).");
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

            if (!adapter.IsValid)
            {
                error = "No valid network adapter available.";
                return false;
            }

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

            if (selected < 0 && devices.Count > 0)
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

            activeIndex = selected;
            if (selected >= 0 && selected < devices.Count)
            {
                activeDevice = devices[selected];
                currentAddress = activeDevice.IpAddress;
                activeDns = activeDevice.DnsServer;
                isReady = activeDevice.Ready && activeDevice.LinkUp &&
                          !string.IsNullOrEmpty(currentAddress) && currentAddress != "0.0.0.0";
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
