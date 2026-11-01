using System;

namespace ZonderqOS.GUI.Apps
{
    /// <summary>
    /// Monotonic, frame-rate-independent gate for expensive Task Manager snapshots.
    /// A negative previous timestamp requests one immediate sample (e.g. after restore).
    /// </summary>
    internal static class TaskManagerRefreshGate
    {
        internal const long IntervalMilliseconds = 500;

        internal static bool ShouldRefresh(long nowTicks, long lastTicks, long frequency)
        {
            if (lastTicks < 0 || nowTicks < lastTicks || frequency <= 0)
                return true;

            // Divide the frequency before comparing to avoid overflow from
            // multiplying potentially large uptime timestamps by 1000.
            long intervalTicks = Math.Max(1L,
                frequency / 2L + (frequency % 2L));
            return nowTicks - lastTicks >= intervalTicks;
        }
    }
}
