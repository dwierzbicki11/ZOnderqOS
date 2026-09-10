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
        private const long MaxSettingsBytes = 64 * 1024;
        private const int MaxSettingsLines = 128;

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

        // Automatic desktop lock after inactivity. 0 disables the watchdog.
        // Supported presets are 0, 1, 5, 15 and 30 minutes.
        public static int AutoLockMinutes { get; private set; } = 5;

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
                    case 0: return 8000;
                    case 2: return 1000;
                    default: return 4000;
                }
            }
        }

        public static int TelemetryHeartbeatFrames
        {
            get
            {
                switch (PerformanceProfile)
                {
                    case 0: return 134;
                    case 2: return 34;
                    default: return 67;
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

        public static string AutoLockName
        {
            get
            {
                switch (AutoLockMinutes)
                {
                    case 0: return "WYLACZONA";
                    case 1: return "1 MIN";
                    case 15: return "15 MIN";
                    case 30: return "30 MIN";
                    default: return "5 MIN";
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

            RestoreDefaultsInternal();

            try
            {
                if (!File.Exists(SettingsPath))
                {
                    loaded = true;
                    return;
                }

                FileInfo info = new FileInfo(SettingsPath);
                if (info.Length > MaxSettingsBytes)
                {
                    WriteMessage.WriteError($"Settings file exceeds {MaxSettingsBytes / 1024} KB limit. Defaults loaded.", "CFG");
                    loaded = true;
                    return;
                }

                using (var reader = new StreamReader(SettingsPath))
                {
                    string line;
                    int lineCount = 0;
                    while ((line = reader.ReadLine()) != null && lineCount < MaxSettingsLines)
                    {
                        lineCount++;
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
            }
            catch (Exception ex)
            {
                RestoreDefaultsInternal();
                WriteMessage.WriteError($"Settings load failed: {ex.Message}. Defaults loaded.", "CFG");
            }
            finally
            {
                loaded = true;
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
                    "auto_lock_minutes=" + AutoLockMinutes + "\n" +
                    "network_dhcp=" + BoolValue(NetworkUseDhcp) + "\n" +
                    "static_ip=" + StaticIpAddress + "\n" +
                    "static_mask=" + StaticSubnetMask + "\n" +
                    "static_gateway=" + StaticGateway + "\n" +
                    "dns_server=" + DnsServer + "\n";

                File.WriteAllText(SettingsPath, content);
                PermissionManager.SetPermission(SettingsPath, "root", 600);
                SecurityLogger.LogEvent("INFO", "System settings updated from GUI.");
                return true;
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Settings save failed: {ex.Message}", "CFG");
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

        public static void CycleAutoLockTimeout()
        {
            switch (AutoLockMinutes)
            {
                case 0: AutoLockMinutes = 1; break;
                case 1: AutoLockMinutes = 5; break;
                case 5: AutoLockMinutes = 15; break;
                case 15: AutoLockMinutes = 30; break;
                default: AutoLockMinutes = 0; break;
            }
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
            if (!IsValidIPv4(ip) || !IsValidSubnetMask(mask) || !IsValidIPv4(gateway) || !IsValidIPv4(dns))
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
            AutoLockMinutes = 5;
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
                case "auto_lock_minutes":
                    if (int.TryParse(value, out number) && IsAutoLockPreset(number))
                        AutoLockMinutes = number;
                    break;
                case "network_dhcp":
                    NetworkUseDhcp = ParseBool(value, NetworkUseDhcp);
                    break;
                case "static_ip":
                    if (IsValidIPv4(value)) StaticIpAddress = value;
                    break;
                case "static_mask":
                    if (IsValidSubnetMask(value)) StaticSubnetMask = value;
                    break;
                case "static_gateway":
                    if (IsValidIPv4(value)) StaticGateway = value;
                    break;
                case "dns_server":
                    if (IsValidIPv4(value)) DnsServer = value;
                    break;
            }
        }

        private static bool IsAutoLockPreset(int value)
        {
            return value == 0 || value == 1 || value == 5 || value == 15 || value == 30;
        }

        private static bool IsValidIPv4(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 15)
                return false;

            int octetCount = 0;
            int octetValue = 0;
            int octetDigits = 0;

            for (int i = 0; i <= value.Length; i++)
            {
                char c = i < value.Length ? value[i] : '.';
                if (c == '.')
                {
                    if (octetDigits == 0 || octetValue > 255)
                        return false;

                    octetCount++;
                    octetValue = 0;
                    octetDigits = 0;
                    continue;
                }

                if (c < '0' || c > '9' || octetDigits >= 3)
                    return false;

                octetValue = octetValue * 10 + (c - '0');
                octetDigits++;
            }

            return octetCount == 4;
        }

        private static bool IsValidSubnetMask(string value)
        {
            if (!TryParseIPv4(value, out uint mask))
                return false;

            if (mask == 0)
                return false;

            uint inverted = ~mask;
            return (inverted & (inverted + 1)) == 0;
        }

        private static bool TryParseIPv4(string value, out uint address)
        {
            address = 0;
            if (!IsValidIPv4(value))
                return false;

            uint result = 0;
            int current = 0;
            for (int i = 0; i <= value.Length; i++)
            {
                if (i == value.Length || value[i] == '.')
                {
                    result = (result << 8) | (uint)current;
                    current = 0;
                }
                else
                {
                    current = current * 10 + (value[i] - '0');
                }
            }

            address = result;
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
