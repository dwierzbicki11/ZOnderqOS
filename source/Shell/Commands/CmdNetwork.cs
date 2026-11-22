using System;
using ZonderqOS.Security;

namespace ZonderqOS.Commands
{
    public class CmdNetwork : ICommand
    {
        public string Name { get; } = "net";
        public string Description { get; } = "Network management: net [info | dhcp | dns <domain> | tcp <host> <port> | udp <ip> <port> <msg> | selftest | asus | device <id>]";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length < 2)
            {
                CommandIO.WriteLine("Usage:");
                CommandIO.WriteLine("  net info                         - Show network interface status");
                CommandIO.WriteLine("  net dhcp                         - DHCP configuration for the active network adapter");
                CommandIO.WriteLine("  net dns <domain>                 - Resolve domain name");
                CommandIO.WriteLine("  net tcp <host> <port>            - Test TCP connection and clean teardown");
                CommandIO.WriteLine("  net udp <ip> <port> <message>    - Send a UDP packet");
                CommandIO.WriteLine("  net selftest [https_url]         - Run 9-stage network, HTTPS TLS & RAM stability suite");
                CommandIO.WriteLine("  net asus                         - Run ASUS X540LA PCI/USB Tethering readiness & network suite");
                CommandIO.WriteLine("  net device <id>                  - Switch active network adapter");
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
                    if (!RequireRoot("dhcp"))
                        return;
                    CommandIO.LastCommandSuccess = Network.ConfigureDhcp();
                    return;

                case "dns":
                    if (args.Length < 3)
                    {
                        CommandIO.WriteLine("Usage: net dns <domain>");
                        CommandIO.LastCommandSuccess = false;
                        return;
                    }

                    string domain = args[2];
                    CommandIO.WriteLine("[NET] DNS lookup for: " + domain + "...");
                    string ip = Network.ResolveDns(domain);
                    if (string.IsNullOrEmpty(ip))
                    {
                        CommandIO.WriteLine("[DNS] Failed to resolve host name (timeout/error).");
                        CommandIO.LastCommandSuccess = false;
                        return;
                    }

                    CommandIO.WriteLine("[DNS] IP address for " + domain + ": " + ip);
                    CommandIO.LastCommandSuccess = true;
                    return;

                case "udp":
                    if (args.Length < 5)
                    {
                        CommandIO.WriteLine("Usage: net udp <ip> <port> <message>");
                        CommandIO.LastCommandSuccess = false;
                        return;
                    }

                    if (!int.TryParse(args[3], out int port) || port < 1 || port > 65535)
                    {
                        CommandIO.WriteLine("[ERROR] Invalid port number.");
                        CommandIO.LastCommandSuccess = false;
                        return;
                    }

                    Network.SendUdpTest(args[2], port, args[4]);
                    CommandIO.LastCommandSuccess = true;
                    return;

                case "tcp":
                    if (args.Length < 4 || !int.TryParse(args[3], out int tcpPort) || tcpPort < 1 || tcpPort > 65535)
                    {
                        CommandIO.WriteLine("Usage: net tcp <host> <port>");
                        CommandIO.LastCommandSuccess = false;
                        return;
                    }
                    if (Network.TestTcpConnection(args[2], tcpPort, 5000, out int tcpMs, out string tcpErr))
                    {
                        WriteMessage.WriteOK("TCP connected to " + args[2] + ":" + tcpPort + " in " + tcpMs + " ms and closed cleanly.", "NET");
                        CommandIO.LastCommandSuccess = true;
                    }
                    else
                    {
                        WriteMessage.WriteError("TCP connect failed: " + tcpErr, "NET");
                        CommandIO.LastCommandSuccess = false;
                    }
                    return;

                case "selftest":
                case "asus":
                    string urlArg = args.Length >= 3 ? args[2] : null;
                    CommandIO.LastCommandSuccess = Network.RunSelfTest(urlArg);
                    return;

                case "device":
                    if (!RequireRoot("device"))
                        return;
                    if (args.Length < 3 || !int.TryParse(args[2], out int devIndex))
                    {
                        CommandIO.WriteLine("Usage: net device <adapter_index>");
                        CommandIO.LastCommandSuccess = false;
                        return;
                    }

                    CommandIO.LastCommandSuccess = Network.SetActiveDevice(devIndex);
                    return;

                default:
                    CommandIO.WriteLine("Unknown subcommand: " + subCommand);
                    CommandIO.LastCommandSuccess = false;
                    return;
            }
        }

        private static bool RequireRoot(string action)
        {
            if (SecurityContext.IsAuthenticated && SecurityContext.CurrentUid == 0)
                return true;

            WriteMessage.WriteError("Root privileges are required for 'net " + action + "'.", "NET");
            SecurityLogger.LogEvent("WARN",
                "Unauthorized 'net " + action + "' attempt by " +
                (SecurityContext.CurrentUser ?? "?") + " (uid=" + SecurityContext.CurrentUid + ")");
            CommandIO.LastCommandSuccess = false;
            return false;
        }
    }
}
