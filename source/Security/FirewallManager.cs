using System;
using System.Collections.Generic;
using System.IO;

namespace ZonderqOS.Security
{
    public enum FirewallMode
    {
        Enabled = 1,
        Strict = 2,
        Disabled = 0
    }

    public enum RuleDirection
    {
        Inbound = 1,
        Outbound = 2,
        Both = 3
    }

    public enum RuleAction
    {
        Allow = 1,
        Drop = 2,
        Reject = 3
    }

    public enum RuleProtocol
    {
        ANY = 0,
        TCP = 1,
        UDP = 2,
        ICMP = 3
    }

    public sealed class FirewallRule
    {
        public int Id { get; set; }
        public int Port { get; set; }
        public RuleProtocol Protocol { get; set; }
        public RuleDirection Direction { get; set; }
        public RuleAction Action { get; set; }
        public string Description { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
    }

    public static class FirewallManager
    {
        private const string ConfigPath = "/etc/firewall.cfg";
        private static readonly object _lock = new object();

        public static FirewallMode Mode { get; private set; } = FirewallMode.Enabled;

        public static ulong AllowedPacketsCount { get; private set; } = 0;
        public static ulong BlockedPacketsCount { get; private set; } = 0;
        public static int LastBlockedPort { get; private set; } = 0;
        public static string LastBlockedProtocol { get; private set; } = "NONE";
        public static string LastBlockedTime { get; private set; } = "Brak";

        private static readonly List<FirewallRule> rules = new List<FirewallRule>(16);
        private static int nextRuleId = 1;
        private static bool isInitialized = false;

        public static void Initialize()
        {
            lock (_lock)
            {
                if (isInitialized)
                    return;

                try
                {
                    if (!Directory.Exists("/etc"))
                        Directory.CreateDirectory("/etc");

                    if (File.Exists(ConfigPath))
                    {
                        LoadConfig();
                    }
                    else
                    {
                        ResetDefaultRules();
                    }

                    isInitialized = true;
                }
                catch (Exception ex)
                {
                    SecurityLogger.LogEvent("WARN", "FirewallManager init warning: " + ex.Message);
                }
            }
        }

        public static void ResetDefaultRules()
        {
            lock (_lock)
            {
                rules.Clear();
                nextRuleId = 1;

                // Standard sensible defaults:
                // Outbound essential network traffic:
                AddRuleInternal(53, RuleProtocol.UDP, RuleDirection.Outbound, RuleAction.Allow, "Zapytania DNS (Domain Name System)");
                AddRuleInternal(80, RuleProtocol.TCP, RuleDirection.Outbound, RuleAction.Allow, "Ruch sieciowy WWW (HTTP)");
                AddRuleInternal(443, RuleProtocol.TCP, RuleDirection.Outbound, RuleAction.Allow, "Ruch szyfrowany SSL/TLS (HTTPS)");
                AddRuleInternal(123, RuleProtocol.UDP, RuleDirection.Outbound, RuleAction.Allow, "Synchronizacja czasu zegara (NTP)");

                // Inbound protection:
                AddRuleInternal(22, RuleProtocol.TCP, RuleDirection.Inbound, RuleAction.Drop, "Blokada zdalnego dostepu SSH");
                AddRuleInternal(23, RuleProtocol.TCP, RuleDirection.Inbound, RuleAction.Drop, "Blokada nieszyfrowanego protokolu Telnet");
                AddRuleInternal(445, RuleProtocol.TCP, RuleDirection.Inbound, RuleAction.Drop, "Blokada portu SMB / NetBIOS");

                Mode = FirewallMode.Enabled;
                SaveConfigLocked();
                SecurityLogger.LogEvent("INFO", "Firewall reset to default security rules.");
            }
        }

        private static void AddRuleInternal(int port, RuleProtocol proto, RuleDirection dir, RuleAction action, string desc)
        {
            rules.Add(new FirewallRule
            {
                Id = nextRuleId++,
                Port = port,
                Protocol = proto,
                Direction = dir,
                Action = action,
                Description = desc,
                IsEnabled = true
            });
        }

        public static void AddRule(int port, RuleProtocol proto, RuleDirection dir, RuleAction action, string desc)
        {
            lock (_lock)
            {
                AddRuleInternal(port, proto, dir, action, desc);
                SaveConfigLocked();
                SecurityLogger.LogEvent("INFO", "Firewall rule added: Port " + port + " (" + proto + "/" + action + ")");
            }
        }

        public static bool RemoveRule(int ruleId)
        {
            lock (_lock)
            {
                for (int i = 0; i < rules.Count; i++)
                {
                    if (rules[i].Id == ruleId)
                    {
                        rules.RemoveAt(i);
                        SaveConfigLocked();
                        return true;
                    }
                }
                return false;
            }
        }

        public static void SetMode(FirewallMode mode)
        {
            lock (_lock)
            {
                Mode = mode;
                SaveConfigLocked();
                SecurityLogger.LogEvent("INFO", "Firewall mode changed to: " + mode.ToString().ToUpperInvariant());
            }
        }

        public static bool EvaluatePacket(int port, RuleProtocol proto, RuleDirection dir, out string matchReason)
        {
            lock (_lock)
            {
                if (Mode == FirewallMode.Disabled)
                {
                    matchReason = "Zapora jest wyłączona (ruch dozwolony)";
                    AllowedPacketsCount++;
                    return true;
                }

                // Strict Mode: Stealth protection, drops all unexpected ICMP ping packets
                if (Mode == FirewallMode.Strict && proto == RuleProtocol.ICMP && dir == RuleDirection.Inbound)
                {
                    RecordBlock(port, proto.ToString());
                    matchReason = "Tryb ścisły (Stealth) - zablokowano zapytanie ICMP Echo";
                    return false;
                }

                // Scan matching rules
                for (int i = 0; i < rules.Count; i++)
                {
                    FirewallRule r = rules[i];
                    if (!r.IsEnabled)
                        continue;

                    bool dirMatch = (r.Direction == RuleDirection.Both) || (r.Direction == dir);
                    bool protoMatch = (r.Protocol == RuleProtocol.ANY) || (r.Protocol == proto);
                    bool portMatch = (r.Port == 0) || (r.Port == port);

                    if (dirMatch && protoMatch && portMatch)
                    {
                        if (r.Action == RuleAction.Allow)
                        {
                            matchReason = "Reguła #" + r.Id + " (" + r.Description + ")";
                            AllowedPacketsCount++;
                            return true;
                        }
                        else
                        {
                            matchReason = "Zablokowano przez regułę #" + r.Id + ": " + r.Description;
                            RecordBlock(port, proto.ToString());
                            return false;
                        }
                    }
                }

                // Default policy if no specific rule matched
                if (dir == RuleDirection.Outbound)
                {
                    // Outbound standard policy: Allow
                    matchReason = "Domyślna polityka ruchu wychodzącego: ALLOW";
                    AllowedPacketsCount++;
                    return true;
                }
                else
                {
                    // Inbound standard policy: Drop unsolicited traffic
                    matchReason = "Domyślna polityka ruchu przychodzącego: DROP";
                    RecordBlock(port, proto.ToString());
                    return false;
                }
            }
        }

        private static void RecordBlock(int port, string proto)
        {
            BlockedPacketsCount++;
            LastBlockedPort = port;
            LastBlockedProtocol = proto;
            LastBlockedTime = GetCurrentTimestamp();

            if (BlockedPacketsCount % 10 == 1)
            {
                SecurityLogger.LogEvent("WARN", "Firewall dropped unsolicited packet on port " + port + " (" + proto + ")");
            }
        }

        private static string GetCurrentTimestamp()
        {
            try
            {
                DateTime now = DateTime.Now;
                return now.Hour.ToString("D2") + ":" + now.Minute.ToString("D2") + ":" + now.Second.ToString("D2");
            }
            catch
            {
                return "00:00:00";
            }
        }

        private static void LoadConfig()
        {
            rules.Clear();
            nextRuleId = 1;

            if (!File.Exists(ConfigPath))
                return;

            string[] lines = File.ReadAllLines(ConfigPath);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                if (line.StartsWith("MODE="))
                {
                    string mStr = line.Substring(5).Trim();
                    if (Enum.TryParse<FirewallMode>(mStr, true, out var m))
                        Mode = m;
                    continue;
                }

                string[] parts = line.Split('|');
                if (parts.Length >= 5)
                {
                    int port;
                    int.TryParse(parts[0].Trim(), out port);
                    Enum.TryParse<RuleProtocol>(parts[1].Trim(), true, out var proto);
                    Enum.TryParse<RuleDirection>(parts[2].Trim(), true, out var dir);
                    Enum.TryParse<RuleAction>(parts[3].Trim(), true, out var action);
                    string desc = parts[4].Trim();

                    rules.Add(new FirewallRule
                    {
                        Id = nextRuleId++,
                        Port = port,
                        Protocol = proto,
                        Direction = dir,
                        Action = action,
                        Description = desc,
                        IsEnabled = true
                    });
                }
            }
        }

        private static void SaveConfigLocked()
        {
            try
            {
                using (var sw = new StreamWriter(ConfigPath, false))
                {
                    sw.WriteLine("# ZOnderqOS Firewall Configuration");
                    sw.WriteLine("MODE=" + Mode.ToString());
                    sw.WriteLine("# Format: Port|Protocol|Direction|Action|Description");
                    for (int i = 0; i < rules.Count; i++)
                    {
                        FirewallRule r = rules[i];
                        sw.WriteLine(r.Port + "|" + r.Protocol + "|" + r.Direction + "|" + r.Action + "|" + r.Description);
                    }
                }
                PermissionManager.SetPermission(ConfigPath, "root", 600);
            }
            catch (Exception ex)
            {
                SecurityLogger.LogEvent("ERROR", "Failed to save firewall configuration: " + ex.Message);
            }
        }

        public static List<FirewallRule> GetRules()
        {
            lock (_lock)
            {
                var copy = new List<FirewallRule>(rules.Count);
                for (int i = 0; i < rules.Count; i++)
                {
                    FirewallRule r = rules[i];
                    copy.Add(new FirewallRule
                    {
                        Id = r.Id,
                        Port = r.Port,
                        Protocol = r.Protocol,
                        Direction = r.Direction,
                        Action = r.Action,
                        Description = r.Description,
                        IsEnabled = r.IsEnabled
                    });
                }
                return copy;
            }
        }
    }
}
