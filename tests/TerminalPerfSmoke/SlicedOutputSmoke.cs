using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using ZonderqOS.GUI;

// This A/B reconstructs the immediately preceding production command parser.
// Both paths invoke the SAME production wrapping routine and publish the same
// final scrollbar update; only the intermediate per-line Substring differs.
internal static class SlicedOutputSmoke
{
    private static readonly BindingFlags PrivateInstance =
        BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo ScrollOffset =
        typeof(TerminalBox).GetField("scrollOffset", PrivateInstance);
    private static readonly FieldInfo UserScrolled =
        typeof(TerminalBox).GetField("userScrolled", PrivateInstance);
    private static readonly FieldInfo ScrollBarField =
        typeof(TerminalBox).GetField("scrollBar", PrivateInstance);
    private static readonly MethodInfo SliceMethod =
        typeof(TerminalBox).GetMethod("PrintLineSlice", PrivateInstance);
    private static readonly MethodInfo FinishMethod =
        typeof(TerminalBox).GetMethod("FinishOutputUpdate", PrivateInstance);

    private sealed class OldParser
    {
        private readonly TerminalBox box;
        private readonly Action<string, int, int, bool> append;
        private readonly Action finish;

        internal OldParser(TerminalBox terminal)
        {
            box = terminal;
            append = (Action<string, int, int, bool>)Delegate.CreateDelegate(
                typeof(Action<string, int, int, bool>), box, SliceMethod);
            finish = (Action)Delegate.CreateDelegate(typeof(Action), box, FinishMethod);
        }

        internal void Print(string output)
        {
            if (string.IsNullOrEmpty(output))
                return;
            int start = 0;
            bool deferred = false;
            bool scrolled = (bool)UserScrolled.GetValue(box);
            for (int i = 0; i <= output.Length; i++)
            {
                if (i < output.Length && output[i] != '\r' && output[i] != '\n')
                    continue;
                int length = i - start;
                if (length > 0)
                {
                    // The allocation removed by the production change.
                    string line = output.Substring(start, length);
                    if (line == "\u0001GUI_CLEAR\u0001")
                    {
                        box.ClearOutput();
                        scrolled = false;
                    }
                    else
                    {
                        append(line, 0, line.Length, true);
                        if (!scrolled)
                            deferred = true;
                    }
                }
                if (i < output.Length && output[i] == '\r' &&
                    i + 1 < output.Length && output[i + 1] == '\n')
                    i++;
                start = i + 1;
            }
            if (deferred && !scrolled)
                finish();
        }
    }

    private static void Same(TerminalBox oldBox, TerminalBox newBox)
    {
        if (oldBox.OutputLines.Count != newBox.OutputLines.Count)
            throw new Exception("sliced command count mismatch");
        for (int i = 0; i < oldBox.OutputLines.Count; i++)
            if (oldBox.OutputLines[i] != newBox.OutputLines[i])
                throw new Exception("sliced command row mismatch at " + i);
        var oldBar = (ScrollBar)ScrollBarField.GetValue(oldBox);
        var newBar = (ScrollBar)ScrollBarField.GetValue(newBox);
        if ((int)ScrollOffset.GetValue(oldBox) != (int)ScrollOffset.GetValue(newBox) ||
            (bool)UserScrolled.GetValue(oldBox) != (bool)UserScrolled.GetValue(newBox) ||
            oldBar.Value != newBar.Value || oldBar.MaxValue != newBar.MaxValue)
            throw new Exception("sliced command scrollbar mismatch");
    }

    private static void Verify()
    {
        if (SliceMethod == null || FinishMethod == null || ScrollOffset == null ||
            UserScrolled == null || ScrollBarField == null)
            throw new Exception("production terminal parser internals unavailable");
        int checks = 0;
        foreach (int width in new[] { 40, 80, 240, 820, 1920 })
        {
            var oldBox = new TerminalBox(0, 0, width, 160);
            var newBox = new TerminalBox(0, 0, width, 160);
            var old = new OldParser(oldBox);
            string[] cases = {
                "", "abc", "\r\n", "a\rb\nc\r\nd",
                "  a   b  ", "\u0001GUI_CLEAR\u0001",
                "before\n\u0001GUI_CLEAR\u0001\nafter",
                "x\r\n\u0001GUI_CLEAR\u0001\r\ny",
                new string('x', 5000), new string(' ', 5000),
                "Zażółć gęślą jaźń\r\n雪"
            };
            foreach (string input in cases)
            {
                old.Print(input);
                newBox.PrintCommandOutput(input);
                Same(oldBox, newBox);
                checks++;
            }
            for (int i = 0; i < 450; i++)
            {
                oldBox.PrintLine("history " + i);
                newBox.PrintLine("history " + i);
            }
            oldBox.HandleKey(new KeyEvent { Key = ConsoleKeyEx.PageUp });
            newBox.HandleKey(new KeyEvent { Key = ConsoleKeyEx.PageUp });
            var random = new Random(3095 + width);
            const string alphabet = "abc XYZ\t\r\ną 0123";
            for (int caseId = 0; caseId < 400; caseId++)
            {
                var chars = new char[random.Next(0, 240)];
                for (int i = 0; i < chars.Length; i++)
                    chars[i] = alphabet[random.Next(alphabet.Length)];
                string input = new string(chars);
                if (caseId % 23 == 0)
                    input += "\r\n\u0001GUI_CLEAR\u0001\r\nend";
                old.Print(input);
                newBox.PrintCommandOutput(input);
                Same(oldBox, newBox);
                checks++;
            }
        }
        Console.WriteLine("PASS: " + checks +
            " production C# direct-slice vs previous allocated-line parser states");
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
        long ticks = Stopwatch.GetTimestamp() - start;
        return (ticks * 1000.0 / Stopwatch.Frequency,
            GC.GetAllocatedBytesForCurrentThread() - allocated);
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }

    private static void Benchmark(string label, string output, int iterations)
    {
        var oldBox = new TerminalBox(0, 0, 820, 520);
        var newBox = new TerminalBox(0, 0, 820, 520);
        var old = new OldParser(oldBox);
        Action before = () => old.Print(output);
        Action after = () => newBox.PrintCommandOutput(output);
        for (int i = 0; i < 16; i++) { before(); after(); }
        double[] oldTimes = new double[7], newTimes = new double[7];
        long oldBytes = 0, newBytes = 0;
        for (int i = 0; i < 7; i++)
        {
            (double ms, long bytes) a, b;
            if ((i & 1) == 0)
            {
                a = Measure(before, iterations);
                b = Measure(after, iterations);
            }
            else
            {
                b = Measure(after, iterations);
                a = Measure(before, iterations);
            }
            oldTimes[i] = a.ms;
            newTimes[i] = b.ms;
            oldBytes += a.bytes;
            newBytes += b.bytes;
        }
        Same(oldBox, newBox);
        if (newBytes >= oldBytes)
            throw new Exception("command slicing did not reduce allocations: " +
                oldBytes + " -> " + newBytes);
        double oldMs = Median(oldTimes), newMs = Median(newTimes);
        Console.WriteLine("HOST A/B " + label + ": previous-line-copy=" +
            oldMs.ToString("F3") + " ms direct-slice=" + newMs.ToString("F3") +
            " ms ratio=" + (oldMs / newMs).ToString("F2") +
            "x bytes/sample=" + (oldBytes / 7) + " -> " + (newBytes / 7) +
            " (7 alternating samples, .NET 10 Release, not QEMU/FAT32)");
    }

    internal static void Run()
    {
        Verify();
        var rows = new string[400];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = "file " + i + " -- " + new string('x', 40);
        Benchmark("400 CRLF rows x200", string.Join("\r\n", rows) + "\r\n", 200);
        Benchmark("one long line x80", new string('x', 100000) + "\r\n", 80);
    }
}
