using System.Diagnostics;

namespace ZonderqOS
{
    /// <summary>
    /// Lightweight runtime session telemetry. It stores only monotonic timestamps and
    /// counters, so normal desktop use does not allocate managed objects every frame.
    /// </summary>
    public static class SessionManager
    {
        private static long startedAt;
        private static long lastUnlockAt;
        private static uint unlockCount;

        public static bool IsActive
        {
            get { return SecurityContext.IsAuthenticated && startedAt > 0; }
        }

        public static uint UnlockCount
        {
            get { return unlockCount; }
        }

        public static ulong ElapsedSeconds
        {
            get
            {
                if (!IsActive || Stopwatch.Frequency <= 0)
                    return 0;

                long now = Stopwatch.GetTimestamp();
                long elapsed = now - startedAt;
                if (elapsed <= 0)
                    return 0;

                return (ulong)(elapsed / Stopwatch.Frequency);
            }
        }

        public static ulong SecondsSinceUnlock
        {
            get
            {
                if (!IsActive || lastUnlockAt <= 0 || Stopwatch.Frequency <= 0)
                    return 0;

                long now = Stopwatch.GetTimestamp();
                long elapsed = now - lastUnlockAt;
                if (elapsed <= 0)
                    return 0;

                return (ulong)(elapsed / Stopwatch.Frequency);
            }
        }

        internal static void BeginSession()
        {
            long now = Stopwatch.GetTimestamp();
            startedAt = now;
            lastUnlockAt = now;
            unlockCount = 0;
        }

        internal static void MarkUnlock()
        {
            if (!SecurityContext.IsAuthenticated)
                return;

            lastUnlockAt = Stopwatch.GetTimestamp();
            unlockCount++;
        }

        internal static void EndSession()
        {
            startedAt = 0;
            lastUnlockAt = 0;
            unlockCount = 0;
        }
    }
}
