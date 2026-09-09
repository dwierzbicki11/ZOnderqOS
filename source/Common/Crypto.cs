using System;
using Cosmos.Kernel.HAL.X64.Devices.Clock;

namespace ZonderqOS
{
    public static class Crypto
    {
        public static string GenerateSalt()
        {
            try
            {
                var (y, m, d, h, min, s) = RTC.ReadTime();
                // Prosty, pseudo-losowy seed bazujący na sprzętowym czasie
                ulong ticks = (ulong)(y * 31536000 + m * 2592000 + d * 86400 + h * 3600 + min * 60 + s);
                return (ticks * 137).ToString("X");
            }
            catch
            {
                return "A9F2B"; // Fallback w przypadku błędu RTC
            }
        }

        public static string HashPassword(string password, string salt)
        {
            string combined = password + salt;
            
            // 64-bitowa implementacja algorytmu FNV-1a
            ulong hash = 14695981039346656037; 
            foreach (char c in combined)
            {
                hash ^= c;
                hash *= 1099511628211; 
            }
            
            return hash.ToString("X");
        }
    }
}