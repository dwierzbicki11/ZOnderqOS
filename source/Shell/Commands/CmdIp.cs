using System;

namespace ZonderqOS.Commands
{
    public class CmdIp : ICommand
    {
        public string Name => "ip";
        public string Description => "IPv4 configuration: ip [info|dhcp|static|device|apply]";

        public void Execute(string[] args, ref string currentPath)
        {
            string action = args.Length > 1 ? args[1].ToLower() : "info";

            switch (action)
            {
                case "info":
                case "show":
                    Network.ShowInterfaceInfo();
                    CommandIO.LastCommandSuccess = Network.ActiveDevice != null;
                    return;

                case "dhcp":
                    SystemSettings.SetNetworkModeDhcp(true);
                    CommandIO.LastCommandSuccess = Network.ConfigureDhcp();
                    return;

                case "static":
                    if (args.Length < 6)
                    {
                        CommandIO.WriteLine("Usage: ip static <address> <mask> <gateway> <dns>");
                        CommandIO.LastCommandSuccess = false;
                        return;
                    }

                    if (!SystemSettings.SetStaticNetwork(args[2], args[3], args[4], args[5]))
                    {
                        CommandIO.WriteLine("ip: invalid static IPv4 configuration");
                        CommandIO.LastCommandSuccess = false;
                        return;
                    }

                    CommandIO.LastCommandSuccess = Network.ConfigureStatic(args[2], args[3], args[4], args[5]);
                    return;

                case "device":
                    if (args.Length < 3 || !int.TryParse(args[2], out int deviceIndex))
                    {
                        CommandIO.WriteLine("Usage: ip device <index>");
                        CommandIO.LastCommandSuccess = false;
                        return;
                    }

                    CommandIO.LastCommandSuccess = Network.SetActiveDevice(deviceIndex);
                    return;

                case "apply":
                    CommandIO.LastCommandSuccess = Network.ApplySavedConfiguration();
                    return;

                default:
                    PrintUsage();
                    CommandIO.LastCommandSuccess = false;
                    return;
            }
        }

        private static void PrintUsage()
        {
            CommandIO.WriteLine("Usage:");
            CommandIO.WriteLine("  ip info");
            CommandIO.WriteLine("  ip dhcp");
            CommandIO.WriteLine("  ip static <address> <mask> <gateway> <dns>");
            CommandIO.WriteLine("  ip device <index>");
            CommandIO.WriteLine("  ip apply");
        }
    }
}
