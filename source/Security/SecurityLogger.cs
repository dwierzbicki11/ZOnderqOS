using System;
using System.IO;
using Cosmos.Kernel.HAL.X64.Devices.Clock;

namespace ZonderqOS
{
    public static class SecurityLogger
    {
        private static string LogPath = @"/var/log/auth.log";

        public static void Initialize()
        {
            try
            {
                if (!Directory.Exists("/var/log")) Directory.CreateDirectory("/var/log");
                
                if (!File.Exists(LogPath))
                {
                    File.WriteAllText(LogPath, $"[{GetTimestamp()}] [SYS] Security audit log initialized.\n");
                }
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Audit logger init failed: {ex.Message}", "SEC");
            }
        }

        public static void LogEvent(string severity, string message)
        {
            try
            {
                string currentUser = SecurityContext.CurrentUser ?? "unknown";
                string logEntry = $"[{GetTimestamp()}] [{severity}] [UID:{currentUser}] {message}\n";
                File.AppendAllText(LogPath, logEntry);
            }
            catch 
            {
                // W przypadku błędu dysku ignorujemy zapis, aby nie wywołać kernel panic
            }
        }

        private static string GetTimestamp()
        {
            try
            {
                // Dekonstrukcja krotki (tuple) zwracanej przez RTC.ReadTime()
                var (year, month, day, hour, minute, second) = RTC.ReadTime();

                // Formatowanie wartości do postaci dwucyfrowej (dodanie zera z przodu, jeśli trzeba)
                string strYear = "20" + year.ToString("D2");
                string strMonth = month.ToString("D2");
                string strDay = day.ToString("D2");
                string strHour = hour.ToString("D2");
                string strMinute = minute.ToString("D2");
                string strSecond = second.ToString("D2");

                return $"{strYear}-{strMonth}-{strDay} {strHour}:{strMinute}:{strSecond}";
            }
            catch
            {
                return "TIME_ERROR";
            }
        }
    }
}