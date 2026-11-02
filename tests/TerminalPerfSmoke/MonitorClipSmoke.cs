using System;
using System.Collections.Generic;
using System.Diagnostics;
using ZonderqOS.GUI;

// Exercises production ShowMonitor on both sides of the A/B. The only
// difference is a host-only switch restoring its former Substring expression.
internal static class MonitorClipSmoke
{
    private static long sink;
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("monitor clip: " + message);
    }

    private static void CheckOutput(TerminalBox box, IReadOnlyList<string> lines, int first)
    {
        int width = Math.Max(1, (box.Width - 34) / box.Font.Width);
        int visible = Math.Max(1, (box.Height - 44) / box.Font.Height);
        int count = Math.Max(1, visible - 1);
        int row = 0;
        for (int i = Math.Max(0, first); i < lines.Count && count > 0; i++, count--, row++)
        {
            string expected = lines[i].Length > width
                ? lines[i].Substring(0, width) : lines[i];
            Check(row < box.OutputLines.Count && box.OutputLines[row] == expected,
                "row " + row + " width " + box.Width + " first " + first);
        }
        Check(row == box.OutputLines.Count, "visible row count");
    }

    private static void Verify()
    {
        int checks = 0;
        var lines = new string[96];
        for (int i = 0; i < lines.Length; i++)
            lines[i] = "CPU " + i + " | " + new string((char)('A' + i % 26), 180);
        foreach (int width in new[] { 40, 120, 240, 800, 1366, 1920 })
        foreach (int height in new[] { 60, 160, 520, 768 })
        {
            var box = new TerminalBox(0, 0, width, height);
            foreach (int first in new[] { -10, 0, 1, 20, 95, 96, int.MaxValue })
            {
                box.ShowMonitor(lines, first);
                CheckOutput(box, lines, first);
                checks++;
            }
            box.ShowMonitor(lines, 0);
            CheckOutput(box, lines, 0);
            box.Width = 120;
            box.ShowMonitor(lines, 0);
            CheckOutput(box, lines, 0);
            box.Font = new Cosmos.Kernel.System.Graphics.Fonts.Font { Width = 10, Height = 24 };
            box.ShowMonitor(lines, 0);
            CheckOutput(box, lines, 0);
            checks += 3;
        }

        var single = new TerminalBox(0, 0, 120, 160);
        string source = new string('Z', 200);
        var one = new[] { source };
        single.ShowMonitor(one, 0);
        string firstClip = single.OutputLines[0];
        single.ShowMonitor(one, 0);
        Check(ReferenceEquals(firstClip, single.OutputLines[0]),
            "unchanged htop source must reuse clipped string"); checks++;
        one[0] = new string('Z', 200);
        single.ShowMonitor(one, 0);
        string changed = single.OutputLines[0];
        Check(changed == firstClip && !ReferenceEquals(changed, firstClip),
            "new source identity invalidates clip"); checks++;
        single.Width = 160;
        single.ShowMonitor(one, 0);
        Check(single.OutputLines[0].Length > changed.Length,
            "resize invalidates clipped width"); checks++;
        single.Width = 120;
        single.ShowMonitor(one, 0);
        string beforeClear = single.OutputLines[0];
        single.ClearOutput();
        single.ShowMonitor(one, 0);
        Check(!ReferenceEquals(beforeClear, single.OutputLines[0]),
            "ClearOutput releases old snapshot"); checks++;
        string beforeCommand = single.OutputLines[0];
        single.PrintLine("command");
        single.ShowMonitor(one, 0);
        Check(!ReferenceEquals(beforeCommand, single.OutputLines[0]),
            "command output releases old snapshot"); checks++;
        one[0] = "short";
        single.ShowMonitor(one, 0);
        Check(ReferenceEquals(one[0], single.OutputLines[0]),
            "short source must be returned unchanged"); checks++;
        one[0] = source;
        single.ShowMonitor(one, 0);
        Check(single.OutputLines[0] == source.Substring(0, 10),
            "short-to-long snapshot transition"); checks++;
        single.ShowMonitor(Array.Empty<string>(), 0);
        Check(single.OutputLines.Count == 0, "empty snapshot"); checks++;
        single.ShowMonitor(one, 0);
        Check(single.OutputLines[0] == source.Substring(0, 10),
            "snapshot after empty"); checks++;

        Console.WriteLine("PASS: " + checks +
            " production htop monitor clip equivalence/cache/invalidation cases");
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }

    private static (double ms, long bytes) Measure(Action action, int repeats)
    {
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++) action();
        return ((Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency,
            GC.GetAllocatedBytesForCurrentThread() - allocated);
    }

    private static void Benchmark()
    {
        // 32 stable, clipped htop rows. Both variants call the actual
        // ShowMonitor and update the same production scrollbar.
        var lines = new string[32];
        for (int i = 0; i < lines.Length; i++)
            lines[i] = "CPU " + i + " | " + new string('X', 180);
        var old = new TerminalBox(0, 0, 120, 800)
        {
            DisableMonitorClipCacheForBenchmark = true
        };
        var current = new TerminalBox(0, 0, 120, 800);
        Action before = () =>
        {
            old.ShowMonitor(lines, 0);
            sink += old.OutputLines.Count;
        };
        Action after = () =>
        {
            current.ShowMonitor(lines, 0);
            sink += current.OutputLines.Count;
        };
        for (int i = 0; i < 100; i++) { before(); after(); }
        for (int i = 0; i < lines.Length; i++)
            Check(old.OutputLines[i] == current.OutputLines[i], "benchmark equality");
        var oldMs = new double[7];
        var newMs = new double[7];
        long oldBytes = 0, newBytes = 0;
        for (int sample = 0; sample < 7; sample++)
        {
            (double ms, long bytes) a, b;
            if ((sample & 1) == 0)
            {
                a = Measure(before, 4000);
                b = Measure(after, 4000);
            }
            else
            {
                b = Measure(after, 4000);
                a = Measure(before, 4000);
            }
            oldMs[sample] = a.ms;
            newMs[sample] = b.ms;
            oldBytes += a.bytes;
            newBytes += b.bytes;
        }
        Check(oldBytes > 0 && newBytes == 0,
            "stable htop clipping allocations " + oldBytes + " -> " + newBytes);
        Console.WriteLine("HOST A/B htop ShowMonitor 32 clipped rows x4000: previous=" +
            Median(oldMs).ToString("F3") + "ms cached=" +
            Median(newMs).ToString("F3") + "ms ratio=" +
            (Median(oldMs) / Median(newMs)).ToString("F2") +
            "x allocations/sample=" + (oldBytes / 7) + "B -> " +
            (newBytes / 7) + "B (7 alternating samples, .NET 10 Release; not guest FPS)");
        GC.KeepAlive(sink);
    }

    internal static void Run()
    {
        Verify();
        Benchmark();
    }
}
