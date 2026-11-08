namespace ZonderqOS.GUI
{
    // Pure monotonic-time gate, kept separate from device access so the
    // production scheduling policy can be exercised on the host.
    internal static class TaskbarTelemetryGate
    {
        internal static bool ShouldPoll(long nowTicks, long lastTicks, long ticksPerSecond)
        {
            // A negative last tick marks the first sample. If the clock is
            // reset, resample immediately. An invalid frequency must not
            // permanently freeze storage-status updates.
            return lastTicks < 0 || ticksPerSecond <= 0 || nowTicks < lastTicks ||
                nowTicks - lastTicks >= ticksPerSecond;
        }
    }
}
