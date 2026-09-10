using System;
using System.IO;

namespace ZonderqOS
{
    /// <summary>
    /// Persistent desktop/system preferences. The file is read once and rewritten only
    /// after an explicit settings change, so normal GUI rendering stays allocation-free.
    /// </summary>
    public static class SystemSettings
    {
        private const string SettingsDirectory = "/etc/zonderq";
        private const string SettingsPath = SettingsDirectory + "/settings.conf";

        private static bool loaded;

        // 0 = oszczedny, 1 = zrownowazony, 2 = responsywny.
        public static int PerformanceProfile { get; private set; } = 1;
        public static bool ShowClockSeconds { get; private set; }
        public static bool ShowTaskbarDate { get; private set; } = true;
        public static bool ShowTrayStatus { get; private set; } = true;
        public static bool ShowDesktopIcons { get; private set; } = true;
        public static int TimeZoneOffsetHours { get; private set; } = 2;

        // Desktop background: 0 = embedded wallpaper, 1 = graphite, 2 = Cosmos navy.
        public static int DesktopBackgroundMode { get; private set; }

        // Shell accent: 0 = Cosmos blue, 1 = steel, 2 = emerald.
        public static int AccentTheme { get; private set; }

        // Persistent IPv4 profile. DHCP is the safe default.
        public static bool NetworkUseDhcp { get; private set; } = true;
        public static string StaticIpAddress { get; private set; } = "10.0.2.15";
        public static string StaticSubnetMask { get; private set; } = "255.255.255.0";
        public static string StaticGateway { get; private set; } = "10.0.2.2";
        public static string DnsServer { get; private set; } = "1.1.1.1";

        public static int IdleHeartbeatFrames
        {
            get
            {
                if (ShowClockSeconds)
                    return 67;

                switch (PerformanceProfile)
                {
                    case 0: return 8000; // ~120 s
                    case 2: return 1000; // ~15 s
                    default: return 4000; // ~60 s
                }
            }
        }

        public static int TelemetryHeartbeatFrames
        {
            get
            {
                switch (PerformanceProfile)
                {
                    case 0: return 134; // ~2 s
                    case 2: return 34;  // ~0.5 s
                    default: return 67; // ~1 s
                }
            }
        }

        public static string PerformanceProfileName
        {
            get
            {
                switch (PerformanceProfile)
                {
                    case 0: return "OSZCZEDNY";
                    case 2: return "RESPONSYWNY";
                    default: return "ZROWNOWAZONY";
                }
            }
        }

        public static string DesktopBackgroundName
        {
            get
            {
                switch (DesktopBackgroundMode)
                {
                    case 1: return "GRAFIT";
                    case 2: return "COSMOS NAVY";
                    default: return "TAPETA PNG";
                }
            }
        }

        public static string AccentThemeName
        {
            get
            {
                switch (AccentTheme)
                {
                    case 1: return "STEEL";
                    case 2: return "EMERALD";
                    default: return "COSMOS BLUE";
                }
            }
        }

        public static string NetworkModeName
        {
            get { return NetworkUseDhcp ? "DHCP" : "STATIC IPv4"; }
        }

        public static void Load()
        {
            if (loaded)
                return;

            loaded = true;
            try
            {
                if (!File.Exists(SettingsPath))
                    return;

                string[] lines = File.ReadAllLines(SettingsPath);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrEmpty(line) || line[0] == '#')
                        continue;

                    int split = line.IndexOf('=');
                    if (split <= 0 || split >= line.Length - 1)
                        continue;

                    string key = line.Substring(0, split).Trim();
                    string value = line.Substring(split + 1).Trim();
                    ApplyLoadedValue(key, value);
                }
            }
            catch
            {
                RestoreDefaultsInternal();
            }
        }

        public static bool Save()
        {
            try
            {
                if (!Directory.Exists(SettingsDirectory))
                    Directory.CreateDirectory(SettingsDirectory);

                string content =
                    "# ZOnderqOS Gen3 system settings\n" +
                    "performance=" + PerformanceProfile + "\n" +
                    "clock_seconds=" + BoolValue(ShowClockSeconds) + "\n" +
                    "taskbar_date=" + BoolValue(ShowTaskbarDate) + "\n" +
                    "tray_status=" + BoolValue(ShowTrayStatus) + "\n" +
                    "desktop_icons=" + BoolValue(ShowDesktopIcons) + "\n" +
                    "timezone=" + TimeZoneOffsetHours + "\n" +
                    "desktop_background=" + DesktopBackgroundMode + "\n" +
                    "accent_theme=" + AccentTheme + "\n" +
                    "network_dhcp=" + BoolValue(NetworkUseDhcp) + "\n" +
                    "static_ip=" + StaticIpAddress + "\n" +
                    "static_mask=" + StaticSubnetMask + "\n" +
                    "static_gateway=" + StaticGateway + "\n" +
                    "dns_server=" + DnsServer + "\n";

                File.WriteAllText(SettingsPath, content);
                SecurityLogger.LogEvent("INFO", "System settings updated from GUI.");
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void CyclePerformanceProfile()
        {
            PerformanceProfile++;
            if (PerformanceProfile > 2)
                PerformanceProfile = 0;
            Save();
        }

        public static void ToggleClockSeconds()
        {
            ShowClockSeconds = !ShowClockSeconds;
            Save();
        }

        public static void ToggleTaskbarDate()
        {
            ShowTaskbarDate = !ShowTaskbarDate;
            Save();
        }

        public static void ToggleTrayStatus()
        {
            ShowTrayStatus = !ShowTrayStatus;
            Save();
        }

        public static void ToggleDesktopIcons()
        {
            ShowDesktopIcons = !ShowDesktopIcons;
            Save();
        }

        public static void CycleDesktopBackground()
        {
            DesktopBackgroundMode++;
            if (DesktopBackgroundMode > 2)
                DesktopBackgroundMode = 0;
            Save();
        }

        public static void CycleAccentTheme()
        {
            AccentTheme++;
            if (AccentTheme > 2)
                AccentTheme = 0;
            Save();
        }

        public static void ShiftTimeZone(int deltaHours)
        {
            int value = TimeZoneOffsetHours + deltaHours;
            if (value < -12)
                value = 14;
            else if (value > 14)
                value = -12;

            TimeZoneOffsetHours = value;
            Save();
        }

        public static void SetNetworkModeDhcp(bool useDhcp)
        {
            NetworkUseDhcp = useDhcp;
            Save();
        }

        public static bool SetStaticNetwork(string ip, string mask, string gateway, string dns)
        {
            if (!IsAddressText(ip) || !IsAddressText(mask) || !IsAddressText(gateway) || !IsAddressText(dns))
                return false;

            StaticIpAddress = ip;
            StaticSubnetMask = mask;
            StaticGateway = gateway;
            DnsServer = dns;
            NetworkUseDhcp = false;
            return Save();
        }

        public static void RestoreDefaults()
        {
            RestoreDefaultsInternal();
            Save();
        }

        private static void RestoreDefaultsInternal()
        {
            PerformanceProfile = 1;
            ShowClockSeconds = false;
            ShowTaskbarDate = true;
            ShowTrayStatus = true;
            ShowDesktopIcons = true;
            TimeZoneOffsetHours = 2;
            DesktopBackgroundMode = 0;
            AccentTheme = 0;
            NetworkUseDhcp = true;
            StaticIpAddress = "10.0.2.15";
            StaticSubnetMask = "255.255.255.0";
            StaticGateway = "10.0.2.2";
            DnsServer = "1.1.1.1";
        }

        private static void ApplyLoadedValue(string key, string value)
        {
            int number;
            switch (key)
            {
                case "performance":
                    if (int.TryParse(value, out number) && number >= 0 && number <= 2)
                        PerformanceProfile = number;
                    break;
                case "clock_seconds":
                    ShowClockSeconds = ParseBool(value, ShowClockSeconds);
                    break;
                case "taskbar_date":
                    ShowTaskbarDate = ParseBool(value, ShowTaskbarDate);
                    break;
                case "tray_status":
                    ShowTrayStatus = ParseBool(value, ShowTrayStatus);
                    break;
                case "desktop_icons":
                    ShowDesktopIcons = ParseBool(value, ShowDesktopIcons);
                    break;
                case "timezone":
                    if (int.TryParse(value, out number) && number >= -12 && number <= 14)
                        TimeZoneOffsetHours = number;
                    break;
                case "desktop_background":
                    if (int.TryParse(value, out number) && number >= 0 && number <= 2)
                        DesktopBackgroundMode = number;
                    break;
                case "accent_theme":
                    if (int.TryParse(value, out number) && number >= 0 && number <= 2)
                        AccentTheme = number;
                    break;
                case "network_dhcp":
                    NetworkUseDhcp = ParseBool(value, NetworkUseDhcp);
                    break;
                case "static_ip":
                    if (IsAddressText(value)) StaticIpAddress = value;
                    break;
                case "static_mask":
                    if (IsAddressText(value)) StaticSubnetMask = value;
                    break;
                case "static_gateway":
                    if (IsAddressText(value)) StaticGateway = value;
                    break;
                case "dns_server":
                    if (IsAddressText(value)) DnsServer = value;
                    break;
            }
        }

        private static bool IsAddressText(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 15)
                return false;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if ((c < '0' || c > '9') && c != '.')
                    return false;
            }
            return true;
        }

        private static bool ParseBool(string value, bool fallback)
        {
            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase))
                return false;
            return fallback;
        }

        private static string BoolValue(bool value)
        {
            return value ? "1" : "0";
        }
    }
}
