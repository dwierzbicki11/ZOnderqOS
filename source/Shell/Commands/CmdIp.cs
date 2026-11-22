using System;
using ZonderqOS.Security;

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
                    if (!RequireRoot("dhcp"))
                        return;
                    SystemSettings.SetNetworkModeDhcp(true);
                    CommandIO.LastCommandSuccess = Network.ConfigureDhcp();
                    return;

                case "static":
                    if (!RequireRoot("static"))
                        return;
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
                    if (!RequireRoot("device"))
                        return;
                    if (args.Length < 3 || !int.TryParse(args[2], out int deviceIndex))
                    {
                        CommandIO.WriteLine("Usage: ip device <index>");
                        CommandIO.LastCommandSuccess = false;
                        return;
                    }

                    CommandIO.LastCommandSuccess = Network.SetActiveDevice(deviceIndex);
                    return;

                case "apply":
                    if (!RequireRoot("apply"))
                        return;
                    CommandIO.LastCommandSuccess = Network.ApplySavedConfiguration();
                    return;

                default:
                    PrintUsage();
                    CommandIO.LastCommandSuccess = false;
                    return;
            }
        }

        private static bool RequireRoot(string action)
        {
            if (SecurityContext.IsAuthenticated && SecurityContext.CurrentUid == 0)
                return true;

            WriteMessage.WriteError("Root privileges are required for 'ip " + action + "'.", "NET");
            SecurityLogger.LogEvent("WARN",
                "Unauthorized 'ip " + action + "' attempt by " +
                (SecurityContext.CurrentUser ?? "?") + " (uid=" + SecurityContext.CurrentUid + ")");
            CommandIO.LastCommandSuccess = false;
            return false;
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
