using System;
using ZonderqOS.GUI;

internal static class Program
{
    private static int checks;

    private static void Check(bool condition, string label)
    {
        checks++;
        if (!condition)
            throw new InvalidOperationException("Taskbar telemetry regression: " + label);
    }

    private static void TestDesktopHeartbeat()
    {
        // Monotonic clock with 1000 ticks per second.
        Check(DesktopHeartbeatGate.ShouldRefresh(0, -1, 1000, false),
            "initial desktop heartbeat");
        Check(!DesktopHeartbeatGate.ShouldRefresh(59000, 0, 1000, false),
            "minutes-only idle desktop does not redraw prematurely");
        Check(DesktopHeartbeatGate.ShouldRefresh(60000, 0, 1000, false),
            "minutes-only desktop redraws within one minute");
        Check(!DesktopHeartbeatGate.ShouldRefresh(999, 0, 1000, true),
            "seconds clock does not redraw too early");
        Check(DesktopHeartbeatGate.ShouldRefresh(1000, 0, 1000, true),
            "seconds clock redraws after one second");
        Check(DesktopHeartbeatGate.ShouldRefresh(100, 1000, 1000, false),
            "monotonic counter reset does not freeze clock");
        Check(DesktopHeartbeatGate.ShouldRefresh(10, 10, 0, false),
            "invalid frequency allows refresh");
        Check(!DesktopHeartbeatGate.ShouldRefresh(long.MaxValue - 1,
            long.MaxValue - 2, 1000, false), "high timestamps do not overflow");
        Check(!DesktopHeartbeatGate.ShouldRefresh(1000, 0,
            long.MaxValue, false), "huge frequencies saturate safely");
        Check(!DesktopHeartbeatGate.ShouldRefresh(10, 10, 1000, true),
            "same tick does not refresh");

        long previous = -1;
        int refreshes = 0;
        for (long now = 0; now <= 180000; now += 10)
        {
            if (!DesktopHeartbeatGate.ShouldRefresh(now, previous, 1000, false))
                continue;
            previous = now;
            refreshes++;
        }
        Check(refreshes == 4, "minutes-only desktop gets four refreshes in three minutes");
    }

    private static void Main()
    {
        TestDesktopHeartbeat();
        Check(TaskbarTelemetryGate.ShouldPoll(0, -1, 1000), "first sample");
        Check(TaskbarTelemetryGate.ShouldPoll(123, -1, 1000), "first nonzero sample");
        Check(!TaskbarTelemetryGate.ShouldPoll(1, 0, 1000), "no per-frame sample");
        Check(!TaskbarTelemetryGate.ShouldPoll(999, 0, 1000), "not before interval");
        Check(TaskbarTelemetryGate.ShouldPoll(1000, 0, 1000), "exact interval");
        Check(TaskbarTelemetryGate.ShouldPoll(1001, 0, 1000), "after interval");
        Check(!TaskbarTelemetryGate.ShouldPoll(1099, 1000, 1000), "second interval pending");
        Check(TaskbarTelemetryGate.ShouldPoll(2000, 1000, 1000), "second interval");
        Check(TaskbarTelemetryGate.ShouldPoll(100, 1000, 1000), "clock reset");
        Check(TaskbarTelemetryGate.ShouldPoll(100, 100, 0), "invalid frequency");
        Check(TaskbarTelemetryGate.ShouldPoll(100, 100, -1), "negative frequency");
        Check(!TaskbarTelemetryGate.ShouldPoll(100, 100, 1), "same tick");
        Check(TaskbarTelemetryGate.ShouldPoll(101, 100, 1), "one tick interval");
        Check(!TaskbarTelemetryGate.ShouldPoll(long.MaxValue - 1, long.MaxValue - 2, 1000),
            "high timestamp no overflow");
        Check(TaskbarTelemetryGate.ShouldPoll(long.MaxValue, long.MaxValue - 1000, 1000),
            "high timestamp interval");

        long last = -1;
        int samples = 0;
        for (long now = 0; now <= 10000; now += 10)
        {
            if (!TaskbarTelemetryGate.ShouldPoll(now, last, 1000))
                continue;
            last = now;
            samples++;
        }
        Check(samples == 11, "11 polls for 1001 frames over 10 seconds");
        Console.WriteLine("Taskbar telemetry host C#: " + checks + " PASS");
    }
}
