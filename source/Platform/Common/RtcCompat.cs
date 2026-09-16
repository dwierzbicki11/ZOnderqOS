using System;

namespace ZonderqOS
{
    /// <summary>
    /// Application-facing clock facade shared by every architecture. Cosmos
    /// supplies DateTime from the active HAL/RTC implementation, so callers do
    /// not need to know whether x64 or ARM64 is underneath.
    ///
    /// ReadTime preserves the legacy ZonderqOS contract where year contains the
    /// final two digits because existing log formatting prefixes it with "20".
    /// </summary>
    internal static class RTC
    {
        public static (int year, int month, int day, int hour, int minute, int second) ReadTime()
        {
            DateTime now = DateTime.UtcNow;
            return (now.Year % 100, now.Month, now.Day, now.Hour, now.Minute, now.Second);
        }
    }
}
