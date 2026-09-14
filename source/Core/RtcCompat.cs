using System;

namespace ZonderqOS
{
    /// <summary>
    /// Application-facing clock facade for Cosmos Gen3. The HAL RTC device is internal;
    /// System.DateTime is the supported public surface and is plugged by Cosmos to the
    /// platform RTC plus the monotonic hardware counter.
    ///
    /// ReadTime intentionally preserves the legacy ZonderqOS contract where year is the
    /// final two digits, because existing log formatting prefixes it with "20".
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
