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

        string prompt = new string('P', 60);
        string promptClip = box.ClipPromptForRender(prompt, 12);
        Check(promptClip == prompt.Substring(0, 12) &&
            ReferenceEquals(promptClip, box.ClipPromptForRender(prompt, 12)),
            "prompt clipping should reuse instance"); checks++;
        Check(box.ClipPromptForRender(prompt, 16) == prompt.Substring(0, 16),
            "prompt resize"); checks++;
        string input = new string('T', 70);
        string inputClip = box.ClipInputForRender(input, 9);
        Check(inputClip == input.Substring(0, 9) &&
            ReferenceEquals(inputClip, box.ClipInputForRender(input, 9)),
            "input clipping should reuse instance"); checks++;
        box.ClearInput();
        Check(!ReferenceEquals(inputClip, box.ClipInputForRender(input, 9)),
            "clear input must discard cached text"); checks++;
        string beforeTrim = box.ClipLineForRender(source, 0, 12);
        for (int i = 0; i < 450; i++)
            box.PrintLine("history " + i);
        string afterTrim = box.ClipLineForRender(source, 0, 12);
        Check(!ReferenceEquals(beforeTrim, afterTrim),
            "bounded history trim must release clipped cache"); checks++;
        box.ShowMonitor(new[] { "monitor snapshot" }, 0);
        Check(!ReferenceEquals(afterTrim, box.ClipLineForRender(source, 0, 12)),
            "monitor snapshot must release clipped cache"); checks++;

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

        // Set a command while wide, then shrink: both prompt and input are
        // clipped at render time, and repeated frames reuse their prefixes.
        rendered.Width = 600;
        rendered.Prompt = "> ";
        rendered.SetInput(new string('T', 70));
        rendered.Width = 120;
        draws.Clear();
        rendered.Render(canvas);
        string firstInput = draws.Find(text => text == new string('T', 8));
        Check(firstInput != null, "resized input must be clipped"); checks++;
        draws.Clear();
        rendered.Render(canvas);
        Check(draws.Exists(text => ReferenceEquals(text, firstInput)),
            "resized input render should reuse prefix"); checks++;
        rendered.Prompt = new string('P', 50);
        draws.Clear();
        rendered.Render(canvas);
        string firstPrompt = draws.Find(text => text == new string('P', 10));
        Check(firstPrompt != null, "resized prompt must be clipped"); checks++;
        draws.Clear();
        rendered.Render(canvas);
        Check(draws.Exists(text => ReferenceEquals(text, firstPrompt)),
            "resized prompt render should reuse prefix"); checks++;

        // Normal unresized history must stay allocation-free across frames.
        var normal = new TerminalBox(0, 0, 820, 160);
        normal.OutputLines.Add("normal short line");
        var noCapture = new Canvas();
        normal.Render(noCapture);
        long normalBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            normal.Render(noCapture);
        long normalBytes = GC.GetAllocatedBytesForCurrentThread() - normalBefore;
        Check(normalBytes == 0, "normal repeated Render() allocated " + normalBytes + " bytes");
        checks++;

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

        // The former prompt/input Render() paths used the same Substring on
        // each heartbeat. Measure both against the production shared cache.
        string longPrompt = new string('P', 80);
        string longInput = new string('T', 90);
        string[] previousInputs = new string[2];
        string[] cachedInputs = new string[2];
        Action previousPromptInput = () =>
        {
            previousInputs[0] = longPrompt.Substring(0, 12);
            previousInputs[1] = longInput.Substring(0, 18);
        };
        Action currentPromptInput = () =>
        {
            cachedInputs[0] = measured.ClipPromptForRender(longPrompt, 12);
            cachedInputs[1] = measured.ClipInputForRender(longInput, 18);
        };
        previousPromptInput();
        currentPromptInput();
        Check(previousInputs[0] == cachedInputs[0] &&
            previousInputs[1] == cachedInputs[1], "prompt/input baseline equivalence"); checks++;
        var promptOldTimes = new double[7];
        var promptNewTimes = new double[7];
        long promptOldBytes = 0, promptNewBytes = 0;
        for (int i = 0; i < 7; i++)
        {
            (double ms, long bytes) a, b;
            if ((i & 1) == 0)
            {
                a = Measure(previousPromptInput, 100000);
                b = Measure(currentPromptInput, 100000);
            }
            else
            {
                b = Measure(currentPromptInput, 100000);
                a = Measure(previousPromptInput, 100000);
            }
            promptOldTimes[i] = a.ms;
            promptNewTimes[i] = b.ms;
            promptOldBytes += a.bytes;
            promptNewBytes += b.bytes;
        }
        Check(promptNewBytes < promptOldBytes / 100,
            "prompt/input repeated-frame allocation regression");
        Console.WriteLine("PASS: " + checks + " terminal render clip comparisons");
        Console.WriteLine("terminal clipped 32 rows x4000: old Substring " +
            Median(oldTimes).ToString("F3") + " ms, cached " +
            Median(newTimes).ToString("F3") + " ms; bytes/sample " +
            (oldBytes / 7) + " -> " + (newBytes / 7) +
            " (.NET 10 host only; no guest FPS claim)");
        Console.WriteLine("terminal clipped prompt+input x100000: old Substring " +
            Median(promptOldTimes).ToString("F3") + " ms, cached " +
            Median(promptNewTimes).ToString("F3") + " ms; bytes/sample " +
            (promptOldBytes / 7) + " -> " + (promptNewBytes / 7) +
            " (.NET 10 host only; no guest FPS claim)");
    }
}
