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

        // Exercise the exact production keyboard navigation helper, including
        // wraparound and invalid states, without loading Cosmos graphics.
        for (int resource = 0; resource < 3; resource++)
        {
            Check(TaskManagerPerformanceNavigation.StepResource(resource, 1) ==
                (resource + 1) % 3, "next performance resource " + resource);
            Check(TaskManagerPerformanceNavigation.StepResource(resource, -1) ==
                (resource + 2) % 3, "previous performance resource " + resource);
            Check(TaskManagerPerformanceNavigation.StepResource(resource, 0) ==
                resource, "unchanged performance resource " + resource);
        }
        Check(TaskManagerPerformanceNavigation.StepResource(-1, 0) == 0,
            "invalid resource resets to CPU");
        Check(TaskManagerPerformanceNavigation.StepResource(3, -1) == 2,
            "invalid resource safely wraps");

        foreach (int count in new[] { int.MinValue, -1, 0, 1, 2, 16, 256, int.MaxValue })
        {
            int last = count > 0 ? count - 1 : 0;
            foreach (int page in new[] { int.MinValue, -10, -1, 0, 1, 2, 15, 16, 255, int.MaxValue })
            {
                int expected = Math.Max(0, Math.Min(page, last));
                Check(TaskManagerPerformanceNavigation.ClampLogicalPage(page, count) ==
                    expected, "logical CPU page clamp " + page + "/" + count);
            }
        }

        // A selection hidden by manual scrolling must not be killable.
        // Verify boundary/overflow cases against the production policy.
        foreach (int count in new[] { 0, 1, 2, 16, 128, int.MaxValue })
        foreach (int scroll in new[] { -1, 0, 1, 15, 16, 127, int.MaxValue })
        foreach (int rows in new[] { -1, 0, 1, 2, 8, 16, int.MaxValue })
        foreach (int selected in new[] { -1, 0, 1, 15, 16, 127, int.MaxValue })
        {
            bool expected = selected >= 0 && selected < count &&
                scroll >= 0 && rows > 0 && selected >= scroll &&
                (long)selected - scroll < rows;
            Check(TaskManagerSelectionPolicy.IsVisible(selected, scroll, rows, count) == expected,
                "destructive selection visibility " + selected + "/" + scroll + "/" + rows + "/" + count);
        }

        Console.WriteLine("Task Manager refresh/navigation C# PASS: " + assertions + " assertions");
        return 0;
    }
}
