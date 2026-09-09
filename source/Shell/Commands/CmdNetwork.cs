using System;

namespace ZonderqOS.Commands
{
    public class CmdNetwork : ICommand
    {
        public string Name { get; } = "net";
        public string Description { get; } = "Zarządzanie siecią: net [info | dhcp | dns <domena> | udp <ip> <port> <tekst> | device <id>]";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Użycie:");
                Console.WriteLine("  net info                         - Wyświetl stan interfejsów");
                Console.WriteLine("  net dhcp                         - Konfiguracja DHCP dla aktywnej karty");
                Console.WriteLine("  net dns <domena>                 - Rozwiąż nazwę domeny");
                Console.WriteLine("  net udp <ip> <port> <wiadomość>  - Wyślij pakiet UDP");
                Console.WriteLine("  net device <id>                  - Przełącz aktywną kartę sieciową");
                return;
            }

            string subCommand = args[1].ToLower();

            switch (subCommand)
            {
                case "info":
                    Network.ShowInterfaceInfo();
                    break;

                case "dhcp":
                    Network.ConfigureDhcp();
                    break;

                case "dns":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Użycie: net dns <domena>");
                        return;
                    }
                    string domain = args[2];
                    Console.WriteLine($"[NET] Odpytywanie DNS dla: {domain}...");
                    string ip = Network.ResolveDns(domain);
                    if (ip != null)
                    {
                        Console.WriteLine($"[DNS] Adres IP dla {domain}: {ip}");
                    }
                    else
                    {
                        Console.WriteLine("[DNS] Nie udało się rozwiązać nazwy (Timeout/Błąd).");
                    }
                    break;

                case "udp":
                    if (args.Length < 5)
                    {
                        Console.WriteLine("Użycie: net udp <ip> <port> <wiadomość>");
                        Console.WriteLine("Przykład: net udp 10.0.2.2 4242 TestMessage");
                        return;
                    }
                    string targetIp = args[2];
                    if (!int.TryParse(args[3], out int port))
                    {
                        Console.WriteLine("[ERROR] Nieprawidłowy numer portu.");
                        return;
                    }
                    string text = args[4];
                    Network.SendUdpTest(targetIp, port, text);
                    break;

                case "device":
                    if (args.Length < 3 || !int.TryParse(args[2], out int devIndex))
                    {
                        Console.WriteLine("Użycie: net device <indeks_karty>");
                        return;
                    }
                    Network.SetActiveDevice(devIndex);
                    break;

                default:
                    Console.WriteLine($"Nieznana podkomenda: {subCommand}");
                    break;
            }
        }
    }
}