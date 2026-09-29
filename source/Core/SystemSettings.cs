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
        private const string SettingsTempPath = SettingsDirectory + "/settings.conf.new";
        private const string SettingsBackupPath = SettingsDirectory + "/settings.conf.bak";
        private const int SettingsSchemaVersion = 1;
        private const long MaxSettingsBytes = 64 * 1024;
        private const int MaxSettingsLines = 128;

        private static bool loaded;

        // 0 = oszczedny, 1 = zrownowazony, 2 = responsywny.
        public static int PerformanceProfile { get; private set; } = 1;
        public static bool ShowClockSeconds { get; private set; }
        public static bool ShowTaskbarDate { get; private set; } = true;
        public static bool ShowTrayStatus { get; private set; } = true;
        public static bool ShowDesktopIcons { get; private set; } = true;
        public static bool BootToGui { get; private set; } = true;
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
                    "schema=" + SettingsSchemaVersion + "\n" +
                    "performance=" + PerformanceProfile + "\n" +
                    "clock_seconds=" + BoolValue(ShowClockSeconds) + "\n" +
                    "taskbar_date=" + BoolValue(ShowTaskbarDate) + "\n" +
                    "tray_status=" + BoolValue(ShowTrayStatus) + "\n" +
                    "desktop_icons=" + BoolValue(ShowDesktopIcons) + "\n" +
                    "boot_gui=" + BoolValue(BootToGui) + "\n" +
                    "timezone=" + TimeZoneOffsetHours + "\n" +
                    "desktop_background=" + DesktopBackgroundMode + "\n" +
                    "accent_theme=" + AccentTheme + "\n" +
                    "auto_lock_minutes=" + AutoLockMinutes + "\n" +
                    "network_dhcp=" + BoolValue(NetworkUseDhcp) + "\n" +
                    "static_ip=" + StaticIpAddress + "\n" +
                    "static_mask=" + StaticSubnetMask + "\n" +
                    "static_gateway=" + StaticGateway + "\n" +
                    "dns_server=" + DnsServer + "\n";

                // Write-then-rename prevents a reset or filesystem error during
                // serialization from truncating the last known-good configuration.
                if (File.Exists(SettingsTempPath))
                    File.Delete(SettingsTempPath);

                File.WriteAllText(SettingsTempPath, content);
                PermissionManager.SetPermission(SettingsTempPath, "root", 600);

                if (File.Exists(SettingsBackupPath))
                    File.Delete(SettingsBackupPath);

                if (File.Exists(SettingsPath))
                    File.Move(SettingsPath, SettingsBackupPath);

                try
                {
                    File.Move(SettingsTempPath, SettingsPath);
                }
                catch
                {
                    // Best-effort rollback: retain the previous complete file.
                    if (!File.Exists(SettingsPath) && File.Exists(SettingsBackupPath))
                        File.Move(SettingsBackupPath, SettingsPath);
                    throw;
                }

                PermissionManager.SetPermission(SettingsPath, "root", 600);

                if (File.Exists(SettingsBackupPath))
                    File.Delete(SettingsBackupPath);

                PermissionManager.RemovePermission(SettingsTempPath);
                PermissionManager.RemovePermission(SettingsBackupPath);

                SecurityLogger.LogEvent("INFO", "System settings updated atomically.");
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    if (File.Exists(SettingsTempPath))
                        File.Delete(SettingsTempPath);
                    PermissionManager.RemovePermission(SettingsTempPath);
                }
                catch { }

                WriteMessage.WriteError($"Settings save failed: {ex.Message}", "CFG");
                SystemLogger.Log(SystemLogLevel.Error, "CFG", "Settings save failed: " + ex.Message);
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

        public static bool SetBootToGui(bool enabled)
        {
            BootToGui = enabled;
            return Save();
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


        public static bool ValidatePersistedFile(out string summary)
        {
            return ValidateSettingsFile(SettingsPath, true, out summary);
        }

        public static bool ValidateBackupFile(string path, out string summary)
        {
            return ValidateSettingsFile(path, false, out summary);
        }

        public static bool BackupTo(string destinationPath, out string error)
        {
            error = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(destinationPath))
                {
                    error = "Backup destination path is empty.";
                    return false;
                }

                if (!File.Exists(SettingsPath))
                {
                    error = "Persistent settings file does not exist yet.";
                    return false;
                }

                string parent = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                    Directory.CreateDirectory(parent);

                File.Copy(SettingsPath, destinationPath, true);
                PermissionManager.SetPermission(destinationPath, SecurityContext.CurrentUser, 600);

                string summary;
                if (!ValidateSettingsFile(destinationPath, false, out summary))
                {
                    try { File.Delete(destinationPath); } catch { }
                    error = "Backup validation failed: " + summary;
                    return false;
                }

                SystemLogger.Log(SystemLogLevel.Info, "CFG", "Settings backup written to " + destinationPath + ".");
                return true;
            }
            catch (Exception ex)
            {
                error = "Backup failed: " + ex.Message;
                return false;
            }
        }

        public static bool RestoreFrom(string sourcePath, out string error)
        {
            error = string.Empty;

            if (!SecurityContext.IsAuthenticated ||
                SecurityContext.CurrentUid != 0 ||
                !string.Equals(SecurityContext.CurrentUser, "root", StringComparison.Ordinal))
            {
                error = "Settings restore requires authenticated root.";
                return false;
            }

            string summary;
            if (!ValidateSettingsFile(sourcePath, false, out summary))
            {
                error = "Backup is invalid: " + summary;
                return false;
            }

            try
            {
                if (!Directory.Exists(SettingsDirectory))
                    Directory.CreateDirectory(SettingsDirectory);

                if (File.Exists(SettingsTempPath))
                    File.Delete(SettingsTempPath);

                File.Copy(sourcePath, SettingsTempPath, true);
                PermissionManager.SetPermission(SettingsTempPath, "root", 600);

                if (File.Exists(SettingsBackupPath))
                    File.Delete(SettingsBackupPath);

                if (File.Exists(SettingsPath))
                    File.Move(SettingsPath, SettingsBackupPath);

                try
                {
                    File.Move(SettingsTempPath, SettingsPath);
                }
                catch
                {
                    if (!File.Exists(SettingsPath) && File.Exists(SettingsBackupPath))
                        File.Move(SettingsBackupPath, SettingsPath);
                    throw;
                }

                PermissionManager.SetPermission(SettingsPath, "root", 600);
                if (File.Exists(SettingsBackupPath))
                    File.Delete(SettingsBackupPath);

                loaded = false;
                Load();

                SystemLogger.Log(SystemLogLevel.Warning, "CFG", "Persistent settings restored from backup.");
                SecurityLogger.LogEvent("INFO", "Root restored persistent system settings from backup.");
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    if (File.Exists(SettingsTempPath))
                        File.Delete(SettingsTempPath);
                }
                catch { }

                error = "Restore failed: " + ex.Message;
                return false;
            }
        }

        private static bool ValidateSettingsFile(string path, bool missingIsValid, out string summary)
        {
            summary = string.Empty;

            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    summary = "settings path is empty";
                    return false;
                }

                if (!File.Exists(path))
                {
                    summary = missingIsValid
                        ? "settings file does not exist; defaults are active"
                        : "settings file does not exist";
                    return missingIsValid;
                }

                FileInfo info = new FileInfo(path);
                if (info.Length < 0 || info.Length > MaxSettingsBytes)
                {
                    summary = "settings file size is invalid";
                    return false;
                }

                int lines = 0;
                int recognized = 0;
                int malformed = 0;
                int schema = 0;

                using (StreamReader reader = new StreamReader(path))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        lines++;
                        if (lines > MaxSettingsLines)
                        {
                            summary = "settings file exceeds line limit";
                            return false;
                        }

                        line = line.Trim();
                        if (line.Length == 0 || line[0] == '#')
                            continue;

                        int split = line.IndexOf('=');
                        if (split <= 0 || split >= line.Length - 1)
                        {
                            malformed++;
                            continue;
                        }

                        string key = line.Substring(0, split).Trim();
                        string value = line.Substring(split + 1).Trim();

                        if (key == "schema")
                        {
                            int parsedSchema;
                            if (!int.TryParse(value, out parsedSchema) || parsedSchema < 1)
                                malformed++;
                            else
                            {
                                schema = parsedSchema;
                                recognized++;
                            }
                        }
                        else if (IsKnownKey(key))
                        {
                            if (IsValidPersistedValue(key, value))
                                recognized++;
                            else
                                malformed++;
                        }
                    }
                }

                if (malformed > 0)
                {
                    summary = "malformed entries=" + malformed + ", recognized=" + recognized;
                    return false;
                }

                summary = "schema=" + (schema == 0 ? "legacy" : schema.ToString()) +
                          ", recognized=" + recognized +
                          ", lines=" + lines;
                return true;
            }
            catch (Exception ex)
            {
                summary = "validation failed: " + ex.Message;
                return false;
            }
        }


        public static bool RepairPersistedFile()
        {
            if (!SecurityContext.IsAuthenticated ||
                SecurityContext.CurrentUid != 0 ||
                !string.Equals(SecurityContext.CurrentUser, "root", StringComparison.Ordinal))
            {
                return false;
            }

            // Rewrites the currently loaded, already range-validated values into
            // the canonical schema. This does not invent values from malformed input.
            return Save();
        }


        private static bool IsValidPersistedValue(string key, string value)
        {
            int number;
            switch (key)
            {
                case "performance":
                    return int.TryParse(value, out number) && number >= 0 && number <= 2;
                case "clock_seconds":
                case "taskbar_date":
                case "tray_status":
                case "desktop_icons":
                case "boot_gui":
                case "network_dhcp":
                    return value == "0" || value == "1" ||
                           value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                           value.Equals("false", StringComparison.OrdinalIgnoreCase);
                case "timezone":
                    return int.TryParse(value, out number) && number >= -12 && number <= 14;
                case "desktop_background":
                case "accent_theme":
                    return int.TryParse(value, out number) && number >= 0 && number <= 2;
                case "auto_lock_minutes":
                    return int.TryParse(value, out number) && IsAutoLockPreset(number);
                case "static_ip":
                case "static_gateway":
                case "dns_server":
                    return IsValidIPv4(value);
                case "static_mask":
                    return IsValidSubnetMask(value);
                default:
                    return false;
            }
        }

        private static bool IsKnownKey(string key)
        {
            switch (key)
            {
                case "performance":
                case "clock_seconds":
                case "taskbar_date":
                case "tray_status":
                case "desktop_icons":
                case "boot_gui":
                case "timezone":
                case "desktop_background":
                case "accent_theme":
                case "auto_lock_minutes":
                case "network_dhcp":
                case "static_ip":
                case "static_mask":
                case "static_gateway":
                case "dns_server":
                    return true;
                default:
                    return false;
            }
        }

        private static void RestoreDefaultsInternal()
        {
            PerformanceProfile = 1;
            ShowClockSeconds = false;
            ShowTaskbarDate = true;
            ShowTrayStatus = true;
            ShowDesktopIcons = true;
            BootToGui = true;
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
                case "schema":
                    // Schema 1 is the current format. Newer schemas are ignored
                    // field-by-field instead of failing boot.
                    break;
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
                case "boot_gui":
                    BootToGui = ParseBool(value, BootToGui);
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
