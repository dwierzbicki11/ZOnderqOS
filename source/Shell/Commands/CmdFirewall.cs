using System;
using System.Collections.Generic;
using ZonderqOS.Security;

namespace ZonderqOS.Commands
{
    public sealed class CmdFirewall : ICommand
    {
        public string Name => "firewall";
        public string Description => "Control network firewall mode, packet filtering and port rules";

        public void Execute(string[] args, ref string currentPath)
        {
            FirewallManager.Initialize();

            if (args.Length < 2 || args[1].ToLowerInvariant() == "status")
            {
                PrintStatus();
                CommandIO.LastCommandSuccess = true;
                return;
            }

            string sub = args[1].ToLowerInvariant();
            if (sub == "on" || sub == "enable")
            {
                FirewallManager.SetMode(FirewallMode.Enabled);
                CommandIO.WriteLine("Firewall is now ENABLED (Standard protection).");
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (sub == "strict" || sub == "stealth")
            {
                FirewallManager.SetMode(FirewallMode.Strict);
                CommandIO.WriteLine("Firewall is now in STRICT mode (Stealth: ICMP/Ping dropped, unsolicited inbound blocked).");
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (sub == "off" || sub == "disable")
            {
                if (SecurityContext.CurrentUser != "root")
                {
                    WriteMessage.WriteError("Access denied. Root privileges required to disable firewall.", "SEC");
                    SecurityLogger.LogEvent("WARN", "Unauthorized attempt to disable firewall.");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                FirewallManager.SetMode(FirewallMode.Disabled);
                CommandIO.WriteLine("WARNING: Firewall is now DISABLED (Traffic unfiltered).");
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (sub == "reset")
            {
                if (SecurityContext.CurrentUser != "root")
                {
                    WriteMessage.WriteError("Access denied. Root privileges required to reset firewall rules.", "SEC");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                FirewallManager.ResetDefaultRules();
                CommandIO.WriteLine("Firewall rules reset to factory defaults.");
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (sub == "add" && args.Length >= 5)
            {
                if (SecurityContext.CurrentUser != "root")
                {
                    WriteMessage.WriteError("Access denied. Root privileges required to add firewall rules.", "SEC");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                int port;
                if (!int.TryParse(args[2], out port) || port < 0 || port > 65535)
                {
                    WriteMessage.WriteError("Invalid port number: " + args[2], "SEC");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                string protoStr = args[3].ToUpperInvariant();
                RuleProtocol proto = RuleProtocol.ANY;
                if (protoStr == "TCP") proto = RuleProtocol.TCP;
                else if (protoStr == "UDP") proto = RuleProtocol.UDP;
                else if (protoStr == "ICMP") proto = RuleProtocol.ICMP;

                string actionStr = args[4].ToLowerInvariant();
                RuleAction action = (actionStr == "allow") ? RuleAction.Allow : RuleAction.Drop;

                string desc = args.Length > 5 ? args[5] : "Custom rule";
                FirewallManager.AddRule(port, proto, RuleDirection.Inbound, action, desc);
                CommandIO.WriteLine("Rule added: Port " + port + " (" + proto + ") -> " + action);
                CommandIO.LastCommandSuccess = true;
                return;
            }

            CommandIO.WriteLine("Usage: firewall [status | on | strict | off | reset | add <port> <tcp|udp> <allow|drop> [desc]]");
            CommandIO.LastCommandSuccess = false;
        }

        private static void PrintStatus()
        {
            CommandIO.WriteLine("=== ZOnderqOS Network Firewall ===");
            CommandIO.WriteLine("Mode:            " + FirewallManager.Mode.ToString().ToUpperInvariant());
            CommandIO.WriteLine("Packets Allowed: " + FirewallManager.AllowedPacketsCount);
            CommandIO.WriteLine("Packets Blocked: " + FirewallManager.BlockedPacketsCount);
            CommandIO.WriteLine("Last Block:      Port " + FirewallManager.LastBlockedPort + " (" + FirewallManager.LastBlockedProtocol + ") at " + FirewallManager.LastBlockedTime);
            CommandIO.WriteLine();
            CommandIO.WriteLine(string.Format("{0,-4} {1,-8} {2,-8} {3,-10} {4,-8} {5}", "ID", "PORT", "PROTO", "DIR", "ACTION", "DESCRIPTION"));
            CommandIO.WriteLine(new string('-', 68));

            List<FirewallRule> rules = FirewallManager.GetRules();
            for (int i = 0; i < rules.Count; i++)
            {
                FirewallRule r = rules[i];
                string portStr = r.Port == 0 ? "ANY" : r.Port.ToString();
                CommandIO.WriteLine(string.Format("{0,-4} {1,-8} {2,-8} {3,-10} {4,-8} {5}",
                    "#" + r.Id, portStr, r.Protocol, r.Direction, r.Action, r.Description));
            }
            CommandIO.WriteLine(new string('-', 68));
        }
    }
}
