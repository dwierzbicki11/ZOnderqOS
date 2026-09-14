using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using Cosmos.Kernel.System.Network;
using Cosmos.Kernel.System.Network.Config;
using Cosmos.Kernel.System.Network.IPv4;
using Cosmos.Kernel.System.Network.IPv4.UDP.DHCP;
using Cosmos.Kernel.System.Network.IPv4.UDP.DNS;

namespace ZonderqOS
{
    /// <summary>
    /// Stable application-facing view of a Cosmos network adapter. The raw
    /// INetworkDevice contract is internal in Cosmos Gen3 and must not leak
    /// into kernel applications.
    /// </summary>
    public sealed class NetworkDeviceInfo
    {
        internal NetworkAdapter Adapter { get; }

        internal NetworkDeviceInfo(NetworkAdapter adapter)
        {
            Adapter = adapter;
        }

        public string Name => Adapter.Name ?? "Unknown";
        public string MacAddress => Adapter.MacAddress?.ToString() ?? "00:00:00:00:00:00";
        public bool LinkUp => Adapter.LinkUp;
        public bool Ready => Adapter.Ready;
        public string IpAddress => Adapter.IPConfig?.Address?.ToString() ?? "0.0.0.0";
        public string SubnetMask => Adapter.IPConfig?.SubnetMask?.ToString() ?? "0.0.0.0";
        public string DefaultGateway => Adapter.IPConfig?.DefaultGateway?.ToString() ?? "0.0.0.0";
    }

    public static class Network
    {
        public static List<NetworkDeviceInfo> Devices { get; private set; } = new List<NetworkDeviceInfo>();
        public static NetworkDeviceInfo ActiveDevice { get; private set; }
        public static bool IsReady { get; private set; }
        public static string CurrentAddress => ActiveDevice?.IpAddress ?? "0.0.0.0";
        private static bool dnsConfigured;

        public static void Initialize()
        {
            try
            {
                WriteMessage.WriteInfo("Initializing TCP/IP stack & scanning network devices...", "NET");
                NetworkStack.Initialize();
                Devices.Clear();
                IsReady = false;
                ActiveDevice = null;

                int deviceCount = NetworkManager.DeviceCount;
                for (int i = 0; i < deviceCount; i++)
                {
                    NetworkAdapter adapter = NetworkManager.GetAdapter(i);
                    if (adapter.IsValid)
                        Devices.Add(new NetworkDeviceInfo(adapter));
                }

                if (Devices.Count == 0)
                {
                    NetworkAdapter primary = NetworkManager.Primary;
                    if (primary.IsValid)
                        Devices.Add(new NetworkDeviceInfo(primary));
                }

                if (Devices.Count == 0)
                {
                    WriteMessage.WriteError("Nie wykryto żadnych interfejsów sieciowych.", "NET");
                    return;
                }

                ActiveDevice = Devices[0];
                NetworkManager.Primary = ActiveDevice.Adapter;

                SystemSettings.Load();
                bool configured = ApplySavedConfiguration();
                if (!configured && !SystemSettings.NetworkUseDhcp)
                {
                    WriteMessage.WriteError("Statyczna konfiguracja IPv4 nie powiodła się. Próba DHCP...", "NET");
                    configured = ConfigureDhcp();
                }

                if (!configured)
                    return;

                IsReady = true;
                WriteMessage.WriteOK(
                    $"Inicjalizacja zakończona. Aktywna karta: {ActiveDevice.Name} (MAC: {ActiveDevice.MacAddress}), Znaleziono kart: {Devices.Count}",
                    "NET");
            }
            catch (Exception ex)
            {
                IsReady = false;
                WriteMessage.WriteError($"Krytyczny błąd inicjalizacji sieci: {ex.Message}", "NET");
            }
        }

        public static bool SetActiveDevice(int index)
        {
            if (index < 0 || index >= Devices.Count)
            {
                WriteMessage.WriteError("Nieprawidłowy indeks karty sieciowej.", "NET");
                return false;
            }

            ActiveDevice = Devices[index];
            NetworkManager.Primary = ActiveDevice.Adapter;

            bool configured = ApplySavedConfiguration();
            IsReady = configured;
            if (configured)
                WriteMessage.WriteOK($"Przełączono aktywny interfejs na: {ActiveDevice.Name}", "NET");
            return configured;
        }

        public static bool ApplySavedConfiguration()
        {
            SystemSettings.Load();
            if (SystemSettings.NetworkUseDhcp)
                return ConfigureDhcp();

            return ConfigureStatic(
                SystemSettings.StaticIpAddress,
                SystemSettings.StaticSubnetMask,
                SystemSettings.StaticGateway,
                SystemSettings.DnsServer);
        }

        public static bool ConfigureDhcp()
        {
            try
            {
                if (ActiveDevice == null || !ActiveDevice.Adapter.IsValid)
                {
                    WriteMessage.WriteError("Brak aktywnego interfejsu sieciowego.", "NET");
                    return false;
                }

                NetworkManager.Primary = ActiveDevice.Adapter;
                NetworkStack.RemoveAllConfigIP();
                WriteMessage.WriteInfo($"Wysyłanie pakietu DHCP DISCOVER na karcie {ActiveDevice.Name}...", "NET");
                using (var dhcpClient = new DHCPClient())
                {
                    if (dhcpClient.SendDiscoverPacket() == -1)
                    {
                        IsReady = false;
                        WriteMessage.WriteError("Przekroczono czas oczekiwania na DHCP (Timeout)", "NET");
                        return false;
                    }
                }

                IPConfig config = ActiveDevice.Adapter.IPConfig;
                if (config == null)
                {
                    IsReady = false;
                    WriteMessage.WriteError("DHCP zakończył się bez poprawnej konfiguracji IPv4.", "NET");
                    return false;
                }

                IsReady = true;
                WriteMessage.WriteOK("DHCP skonfigurowane pomyślnie!", "NET");
                WriteMessage.WriteInfo($"  Karta:   {ActiveDevice.Name}", "NET");
                WriteMessage.WriteInfo($"  IP:      {config.Address}", "NET");
                WriteMessage.WriteInfo($"  Subnet:  {config.SubnetMask}", "NET");
                WriteMessage.WriteInfo($"  Gateway: {config.DefaultGateway}", "NET");
                return true;
            }
            catch (Exception ex)
            {
                IsReady = false;
                WriteMessage.WriteError($"Błąd DHCP: {ex.Message}", "NET");
                return false;
            }
        }

        public static bool ConfigureStatic(string ip, string subnet, string gateway, string dns)
        {
            try
            {
                if (ActiveDevice == null || !ActiveDevice.Adapter.IsValid)
                {
                    WriteMessage.WriteError("Brak aktywnego interfejsu sieciowego.", "NET");
                    return false;
                }

                Address ipAddress = Address.Parse(ip);
                Address subnetAddress = Address.Parse(subnet);
                Address gatewayAddress = Address.Parse(gateway);
                Address dnsAddress = Address.Parse(dns);
                if (ipAddress == null || subnetAddress == null || gatewayAddress == null || dnsAddress == null)
                {
                    WriteMessage.WriteError("Nieprawidłowy adres w konfiguracji statycznej IPv4.", "NET");
                    return false;
                }

                NetworkManager.Primary = ActiveDevice.Adapter;
                NetworkStack.RemoveAllConfigIP();
                bool enabled = IPConfig.Enable(ActiveDevice.Adapter, ipAddress, subnetAddress, gatewayAddress);
                if (!enabled)
                {
                    IsReady = false;
                    return false;
                }

                DNSConfig.DNSNameservers.Clear();
                DNSConfig.Add(dnsAddress);
                dnsConfigured = true;
                IsReady = true;

                WriteMessage.WriteOK("Statyczne IPv4 skonfigurowane pomyślnie.", "NET");
                WriteMessage.WriteInfo($"  IP:      {ipAddress}", "NET");
                WriteMessage.WriteInfo($"  Subnet:  {subnetAddress}", "NET");
                WriteMessage.WriteInfo($"  Gateway: {gatewayAddress}", "NET");
                WriteMessage.WriteInfo($"  DNS:     {dnsAddress}", "NET");
                return true;
            }
            catch (Exception ex)
            {
                IsReady = false;
                WriteMessage.WriteError($"Błąd statycznej konfiguracji IPv4: {ex.Message}", "NET");
                return false;
            }
        }

        public static void SendUdpTest(string targetIp, int port, string text)
        {
            try
            {
                using var udpClient = new UdpClient(port);
                byte[] message = Encoding.ASCII.GetBytes(text ?? "");
                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Parse(targetIp), port);
                udpClient.Send(message, message.Length, remoteEndPoint);
                WriteMessage.WriteOK($"Wysłano UDP z interfejsu {ActiveDevice?.Name} do {targetIp}:{port}", "NET");
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Błąd UDP: {ex.Message}", "NET");
            }
        }

        public static string ResolveDns(string domain)
        {
            try
            {
                if (!dnsConfigured)
                {
                    if (DNSConfig.DNSNameservers.Count == 0)
                        DNSConfig.Add(new Address(1, 1, 1, 1));
                    dnsConfigured = true;
                }

                Address dnsServer = DNSConfig.DNSNameservers.Count > 0
                    ? DNSConfig.DNSNameservers[0]
                    : new Address(1, 1, 1, 1);

                using var dnsClient = new DnsClient();
                dnsClient.Connect(dnsServer);
                dnsClient.SendAsk(domain);
                Address resolvedAddress = dnsClient.Receive(5000);
                dnsClient.Close();

                return resolvedAddress?.ToString();
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Błąd DNS: {ex.Message}", "NET");
                return null;
            }
        }

        public static void ShowInterfaceInfo()
        {
            WriteMessage.WriteInfo($"--- ZonderqOS Network Interfaces ({Devices.Count} found) ---", "NET");

            for (int i = 0; i < Devices.Count; i++)
            {
                NetworkDeviceInfo dev = Devices[i];
                bool isActive = dev == ActiveDevice;
                string marker = isActive ? " [ACTIVE]" : "";

                WriteMessage.WriteInfo($"[{i}] {dev.Name}{marker}", "NET");
                WriteMessage.WriteInfo($"    MAC:      {dev.MacAddress}", "NET");
                WriteMessage.WriteInfo($"    Link Up:  {dev.LinkUp}", "NET");
                WriteMessage.WriteInfo($"    Ready:    {dev.Ready}", "NET");
                WriteMessage.WriteInfo($"    IPv4:     {dev.IpAddress}", "NET");
            }

            if (ActiveDevice != null)
                WriteMessage.WriteInfo($"Aktywny adres IPv4: {CurrentAddress}", "NET");
            else
                WriteMessage.WriteError("Brak aktywnego interfejsu.", "NET");
        }
    }
}
