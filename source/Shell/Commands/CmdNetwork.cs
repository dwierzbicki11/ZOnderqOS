using System;

namespace ZonderqOS.Commands
{
    public class CmdNetwork : ICommand
    {
        public string Name { get; } = "net";
        public string Description { get; } = "Zarzadzanie siecia: net [info | dhcp | dns <domena> | udp <ip> <port> <tekst> | device <id>]";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length < 2)
            {
                CommandIO.WriteLine("Uzycie:");
                CommandIO.WriteLine("  net info                         - Wyswietl stan interfejsow");
                CommandIO.WriteLine("  net dhcp                         - Konfiguracja DHCP dla aktywnej karty");
                CommandIO.WriteLine("  net dns <domena>                 - Rozwiaz nazwe domeny");
                CommandIO.WriteLine("  net udp <ip> <port> <wiadomosc>  - Wyslij pakiet UDP");
                CommandIO.WriteLine("  net device <id>                  - Przelacz aktywna karte sieciowa");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string subCommand = args[1].ToLower();
            switch (subCommand)
            {
                case "info":
                    Network.ShowInterfaceInfo();
                    CommandIO.LastCommandSuccess = Network.ActiveDevice != null;
                    return;

                case "dhcp":
                    CommandIO.LastCommandSuccess = Network.ConfigureDhcp();
                    return;

                case "dns":
                    if (args.Length < 3)
                    {
                        CommandIO.WriteLine("Uzycie: net dns <domena>");
                        CommandIO.LastCommandSuccess = false;
                        return;
                    }

                    string domain = args[2];
                    CommandIO.WriteLine("[NET] Odpytywanie DNS dla: " + domain + "...");
                    string ip = Network.ResolveDns(domain);
                    if (string.IsNullOrEmpty(ip))
                    {
                        CommandIO.WriteLine("[DNS] Nie udalo sie rozwiazac nazwy (Timeout/Blad).");
                        CommandIO.LastCommandSuccess = false;
                        return;
                    }

                    CommandIO.WriteLine("[DNS] Adres IP dla " + domain + ": " + ip);
                    CommandIO.LastCommandSuccess = true;
                    return;

                case "udp":
                    if (args.Length < 5)
                    {
                        CommandIO.WriteLine("Uzycie: net udp <ip> <port> <wiadomosc>");
                        CommandIO.LastCommandSuccess = false;
                        return;
                    }

                    if (!int.TryParse(args[3], out int port) || port < 1 || port > 65535)
                    {
                        CommandIO.WriteLine("[ERROR] Nieprawidlowy numer portu.");
                        CommandIO.LastCommandSuccess = false;
                        return;
                    }

                    Network.SendUdpTest(args[2], port, args[4]);
                    CommandIO.LastCommandSuccess = true;
                    return;

                case "device":
                    if (args.Length < 3 || !int.TryParse(args[2], out int devIndex))
                    {
                        CommandIO.WriteLine("Uzycie: net device <indeks_karty>");
                        CommandIO.LastCommandSuccess = false;
                        return;
                    }

                    CommandIO.LastCommandSuccess = Network.SetActiveDevice(devIndex);
                    return;

                default:
                    CommandIO.WriteLine("Nieznana podkomenda: " + subCommand);
                    CommandIO.LastCommandSuccess = false;
                    return;
            }
        }
    }
}
