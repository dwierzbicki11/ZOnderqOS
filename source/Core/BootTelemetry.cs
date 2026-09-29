using System;
using System.Diagnostics;
using System.Threading;

namespace ZonderqOS.SystemCore
{
    public static class BootTelemetry
    {
        private static long bootTimestamp;

        public static void Initialize()
        {
            long now = Stopwatch.GetTimestamp();
            if (now <= 0)
                now = 1;

            Interlocked.CompareExchange(ref bootTimestamp, now, 0);
        }

        public static long UptimeTicks
        {
            get
            {
                long start = Volatile.Read(ref bootTimestamp);
                if (start <= 0)
                    return 0;

                long now = Stopwatch.GetTimestamp();
                return now >= start ? now - start : 0;
            }
        }

        public static double UptimeSeconds
        {
            get
            {
                long ticks = UptimeTicks;
                return Stopwatch.Frequency > 0
                    ? (double)ticks / Stopwatch.Frequency
                    : 0d;
            }
        }
    }
}
