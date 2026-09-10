using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.System.Network;
using Cosmos.Kernel.System.Network.Config;
using Cosmos.Kernel.System.Network.IPv4;
using Cosmos.Kernel.System.Network.IPv4.UDP.DHCP;
using Cosmos.Kernel.System.Network.IPv4.UDP.DNS;

namespace ZonderqOS
{
    public static class Network
    {
        public static List<INetworkDevice> Devices { get; private set; } = new List<INetworkDevice>();
        public static INetworkDevice ActiveDevice { get; private set; }
        public static bool IsReady { get; private set; }
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
                    var dev = NetworkManager.GetDevice(i);
                    if (dev != null)
                    {
                        Devices.Add(dev);
                    }
                }

                if (Devices.Count == 0)
                {
                    var primary = NetworkManager.PrimaryDevice;
                    if (primary != null)
                    {
                        Devices.Add(primary);
                    }
                }

                if (Devices.Count == 0)
                {
                    WriteMessage.WriteError("Nie wykryto żadnych interfejsów sieciowych.", "NET");
                    return;
                }

                ActiveDevice = Devices[0];
                if (!ActiveDevice.Ready)
                {
                    ActiveDevice.Initialize();
                }

                if (!ConfigureDhcp())
                {
                    return;
                }

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
            if (!ActiveDevice.Ready)
            {
                ActiveDevice.Initialize();
            }

            bool configured = ConfigureDhcp();
            IsReady = configured;
            if (configured)
            {
                WriteMessage.WriteOK($"Przełączono aktywny interfejs na: {ActiveDevice.Name}", "NET");
            }
            return configured;
        }

        public static bool ConfigureDhcp()
        {
            try
            {
                if (ActiveDevice == null)
                {
                    WriteMessage.WriteError("Brak aktywnego interfejsu sieciowego.", "NET");
                    return false;
                }

                WriteMessage.WriteInfo($"Wysyłanie pakietu DHCP DISCOVER na karcie {ActiveDevice.Name}...", "NET");
                using (var dhcpClient = new DHCPClient())
                {
                    if (dhcpClient.SendDiscoverPacket() == -1)
                    {
                        WriteMessage.WriteError("Przekroczono czas oczekiwania na DHCP (Timeout)", "NET");
                        return false;
                    }
                }

                IPConfig config = NetworkConfigManager.Get(ActiveDevice);
                if (config == null)
                {
                    WriteMessage.WriteError("DHCP zakończył się bez poprawnej konfiguracji IPv4.", "NET");
                    return false;
                }

                WriteMessage.WriteOK("DHCP skonfigurowane pomyślnie!", "NET");
                WriteMessage.WriteInfo($"  Karta:   {ActiveDevice.Name}", "NET");
                WriteMessage.WriteInfo($"  IP:      {config.IPAddress}", "NET");
                WriteMessage.WriteInfo($"  Subnet:  {config.SubnetMask}", "NET");
                WriteMessage.WriteInfo($"  Gateway: {config.DefaultGateway}", "NET");
                return true;
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Błąd DHCP: {ex.Message}", "NET");
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
                    DNSConfig.Add(new Address(1, 1, 1, 1));
                    dnsConfigured = true;
                }

                using var dnsClient = new DnsClient();
                dnsClient.Connect(new Address(1, 1, 1, 1));
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
                var dev = Devices[i];
                bool isActive = dev == ActiveDevice;
                string marker = isActive ? " [ACTIVE]" : "";

                WriteMessage.WriteInfo($"[{i}] {dev.Name}{marker}", "NET");
                WriteMessage.WriteInfo($"    MAC:      {dev.MacAddress}", "NET");
                WriteMessage.WriteInfo($"    Link Up:  {dev.LinkUp}", "NET");
                WriteMessage.WriteInfo($"    Ready:    {dev.Ready}", "NET");
            }

            if (ActiveDevice != null)
            {
                var ip = NetworkConfigManager.CurrentAddress;
                WriteMessage.WriteInfo($"Aktywny adres IPv4: {(ip != null ? ip.ToString() : "0.0.0.0")}", "NET");
            }
            else
            {
                WriteMessage.WriteError("Brak aktywnego interfejsu.", "NET");
            }
        }
    }
}