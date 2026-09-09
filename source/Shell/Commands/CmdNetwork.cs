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
                CommandIO.WriteLine("Użycie:");
                CommandIO.WriteLine("  net info                         - Wyświetl stan interfejsów");
                CommandIO.WriteLine("  net dhcp                         - Konfiguracja DHCP dla aktywnej karty");
                CommandIO.WriteLine("  net dns <domena>                 - Rozwiąż nazwę domeny");
                CommandIO.WriteLine("  net udp <ip> <port> <wiadomość>  - Wyślij pakiet UDP");
                CommandIO.WriteLine("  net device <id>                  - Przełącz aktywną kartę sieciową");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string subCommand = args[1].ToLower();
            switch (subCommand)
            {
                case "info": Network.ShowInterfaceInfo(); break;
                case "dhcp": Network.ConfigureDhcp(); break;
                case "dns":
                    if (args.Length < 3) { CommandIO.WriteLine("Użycie: net dns <domena>"); CommandIO.LastCommandSuccess = false; return; }
                    string domain = args[2];
                    CommandIO.WriteLine($"[NET] Odpytywanie DNS dla: {domain}...");
                    string ip = Network.ResolveDns(domain);
                    CommandIO.WriteLine(ip != null ? $"[DNS] Adres IP dla {domain}: {ip}" : "[DNS] Nie udało się rozwiązać nazwy (Timeout/Błąd).");
                    break;
                case "udp":
                    if (args.Length < 5) { CommandIO.WriteLine("Użycie: net udp <ip> <port> <wiadomość>"); CommandIO.LastCommandSuccess = false; return; }
                    if (!int.TryParse(args[3], out int port)) { CommandIO.WriteLine("[ERROR] Nieprawidłowy numer portu."); CommandIO.LastCommandSuccess = false; return; }
                    Network.SendUdpTest(args[2], port, args[4]);
                    break;
                case "device":
                    if (args.Length < 3 || !int.TryParse(args[2], out int devIndex)) { CommandIO.WriteLine("Użycie: net device <indeks_karty>"); CommandIO.LastCommandSuccess = false; return; }
                    Network.SetActiveDevice(devIndex);
                    break;
                default:
                    CommandIO.WriteLine($"Nieznana podkomenda: {subCommand}");
                    CommandIO.LastCommandSuccess = false;
                    return;
            }
            CommandIO.LastCommandSuccess = true;
        }
    }
}