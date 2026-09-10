using System;
using System.Diagnostics;
using Cosmos.Kernel.HAL.X64.Devices.Clock;

namespace ZonderqOS
{
    public static class Crypto
    {
        private static ulong saltCounter;

        public static string GenerateSalt()
        {
            // Cosmos Gen3 does not currently expose a CSPRNG through this project. Mix RTC,
            // the monotonic high-resolution timestamp and a per-boot counter so accounts or
            // password changes created in the same RTC second still receive different salts.
            ulong rtcValue = 0;
            try
            {
                var (y, m, d, h, min, s) = RTC.ReadTime();
                rtcValue = (ulong)y * 31536000UL + (ulong)m * 2592000UL +
                           (ulong)d * 86400UL + (ulong)h * 3600UL +
                           (ulong)min * 60UL + (ulong)s;
            }
            catch
            {
                rtcValue = 0xA9F2BUL;
            }

            ulong monotonic = 0;
            try
            {
                long timestamp = Stopwatch.GetTimestamp();
                monotonic = timestamp > 0 ? (ulong)timestamp : 0UL;
            }
            catch
            {
                monotonic = 0UL;
            }

            saltCounter++;
            ulong mixed = rtcValue ^ RotateLeft(monotonic, 17) ^
                          (saltCounter * 0x9E3779B97F4A7C15UL);
            mixed = Mix64(mixed);
            ulong second = Mix64(mixed ^ monotonic ^ 0xD6E8FEB86659FD93UL);
            return mixed.ToString("X16") + second.ToString("X16");
        }

        public static string HashPassword(string password, string salt)
        {
            string combined = password + salt;

            // Keep the existing FNV-1a representation for on-disk compatibility with
            // current /etc/shadow entries. Authentication throttling is implemented by
            // AuthenticationGuard; a future shadow-format version can migrate the KDF
            // without invalidating existing installations.
            ulong hash = 14695981039346656037UL;
            foreach (char c in combined)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }

            return hash.ToString("X");
        }

        private static ulong Mix64(ulong value)
        {
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            value ^= value >> 31;
            return value;
        }

        private static ulong RotateLeft(ulong value, int shift)
        {
            return (value << shift) | (value >> (64 - shift));
        }
    }
}
