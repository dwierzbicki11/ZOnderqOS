using System;
using System.IO;

namespace ZonderqOS
{
    /// <summary>
    /// Persistent desktop/system preferences. The file is only read once at GUI startup
    /// and rewritten on explicit user changes, so settings do not add allocations to the
    /// normal render loop.
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

        public static int IdleHeartbeatFrames
        {
            get
            {
                // Seconds on the clock need a one-second repaint. Otherwise the desktop
                // can remain almost completely event-driven while idle.
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
                // Corrupt/missing storage must never prevent the GUI from starting.
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
                    "timezone=" + TimeZoneOffsetHours + "\n";

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
            }
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
