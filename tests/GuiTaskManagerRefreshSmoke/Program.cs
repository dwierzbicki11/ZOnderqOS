using System;
using ZonderqOS.GUI.Apps;

internal static class Program
{
    private static int assertions;

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition)
            throw new InvalidOperationException("Task Manager refresh gate: " + message);
    }

    public static int Main()
    {
        const long frequency = 10_000_000; // Stopwatch ticks per second.
        const long start = 30_000_000;
        const long interval = 5_000_000;

        Check(TaskManagerRefreshGate.IntervalMilliseconds == 500, "half-second contract");
        Check(TaskManagerRefreshGate.ShouldRefresh(start, -1, frequency), "first visible frame");
        Check(!TaskManagerRefreshGate.ShouldRefresh(start, start, frequency), "no duplicate sample");
        Check(!TaskManagerRefreshGate.ShouldRefresh(start + interval - 1, start, frequency),
            "not before half a second");
        Check(TaskManagerRefreshGate.ShouldRefresh(start + interval, start, frequency),
            "at half a second");
        Check(TaskManagerRefreshGate.ShouldRefresh(start + interval + 1, start, frequency),
            "after half a second");

        // One second of GUI time should yield the same number of samples
        // whether Update() is called at 2 FPS, 60 FPS or 240 FPS.
        foreach (int fps in new[] { 2, 10, 30, 60, 120, 240 })
        {
            long last = -1;
            int samples = 0;
            for (int frame = 0; frame <= fps * 4; frame++)
            {
                long now = start + frame * frequency / fps;
                if (!TaskManagerRefreshGate.ShouldRefresh(now, last, frequency))
                    continue;
                samples++;
                last = now;
            }
            Check(samples >= 7 && samples <= 9, "bounded cadence at " + fps + " FPS");
        }

        // Manual refresh (F5) resets the interval from the end of the sample.
        long manuallyRefreshedAt = start + 1_250_000;
        Check(!TaskManagerRefreshGate.ShouldRefresh(
            manuallyRefreshedAt + interval - 1, manuallyRefreshedAt, frequency),
            "manual refresh suppresses immediate duplicate");
        Check(TaskManagerRefreshGate.ShouldRefresh(
            manuallyRefreshedAt + interval, manuallyRefreshedAt, frequency),
            "manual refresh re-arms next interval");

        Check(TaskManagerRefreshGate.ShouldRefresh(100, 200, frequency),
            "monotonic clock regression re-arms");
        Check(TaskManagerRefreshGate.ShouldRefresh(100, 100, 0),
            "unknown stopwatch frequency is safe");
        Check(!TaskManagerRefreshGate.ShouldRefresh(long.MaxValue - 1,
            long.MaxValue - 2, frequency), "large uptime avoids overflow");
        Check(TaskManagerRefreshGate.ShouldRefresh(long.MaxValue,
            long.MaxValue - interval, frequency), "large uptime interval");

        // Odd frequency rounds upward, not earlier than half a second.
        Check(!TaskManagerRefreshGate.ShouldRefresh(5, 0, 11), "odd frequency before threshold");
        Check(TaskManagerRefreshGate.ShouldRefresh(6, 0, 11), "odd frequency at threshold");

        Console.WriteLine("Task Manager monotonic refresh C# PASS: " + assertions + " assertions");
        return 0;
    }
}
