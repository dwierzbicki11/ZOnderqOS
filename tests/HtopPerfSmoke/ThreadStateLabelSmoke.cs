using System;
using System.Diagnostics;
using Cosmos.Kernel.System.Diagnostics;
using ZonderqOS.SystemCore;

internal static class ThreadStateLabelSmoke
{
    private static long sink;

    private static void Assert(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    private static double Time(Func<long> action, int repeats)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++) sink ^= action();
        return (double)(Stopwatch.GetTimestamp() - start) * 1000d /
            Stopwatch.Frequency / repeats;
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }

    internal static void Run()
    {
        // Exact output for every cached index, and for invalid/future enum
        // values that must continue to use the original ToString fallback.
        for (int state = -4; state < 48; state++)
        {
            var value = (KernelThreadState)state;
            string expected = value.ToString().PadRight(10);
            string actual = HtopMonitor.ThreadStateText(value);
            Assert(actual == expected, "state text changed for " + state);
            if (state >= 0 && state < 16)
                Assert(object.ReferenceEquals(actual, HtopMonitor.ThreadStateText(value)),
                    "common state label was not reused " + state);
        }

        // Exercise production row construction and its slot cache invalidation:
        // a state transition must be visible even with the same TID/CPU/PID.
        var monitor = new HtopMonitor();
        var thread = new KernelThreadInfo
        {
            Id = 71, CpuId = 0, State = KernelThreadState.READY
        };
        string first = monitor.GetThreadRowLine(0, thread, 12, 20, "worker");
        Assert(first.Contains("READY      "), "READY label missing from real row");
        Assert(object.ReferenceEquals(first,
            monitor.GetThreadRowLine(0, thread, 12, 20, "worker")),
            "unchanged row cache was invalidated");
        thread.State = KernelThreadState.WAITING;
        string second = monitor.GetThreadRowLine(0, thread, 12, 20, "worker");
        Assert(!object.ReferenceEquals(first, second), "state transition kept stale row");
        Assert(second.Contains("WAITING    "), "WAITING label missing from real row");
        thread.State = (KernelThreadState)999;
        string unknown = monitor.GetThreadRowLine(0, thread, 12, 20, "worker");
        Assert(unknown.Contains("999       "), "future state fallback changed");
        Console.WriteLine("PASS: 73 htop state-label equivalence/cache/production-row checks");

        var states = new KernelThreadState[1024];
        for (int i = 0; i < states.Length; i++)
            states[i] = (KernelThreadState)(i % 16);

        Func<long> before = () =>
        {
            long checksum = 0;
            for (int i = 0; i < states.Length; i++)
            {
                string label = states[i].ToString().PadRight(10);
                checksum += label.Length + label[0] + label[label.Length - 1];
            }
            return checksum;
        };
        Func<long> after = () =>
        {
            long checksum = 0;
            for (int i = 0; i < states.Length; i++)
            {
                string label = HtopMonitor.ThreadStateText(states[i]);
                checksum += label.Length + label[0] + label[label.Length - 1];
            }
            return checksum;
        };
        Assert(before() == after(), "state label A/B checksum mismatch");
        for (int i = 0; i < 20; i++) { sink ^= before(); sink ^= after(); }
        long startBytes = GC.GetAllocatedBytesForCurrentThread();
        sink ^= before();
        long beforeBytes = GC.GetAllocatedBytesForCurrentThread() - startBytes;
        startBytes = GC.GetAllocatedBytesForCurrentThread();
        sink ^= after();
        long afterBytes = GC.GetAllocatedBytesForCurrentThread() - startBytes;
        Assert(beforeBytes > 0, "legacy state formatting did not allocate");
        Assert(afterBytes == 0, "cached state formatting allocated " + afterBytes);

        double[] oldMs = new double[7], newMs = new double[7];
        for (int sample = 0; sample < oldMs.Length; sample++)
        {
            if ((sample & 1) == 0)
            {
                oldMs[sample] = Time(before, 200);
                newMs[sample] = Time(after, 200);
            }
            else
            {
                newMs[sample] = Time(after, 200);
                oldMs[sample] = Time(before, 200);
            }
        }
        double a = Median(oldMs), b = Median(newMs);
        Console.WriteLine("HOST A/B htop 1024 padded state labels: legacy=" +
            a.ToString("F5") + " ms cached=" + b.ToString("F5") +
            " ms ratio=" + (a / b).ToString("F2") + "x allocations=" +
            beforeBytes + " B -> " + afterBytes +
            " B (7 alternating samples; not kernel CPU/idle)");
        GC.KeepAlive(sink);
    }
}
