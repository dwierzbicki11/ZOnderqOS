using System;
using System.Diagnostics;
using System.Reflection;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;

// A/B uses the previous production UpdateScrollBar implementation, not a
// no-op benchmark. Both paths update real ScrollBar objects from the same
// production TerminalBox dimensions and output history.
internal static class ScrollBarRefreshSmoke
{
    private static readonly BindingFlags PrivateInstance =
        BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo ScrollOffset =
        typeof(TerminalBox).GetField("scrollOffset", PrivateInstance);
    private static readonly FieldInfo ScrollBarField =
        typeof(TerminalBox).GetField("scrollBar", PrivateInstance);
    private static readonly MethodInfo UpdateMethod =
        typeof(TerminalBox).GetMethod("UpdateScrollBar", PrivateInstance);

    private static ScrollBar GetBar(TerminalBox box) =>
        (ScrollBar)ScrollBarField.GetValue(box);

    private static void LegacyUpdate(TerminalBox box, ScrollBar bar, int offset)
    {
        // Frozen pre-cache UpdateScrollBar logic, with Font.Width=8,
        // Font.Height=16 and the same fixed production padding constants.
        int visibleLines = Math.Max(1, (box.Height - 26 - 10 - 8) / 16);
        int visible = Math.Max(1, visibleLines - 1);
        bar.X = box.X + Math.Max(1, box.Width - 12);
        bar.Y = box.Y + 26 + 4;
        bar.Width = 8;
        bar.Height = Math.Max(20, box.Height - 26 - 8);
        bar.SetRange(box.OutputLines.Count + 1, visible);
        int maxScroll = Math.Max(0, box.OutputLines.Count - visible);
        bar.Value = Math.Max(0, Math.Min(offset, maxScroll));
    }

    private static void Check(TerminalBox box, ScrollBar oldBar, string label)
    {
        // Render also exercises the repeated idle-frame update call.
        box.Render(new Canvas());
        int offset = (int)ScrollOffset.GetValue(box);
        LegacyUpdate(box, oldBar, offset);
        ScrollBar actual = GetBar(box);
        if (actual.X != oldBar.X || actual.Y != oldBar.Y ||
            actual.Width != oldBar.Width || actual.Height != oldBar.Height ||
            actual.Value != oldBar.Value || actual.MaxValue != oldBar.MaxValue)
            throw new Exception(label + ": cached scrollbar diverged from pre-cache layout");
    }

    private static void Verify()
    {
        if (ScrollOffset == null || ScrollBarField == null || UpdateMethod == null)
            throw new Exception("production terminal scrollbar internals unavailable");
        int checks = 0;
        foreach (int width in new[] { 40, 80, 800, 1366, 1920 })
        foreach (int height in new[] { 48, 240, 600, 768, 1080 })
        {
            var box = new TerminalBox(3, 5, width, height);
            var oldBar = new ScrollBar(0, 0, 8, 20);
            Check(box, oldBar, "initial");
            checks++;
            for (int i = 0; i < 430; i++)
                box.PrintLine("history " + i);
            Check(box, oldBar, "history trim");
            checks++;
            for (int repeat = 0; repeat < 20; repeat++)
            {
                Check(box, oldBar, "unchanged idle frame");
                checks++;
            }
            box.HandleKey(new KeyEvent { Key = ConsoleKeyEx.PageUp });
            Check(box, oldBar, "page up");
            checks++;
            // The real ScrollBar.Value setter invokes the production callback,
            // just as thumb dragging and page clicks do.
            ScrollBar liveBar = GetBar(box);
            liveBar.Value = Math.Min(5, liveBar.MaxValue);
            Check(box, oldBar, "scrollbar ValueChanged callback");
            checks++;
            box.HandleMouse(liveBar.X + 2, liveBar.Y + liveBar.Height - 2, true, false);
            Check(box, oldBar, "scrollbar track click");
            checks++;
            box.HandleMouse(liveBar.X + 2, liveBar.Y + liveBar.Height - 2, false, true);
            Check(box, oldBar, "scrollbar release");
            checks++;
            box.HandleKey(new KeyEvent { Key = ConsoleKeyEx.PageDown });
            Check(box, oldBar, "page down");
            checks++;
            box.HandleKey(new KeyEvent { Key = ConsoleKeyEx.End });
            Check(box, oldBar, "end");
            checks++;
            box.X = -5;
            box.Y = 18;
            box.Width = Math.Max(30, width / 2);
            box.Height = Math.Max(30, height / 2);
            Check(box, oldBar, "move and resize");
            checks++;
            box.OutputLines.RemoveRange(0, 200);
            Check(box, oldBar, "external history shrink");
            checks++;
            box.OutputLines.Add("new row");
            Check(box, oldBar, "external history growth");
            checks++;
            box.ClearOutput();
            Check(box, oldBar, "clear");
            checks++;
            box.ShowMonitor(new[] { "cpu", "memory", "threads" }, 0);
            Check(box, oldBar, "monitor");
            checks++;
            box.PrintCommandOutput("a\\r\\nb\\n" + new string('x', 120));
            Check(box, oldBar, "command output");
            checks++;
        }
        Console.WriteLine("PASS: " + checks +
            " cached scrollbar geometry/range comparisons with pre-cache C#");
    }

    private static (double ms, long bytes) Measure(Action action, int iterations)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
            action();
        return ((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency,
            GC.GetAllocatedBytesForCurrentThread() - bytes);
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }

    private static void Benchmark()
    {
        var box = new TerminalBox(12, 24, 800, 600);
        for (int i = 0; i < 160; i++)
            box.OutputLines.Add("row " + i);
        var oldBar = new ScrollBar(0, 0, 8, 20);
        var cached = (Action)Delegate.CreateDelegate(typeof(Action), box, UpdateMethod);
        // Both callbacks have a single delegate invocation; reflection is
        // outside the measured loop. The terminal is at scroll offset zero.
        Action legacy = () => LegacyUpdate(box, oldBar, 0);
        legacy();
        cached();
        for (int i = 0; i < 2000; i++) { legacy(); cached(); }

        var before = new double[7];
        var after = new double[7];
        long oldBytes = 0, newBytes = 0;
        const int iterations = 250000;
        for (int i = 0; i < 7; i++)
        {
            (double ms, long bytes) a, b;
            if ((i & 1) == 0)
            {
                a = Measure(legacy, iterations);
                b = Measure(cached, iterations);
            }
            else
            {
                b = Measure(cached, iterations);
                a = Measure(legacy, iterations);
            }
            before[i] = a.ms;
            after[i] = b.ms;
            oldBytes += a.bytes;
            newBytes += b.bytes;
        }
        if (newBytes > oldBytes)
            throw new Exception("cached scrollbar introduced allocation regression");
        double oldMs = Median(before), newMs = Median(after);
        Console.WriteLine("HOST A/B 250000 unchanged terminal scrollbar updates: " +
            "previous=" + oldMs.ToString("F3") + " ms cached=" +
            newMs.ToString("F3") + " ms ratio=" +
            (oldMs / newMs).ToString("F2") + "x bytes/sample=" +
            (oldBytes / 7) + " -> " + (newBytes / 7) +
            " (7 alternating samples, .NET 10 Release; not QEMU idle CPU)");
    }

    internal static void Run()
    {
        Verify();
        Benchmark();
    }
}
