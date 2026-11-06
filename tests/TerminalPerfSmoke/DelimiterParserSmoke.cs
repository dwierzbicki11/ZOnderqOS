using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using ZonderqOS.GUI;

// End-to-end A/B: frozen immediately preceding scalar delimiter loop and
// production span parser both call the SAME production wrapping/scrollbar
// routines. No legacy Split, Substring, or per-line scrollbar confounder.
internal static class DelimiterParserSmoke
{
    private static readonly BindingFlags PrivateInstance =
        BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly MethodInfo Slice =
        typeof(TerminalBox).GetMethod("PrintLineSlice", PrivateInstance);
    private static readonly MethodInfo Finish =
        typeof(TerminalBox).GetMethod("FinishOutputUpdate", PrivateInstance);
    private static readonly FieldInfo Offset =
        typeof(TerminalBox).GetField("scrollOffset", PrivateInstance);
    private static readonly FieldInfo Scrolled =
        typeof(TerminalBox).GetField("userScrolled", PrivateInstance);
    private static readonly FieldInfo Bar =
        typeof(TerminalBox).GetField("scrollBar", PrivateInstance);
    private static long checksum;

    private sealed class ScalarParser
    {
        private readonly TerminalBox box;
        private readonly Action<string, int, int, bool> append;
        private readonly Action finish;

        internal ScalarParser(TerminalBox box)
        {
            this.box = box;
            append = (Action<string, int, int, bool>)Delegate.CreateDelegate(
                typeof(Action<string, int, int, bool>), box, Slice);
            finish = (Action)Delegate.CreateDelegate(typeof(Action), box, Finish);
        }

        internal void Print(string output)
        {
            if (string.IsNullOrEmpty(output))
                return;
            int start = 0;
            bool deferred = false;
            for (int i = 0; i <= output.Length; i++)
            {
                if (i < output.Length && output[i] != '\r' && output[i] != '\n')
                    continue;
                int length = i - start;
                if (length > 0)
                {
                    if (length == 11 &&
                        string.CompareOrdinal(output, start, "\u0001GUI_CLEAR\u0001", 0, length) == 0)
                        box.ClearOutput();
                    else
                    {
                        append(output, start, length, true);
                        deferred = true;
                    }
                }
                if (i < output.Length && output[i] == '\r' &&
                    i + 1 < output.Length && output[i + 1] == '\n')
                    i++;
                start = i + 1;
            }
            if (deferred)
                finish();
        }
    }

    private static void Same(TerminalBox a, TerminalBox b, string label)
    {
        if (a.OutputLines.Count != b.OutputLines.Count)
            throw new Exception(label + ": output count differs");
        for (int i = 0; i < a.OutputLines.Count; i++)
            if (a.OutputLines[i] != b.OutputLines[i])
                throw new Exception(label + ": output differs at " + i);
        var ab = (ScrollBar)Bar.GetValue(a);
        var bb = (ScrollBar)Bar.GetValue(b);
        if ((int)Offset.GetValue(a) != (int)Offset.GetValue(b) ||
            (bool)Scrolled.GetValue(a) != (bool)Scrolled.GetValue(b) ||
            ab.Value != bb.Value || ab.MaxValue != bb.MaxValue)
            throw new Exception(label + ": scrollbar state differs");
    }

    private static void Verify()
    {
        if (Slice == null || Finish == null || Offset == null || Scrolled == null || Bar == null)
            throw new Exception("production terminal parser reflection unavailable");
        int checks = 0;
        foreach (int width in new[] { 40, 80, 240, 820, 1920 })
        {
            var oldBox = new TerminalBox(0, 0, width, 520) { IsFocused = true };
            var newBox = new TerminalBox(0, 0, width, 520) { IsFocused = true };
            var old = new ScalarParser(oldBox);
            var random = new Random(3095 + width);
            const string alphabet = "abcXYZ \r\n\tą0123";
            for (int n = 0; n < 200; n++)
            {
                char[] chars = new char[random.Next(0, 512)];
                for (int i = 0; i < chars.Length; i++)
                    chars[i] = alphabet[random.Next(alphabet.Length)];
                string output = new string(chars);
                if (n % 23 == 0)
                    output += "\r\n\u0001GUI_CLEAR\u0001\r\nend";
                if (n % 11 == 0)
                {
                    oldBox.HandleKey(new KeyEvent { Key = ConsoleKeyEx.PageUp });
                    newBox.HandleKey(new KeyEvent { Key = ConsoleKeyEx.PageUp });
                }
                old.Print(output);
                newBox.PrintCommandOutput(output);
                Same(oldBox, newBox, "random " + n + " width " + width);
                checks++;
            }
        }
        Console.WriteLine("PASS: " + checks +
            " end-to-end production C# span vs frozen scalar command parser states");
    }

    private static (double ms, long bytes) Measure(Action action, int iterations)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
            action();
        return ((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency,
            GC.GetAllocatedBytesForCurrentThread() - beforeBytes);
    }

    private static double Median(double[] samples)
    {
        Array.Sort(samples);
        return samples[samples.Length / 2];
    }

    private static void Benchmark(string label, string output, int iterations)
    {
        var oldBox = new TerminalBox(0, 0, 820, 520);
        var newBox = new TerminalBox(0, 0, 820, 520);
        var old = new ScalarParser(oldBox);
        Action before = () => { old.Print(output); checksum += oldBox.OutputLines.Count; };
        Action after = () => { newBox.PrintCommandOutput(output); checksum += newBox.OutputLines.Count; };
        for (int i = 0; i < 8; i++) { before(); after(); }
        double[] a = new double[7], b = new double[7];
        long oldBytes = 0, newBytes = 0;
        for (int round = 0; round < 7; round++)
        {
            (double ms, long bytes) left, right;
            if ((round & 1) == 0)
            {
                left = Measure(before, iterations);
                right = Measure(after, iterations);
            }
            else
            {
                right = Measure(after, iterations);
                left = Measure(before, iterations);
            }
            a[round] = left.ms;
            b[round] = right.ms;
            oldBytes += left.bytes;
            newBytes += right.bytes;
        }
        Same(oldBox, newBox, label);
        if (oldBytes != newBytes)
            throw new Exception(label + ": unexpected allocation difference " +
                oldBytes + " -> " + newBytes);
        double oldMs = Median(a), newMs = Median(b);
        Console.WriteLine("HOST A/B scalar vs span parser " + label +
            ": " + oldMs.ToString("F3") + " -> " + newMs.ToString("F3") +
            " ms ratio=" + (oldMs / newMs).ToString("F2") +
            "x bytes/sample=" + (oldBytes / 7) + " -> " + (newBytes / 7) +
            " (same production wrapping and scrollbar; .NET Release host, not QEMU)");
    }

    internal static void Run()
    {
        Verify();
        string[] rows = new string[400];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = "file " + i + " -- " + new string('x', 40);
        Benchmark("400 CRLF rows x200", string.Join("\r\n", rows) + "\r\n", 200);
        Benchmark("one 100K-char row x80", new string('x', 100000) + "\r\n", 80);
        string[] wrapped = new string[120];
        for (int i = 0; i < wrapped.Length; i++)
            wrapped[i] = "entry " + i + " " + new string('x', 2048);
        Benchmark("120 wrapped rows x30", string.Join("\r\n", wrapped) + "\r\n", 30);
        GC.KeepAlive(checksum);
    }
}
