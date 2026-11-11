namespace ZonderqOS.GUI
{
    /// <summary>
    /// Bounds desktop clock staleness independently of loop FPS and hardware.
    /// A monotonic gate prevents idle profiles from delaying clock updates for
    /// multiple minutes while preserving frame-based performance heartbeats.
    /// </summary>
    internal static class DesktopHeartbeatGate
    {
        internal static bool ShouldRefresh(long nowTicks, long lastTicks,
            long ticksPerSecond, bool showSeconds)
        {
            if (lastTicks < 0 || ticksPerSecond <= 0 || nowTicks < lastTicks)
                return true;

            // Saturate the threshold on unexpected timer frequencies rather
            // than overflowing into a negative interval.
            long interval = showSeconds ? ticksPerSecond :
                ticksPerSecond > long.MaxValue / 60 ? long.MaxValue : ticksPerSecond * 60;

            return nowTicks - lastTicks >= interval;
        }
    }
}
