using System;
using System.Collections.Generic;
using System.Diagnostics;
using ZonderqOS.GUI;
using Cosmos.Kernel.System.Graphics;

// Exercises the actual TerminalBox clipping helper and its Render() callsite.
// Baseline is the old per-frame Substring; measurements are host-only.
internal static class RenderClipSmoke
{
    private static void Check(bool ok, string message)
    {
        if (!ok)
            throw new Exception("terminal render clip: " + message);
    }

    private static (double ms, long bytes) Measure(Action action, int iterations)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
            action();
        return ((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency,
            GC.GetAllocatedBytesForCurrentThread() - allocated);
    }

    private static double Median(double[] samples)
    {
        Array.Sort(samples);
        return samples[samples.Length / 2];
    }

    internal static void Run()
    {
        int checks = 0;
        var box = new TerminalBox(0, 0, 820, 160);
        string source = new string('A', 120);
        string first = box.ClipLineForRender(source, 0, 12);
        Check(first == source.Substring(0, 12), "first prefix"); checks++;
        Check(ReferenceEquals(first, box.ClipLineForRender(source, 0, 12)),
            "unchanged frame must reuse prefix"); checks++;
        string resized = box.ClipLineForRender(source, 0, 20);
        Check(resized == source.Substring(0, 20) && !ReferenceEquals(first, resized),
            "resized width must invalidate prefix"); checks++;
        string other = new string('B', 120);
        Check(box.ClipLineForRender(other, 0, 20) == other.Substring(0, 20),
            "changed source at same row"); checks++;
        Check(ReferenceEquals(other, box.ClipLineForRender(other, 0, 200)),
            "unclipped source identity"); checks++;
        string fresh = box.ClipLineForRender(other, 0, 20);
        Check(!ReferenceEquals(fresh, box.ClipLineForRender(source, 0, 20)),
            "short row clears stale clipped source"); checks++;
        box.ClearOutput();
        Check(!ReferenceEquals(first, box.ClipLineForRender(source, 0, 12)),
            "clear must release old clipped cache"); checks++;

        // Render a real, resized production widget through a recording Canvas.
        var draws = new List<string>();
        var canvas = new Canvas { OnDrawString = text => draws.Add(text) };
        var rendered = new TerminalBox(0, 0, 200, 160);
        rendered.OutputLines.Add(new string('X', 70));
        rendered.Width = 120; // (120 - 34) / 8 = 10 characters.
        rendered.Render(canvas);
        Check(draws.Contains(new string('X', 10)), "narrow Render() must clip"); checks++;
        string captured = draws.Find(text => text.Length == 10 && text[0] == 'X');
        draws.Clear();
        rendered.Render(canvas);
        Check(draws.Exists(text => ReferenceEquals(text, captured)),
            "repeated Render() must reuse same clipped instance"); checks++;
        rendered.OutputLines[0] = new string('Y', 70);
        draws.Clear();
        rendered.Render(canvas);
        Check(draws.Contains(new string('Y', 10)), "external list replacement"); checks++;
        rendered.Width = 160; // (160 - 34) / 8 = 15 characters.
        draws.Clear();
        rendered.Render(canvas);
        Check(draws.Contains(new string('Y', 15)), "window resize"); checks++;
        rendered.OutputLines.Clear();
        draws.Clear();
        rendered.Render(canvas);
        Check(!draws.Exists(text => text.Length > 0 && text[0] == 'Y'),
            "external list clear"); checks++;

        // Compare the same 32 visible lines and fixed-width prefix selection.
        // The destination arrays prevent the JIT from eliding substrings.
        const int rows = 32;
        const int chars = 18;
        var lines = new string[rows];
        var oldResults = new string[rows];
        var newResults = new string[rows];
        for (int i = 0; i < rows; i++)
            lines[i] = new string((char)('A' + i % 26), 180);
        var measured = new TerminalBox(0, 0, 820, 600);
        Action legacy = () =>
        {
            for (int i = 0; i < rows; i++)
                oldResults[i] = lines[i].Substring(0, chars);
        };
        Action cached = () =>
        {
            for (int i = 0; i < rows; i++)
                newResults[i] = measured.ClipLineForRender(lines[i], i, chars);
        };
        legacy();
        cached();
        for (int i = 0; i < rows; i++)
        {
            Check(oldResults[i] == newResults[i], "baseline equality " + i);
            checks++;
        }
        var oldTimes = new double[7];
        var newTimes = new double[7];
        long oldBytes = 0, newBytes = 0;
        for (int i = 0; i < 7; i++)
        {
            (double ms, long bytes) oldSample, newSample;
            if ((i & 1) == 0)
            {
                oldSample = Measure(legacy, 4000);
                newSample = Measure(cached, 4000);
            }
            else
            {
                newSample = Measure(cached, 4000);
                oldSample = Measure(legacy, 4000);
            }
            oldTimes[i] = oldSample.ms;
            newTimes[i] = newSample.ms;
            oldBytes += oldSample.bytes;
            newBytes += newSample.bytes;
        }
        Check(newBytes < oldBytes / 100, "repeated-frame allocation regression");
        Console.WriteLine("PASS: " + checks + " terminal render clip comparisons");
        Console.WriteLine("terminal clipped 32 rows x4000: old Substring " +
            Median(oldTimes).ToString("F3") + " ms, cached " +
            Median(newTimes).ToString("F3") + " ms; bytes/sample " +
            (oldBytes / 7) + " -> " + (newBytes / 7) +
            " (.NET 10 host only; no guest FPS claim)");
    }
}
