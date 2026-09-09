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
using Cosmos.Kernel.System.Timer;

namespace ZonderqOS
{
    public static class Network
    {
        // Lista wszystkich wykrytych interfejsów w systemie
        public static List<INetworkDevice> Devices { get; private set; } = new List<INetworkDevice>();
        
        // Aktualnie aktywny (główny) interfejs używany przez stos
        public static INetworkDevice ActiveDevice { get; private set; }
        
        public static bool IsReady { get; private set; } = false;

        public static void Initialize()
        {
            try
            {
                WriteMessage.WriteInfo("Initializing TCP/IP stack & scanning network devices...", "NET");
                
                // Inicjalizacja głównego stosu sieciowego raz
                NetworkStack.Initialize();

                Devices.Clear();

                // Dynamiczne pobieranie wszystkich dostępnych kart sieciowych przez API NetworkManager
                int deviceCount = NetworkManager.DeviceCount;
                for (int i = 0; i < deviceCount; i++)
                {
                    var dev = NetworkManager.GetDevice(i);
                    if (dev != null)
                    {
                        Devices.Add(dev);
                    }
                }

                // Ustawienie pierwszej karty jako aktywnej domyślnie
                if (Devices.Count > 0)
                {
                    ActiveDevice = Devices[0];
                }
                else
                {
                    // Fallback na PrimaryDevice, gdyby kolekcja była pusta
                    var primary = NetworkManager.PrimaryDevice;
                    if (primary != null)
                    {
                        Devices.Add(primary);
                        ActiveDevice = primary;
                    }
                }

                if (Devices.Count == 0 || ActiveDevice == null)
                {
                    WriteMessage.WriteError("Nie wykryto żadnych interfejsów sieciowych.", "NET");
                    return;
                }

                // Inicjalizacja aktywnego urządzenia
                ActiveDevice.Initialize();
                IsReady = true;
                ConfigureDhcp();
                WriteMessage.WriteOK(
                    $"Inicjalizacja zakończona. Aktywna karta: {ActiveDevice.Name} (MAC: {ActiveDevice.MacAddress}), Znaleziono kart: {Devices.Count}", 
                    "NET"
                );
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Krytyczny błąd inicjalizacji sieci: {ex.Message}", "NET");
            }
        }

        /// <summary>
        /// Pozwala przełączyć aktywną kartę sieciową po indeksie z listy.
        /// </summary>
        public static bool SetActiveDevice(int index)
        {
            if (index >= 0 && index < Devices.Count)
            {
                ActiveDevice = Devices[index];
                if (!ActiveDevice.Ready)
                {
                    ActiveDevice.Initialize();
                }
                WriteMessage.WriteOK($"Przełączono aktywny interfejs na: {ActiveDevice.Name}", "NET");
                return true;
            }
            WriteMessage.WriteError("Nieprawidłowy indeks karty sieciowej.", "NET");
            return false;
        }

        /// <summary>
        /// Konfiguruje adresację IP dynamicznie przez serwer DHCP dla aktywnej karty[cite: 1].
        /// </summary>
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
                var dhcpClient = new DHCPClient();

                // Przeprowadza pełną wymianę DISCOVER -> OFFER -> REQUEST -> ACK[cite: 1]
                if (dhcpClient.SendDiscoverPacket() != -1)
                {
                    IPConfig config = NetworkConfigManager.Get(ActiveDevice);
                    IPConfig.Enable(ActiveDevice,new Address(192,168,1,100),new Address(255,255,255,0),new Address(192,168,1,100));
                    WriteMessage.WriteOK("DHCP skonfigurowane pomyślnie!", "NET");
                    WriteMessage.WriteInfo($"  Karta:   {ActiveDevice.Name}", "NET");
                    WriteMessage.WriteInfo($"  IP:      {config.IPAddress}", "NET");
                    WriteMessage.WriteInfo($"  Subnet:  {config.SubnetMask}", "NET");
                    WriteMessage.WriteInfo($"  Gateway: {config.DefaultGateway}", "NET");
                    return true;
                }
                
                WriteMessage.WriteError("Przekroczono czas oczekiwania na DHCP (Timeout)", "NET");
                return false;
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Błąd DHCP: {ex.Message}", "NET");
                return false;
            }
        }

        /// <summary>
        /// Wysyła testową wiadomość UDP[cite: 1].
        /// </summary>
        public static void SendUdpTest(string targetIp, int port, string text)
        {
            try
            {
                using var udpClient = new UdpClient(port);
                byte[] message = Encoding.ASCII.GetBytes(text);
                
                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Parse(targetIp), port);
                udpClient.Send(message, message.Length, remoteEndPoint);
                
                WriteMessage.WriteOK($"Wysłano UDP z interfejsu {ActiveDevice?.Name} do {targetIp}:{port}", "NET");
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Błąd UDP: {ex.Message}", "NET");
            }
        }

        /// <summary>
        /// Rozwiązuje nazwę domenową na adres IP za pomocą DnsClient[cite: 1].
        /// </summary>
        public static string ResolveDns(string domain)
        {
            try
            {
                DNSConfig.Add(new Address(1, 1, 1, 1)); // Cloudflare DNS[cite: 1]

                using var dnsClient = new DnsClient();
                dnsClient.Connect(new Address(1, 1, 1, 1));

                dnsClient.SendAsk(domain); // Wysłanie zapytania[cite: 1]
                Address resolvedAddress = dnsClient.Receive(5000); // Timeout 5s[cite: 1]

                if (resolvedAddress != null)
                {
                    return resolvedAddress.ToString();
                }
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Błąd DNS: {ex.Message}", "NET");
            }
            return null;
        }

        /// <summary>
        /// Wyświetla informacje o wszystkich wykrytych kartach sieciowych oraz status aktywnej.
        /// </summary>
        public static void ShowInterfaceInfo()
        {
            WriteMessage.WriteInfo($"--- ZonderqOS Network Interfaces ({Devices.Count} found) ---", "NET");
            
            for (int i = 0; i < Devices.Count; i++)
            {
                var dev = Devices[i];
                bool isActive = (dev == ActiveDevice);
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