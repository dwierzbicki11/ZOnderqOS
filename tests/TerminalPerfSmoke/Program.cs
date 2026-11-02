using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using ZonderqOS.GUI;

// Real production TerminalBox/ScrollBar/Widget are linked into this project.
// LegacyOutput is a frozen reference of the pre-batching output path.
internal sealed class LegacyOutput
{
    private const int MaxOutputLines = 400;
    private const int TrimBatch = 24;
    private const int HeaderHeight = 26;
    private const int ContentPadding = 10;
    private readonly ScrollBar scrollBar;
    private readonly int width;
    private readonly int height;
    private int scrollOffset;
    private bool userScrolled;
    internal readonly List<string> OutputLines = new List<string>();
    internal int ScrollOffset => scrollOffset;
    internal bool UserScrolled => userScrolled;
    internal int ScrollValue => scrollBar.Value;
    internal int MaxScrollValue => scrollBar.MaxValue;

    internal LegacyOutput(int width, int height)
    {
        this.width = width;
        this.height = height;
        scrollBar = new ScrollBar(width - 12, HeaderHeight + 4, 8,
            Math.Max(20, height - HeaderHeight - 8));
        scrollBar.ValueChanged = value => scrollOffset = value;
    }

    private int GetMaxChars() => Math.Max(1, (width - ContentPadding * 2 - 14) / 8);
    private int GetVisibleLines() =>
        Math.Max(1, (height - HeaderHeight - ContentPadding - 8) / 16);
    private int GetMaxScroll() =>
        Math.Max(0, OutputLines.Count - Math.Max(1, GetVisibleLines() - 1));

    private void UpdateScrollBar()
    {
        int visible = Math.Max(1, GetVisibleLines() - 1);
        scrollBar.X = Math.Max(1, width - 12);
        scrollBar.Y = HeaderHeight + 4;
        scrollBar.Width = 8;
        scrollBar.Height = Math.Max(20, height - HeaderHeight - 8);
        scrollBar.SetRange(OutputLines.Count + 1, visible);
        scrollBar.Value = Math.Max(0, Math.Min(scrollOffset, GetMaxScroll()));
    }

    private void ScrollToBottom()
    {
        scrollOffset = GetMaxScroll();
        userScrolled = false;
        UpdateScrollBar();
    }

    private void AddOutputLine(string line)
    {
        if (OutputLines.Count >= MaxOutputLines)
        {
            int remove = Math.Min(TrimBatch, OutputLines.Count);
            OutputLines.RemoveRange(0, remove);
            scrollOffset = Math.Max(0, scrollOffset - remove);
        }
        OutputLines.Add(line ?? "");
        if (!userScrolled)
            ScrollToBottom();
        else
            UpdateScrollBar();
    }

    internal void PrintLine(string line)
    {
        if (line == null)
            line = "";
        int maxChars = GetMaxChars();
        if (maxChars <= 0)
            return;
        if (line.Length == 0)
        {
            AddOutputLine("");
            return;
        }
        int position = 0;
        while (position < line.Length)
        {
            int remaining = line.Length - position;
            int take = Math.Min(maxChars, remaining);
            if (take < remaining)
            {
                int lastSpace = line.LastIndexOf(' ', position + take - 1, take);
                if (lastSpace >= position)
                    take = lastSpace - position;
            }
            if (take <= 0)
                take = Math.Min(maxChars, remaining);
            AddOutputLine(line.Substring(position, take).TrimEnd());
            position += take;
            while (position < line.Length && line[position] == ' ')
                position++;
        }
    }

    internal void PrintCommandOutput(string output)
    {
        if (string.IsNullOrEmpty(output))
            return;
        string normalized = output.Replace("\r\n", "\n").Replace("\r", "\n");
        string[] lines = normalized.Split('\n');
        foreach (string line in lines)
        {
            if (line == "\u0001GUI_CLEAR\u0001")
            {
                ClearOutput();
                continue;
            }
            if (line.Length > 0)
                PrintLine(line);
        }
    }

    internal void ClearOutput()
    {
        OutputLines.Clear();
        userScrolled = false;
        scrollOffset = 0;
        UpdateScrollBar();
    }

    internal void ShowMonitor(IReadOnlyList<string> lines, int firstLine)
    {
        ClearOutput();
        int widthChars = GetMaxChars();
        int count = Math.Max(1, GetVisibleLines() - 1);
        for (int index = Math.Max(0, firstLine); index < lines.Count && count > 0; index++, count--)
        {
            string line = lines[index];
            AddOutputLine(line.Length > widthChars ? line.Substring(0, widthChars) : line);
        }
    }

    internal void PageUp()
    {
        scrollOffset = Math.Max(0, scrollOffset - Math.Max(1, GetVisibleLines() - 2));
        userScrolled = true;
        UpdateScrollBar();
    }
}

internal static class Program
{
    private static readonly FieldInfo ScrollOffset = typeof(TerminalBox).GetField(
        "scrollOffset", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo UserScrolled = typeof(TerminalBox).GetField(
        "userScrolled", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo ScrollBarField = typeof(TerminalBox).GetField(
        "scrollBar", BindingFlags.Instance | BindingFlags.NonPublic);

    private static void Same(TerminalBox current, LegacyOutput old, string label)
    {
        if (current.OutputLines.Count != old.OutputLines.Count)
            throw new Exception(label + ": output count mismatch");
        for (int i = 0; i < current.OutputLines.Count; i++)
            if (current.OutputLines[i] != old.OutputLines[i])
                throw new Exception(label + ": output mismatch at " + i);
        int offset = (int)ScrollOffset.GetValue(current);
        bool scrolled = (bool)UserScrolled.GetValue(current);
        ScrollBar bar = (ScrollBar)ScrollBarField.GetValue(current);
        if (offset != old.ScrollOffset || scrolled != old.UserScrolled ||
            bar.Value != old.ScrollValue || bar.MaxValue != old.MaxScrollValue)
            throw new Exception(label + ": scrollbar state mismatch (" + offset +
                "/" + old.ScrollOffset + ", " + bar.Value + "/" + old.ScrollValue + ")");
    }

    // Previous per-row scrollbar policy. Uses the current production PrintLine,
    // isolating the cost of repeated SetRange/Value updates in command output.
    private static void PreviousCommandOutput(TerminalBox box, string output)
    {
        if (string.IsNullOrEmpty(output))
            return;
        int start = 0;
        for (int i = 0; i <= output.Length; i++)
        {
            if (i < output.Length && output[i] != '\r' && output[i] != '\n')
                continue;
            int length = i - start;
            if (length > 0)
            {
                string line = output.Substring(start, length);
                if (line == "\u0001GUI_CLEAR\u0001")
                    box.ClearOutput();
                else
                    box.PrintLine(line);
            }
            if (i < output.Length && output[i] == '\r' &&
                i + 1 < output.Length && output[i + 1] == '\n')
                i++;
            start = i + 1;
        }
    }

    private static void SameTerminals(TerminalBox current, TerminalBox previous, string label)
    {
        if (current.OutputLines.Count != previous.OutputLines.Count)
            throw new Exception(label + ": output count mismatch");
        for (int i = 0; i < current.OutputLines.Count; i++)
            if (current.OutputLines[i] != previous.OutputLines[i])
                throw new Exception(label + ": output mismatch at " + i);
        ScrollBar currentBar = (ScrollBar)ScrollBarField.GetValue(current);
        ScrollBar previousBar = (ScrollBar)ScrollBarField.GetValue(previous);
        if ((int)ScrollOffset.GetValue(current) != (int)ScrollOffset.GetValue(previous) ||
            (bool)UserScrolled.GetValue(current) != (bool)UserScrolled.GetValue(previous) ||
            currentBar.Value != previousBar.Value || currentBar.MaxValue != previousBar.MaxValue)
            throw new Exception(label + ": scroll state mismatch");
    }

    private static void VerifyCommandBatch()
    {
        const string marker = "\u0001GUI_CLEAR\u0001";
        string[] fixedCases = {
            "", "\r\n", "abc", "   ", "hello\rworld", "hello\nworld",
            "first\r\nsecond\rthird\n", marker, "x" + marker,
            marker + "x", "before\n" + marker + "\nafter",
            "\n" + marker + "\r\n" + marker + "\r\nend",
            new string(' ', 2048), new string('x', 5000),
            "Zażółć gęślą jaźń\r\n雪\t  "
        };
        int checks = 0;
        foreach (int width in new[] { 40, 80, 240, 820, 1920 })
        {
            var previous = new TerminalBox(0, 0, width, 160) { IsFocused = true };
            var current = new TerminalBox(0, 0, width, 160) { IsFocused = true };
            foreach (string output in fixedCases)
            {
                PreviousCommandOutput(previous, output);
                current.PrintCommandOutput(output);
                SameTerminals(current, previous, "fixed command batch");
                checks++;
            }
            // Exercise the trim + scroll path and deterministic mixed separators.
            for (int i = 0; i < 450; i++)
            {
                previous.PrintLine("history " + i);
                current.PrintLine("history " + i);
            }
            previous.HandleKey(new KeyEvent { Key = ConsoleKeyEx.PageUp });
            current.HandleKey(new KeyEvent { Key = ConsoleKeyEx.PageUp });
            var random = new Random(3095 + width);
            const string alphabet = "abc Z\t\r\ną 0123";
            for (int caseId = 0; caseId < 300; caseId++)
            {
                int length = random.Next(0, 240);
                var chars = new char[length];
                for (int i = 0; i < chars.Length; i++)
                    chars[i] = alphabet[random.Next(alphabet.Length)];
                string output = new string(chars);
                if (caseId % 17 == 0)
                    output += "\r\n" + marker + "\nnext";
                PreviousCommandOutput(previous, output);
                current.PrintCommandOutput(output);
                SameTerminals(current, previous, "random command batch");
                checks++;
            }
        }
        Console.WriteLine("PASS: " + checks +
            " batched command output vs previous production C# implementation");
    }

    private static void Verify()
    {
        if (ScrollOffset == null || UserScrolled == null || ScrollBarField == null)
            throw new Exception("Production terminal state not found");
        string[] monitor = new string[130];
        for (int i = 0; i < monitor.Length; i++)
            monitor[i] = "CPU " + i + " : " + new string((char)('A' + i % 26), i % 120);
        string[] print = {
            null, "", "a", "   ", "a b c", new string('X', 3500),
            "a  b  c  d  " + new string('z', 800), "line\nembedded\nbreak",
            new string(' ', 2048), "Zażółć gęślą jaźń"
        };
        int checks = 0;
        foreach (int width in new[] { 40, 80, 240, 820, 1920 })
        foreach (int height in new[] { 60, 160, 520, 1080 })
        {
            var current = new TerminalBox(0, 0, width, height) { IsFocused = true };
            var old = new LegacyOutput(width, height);
            foreach (string line in print)
            {
                current.PrintLine(line);
                old.PrintLine(line);
                Same(current, old, "PrintLine");
                checks++;
            }
            for (int i = 0; i < 450; i++)
            {
                current.PrintLine("history " + i);
                old.PrintLine("history " + i);
            }
            Same(current, old, "bounded history");
            current.HandleKey(new KeyEvent { Key = ConsoleKeyEx.PageUp });
            old.PageUp();
            Same(current, old, "PageUp");
            current.PrintLine(new string('q', 3000));
            old.PrintLine(new string('q', 3000));
            Same(current, old, "scrolled trim");
            checks += 3;
            foreach (int first in new[] { -100, 0, 5, 90, 129, 130, int.MaxValue })
            {
                current.ShowMonitor(monitor, first);
                old.ShowMonitor(monitor, first);
                Same(current, old, "ShowMonitor");
                checks++;
            }
            current.ShowMonitor(Array.Empty<string>(), 0);
            old.ShowMonitor(Array.Empty<string>(), 0);
            Same(current, old, "empty monitor");
            checks++;
            string[] outputs = {
                null, "", "\r\n", "abc", "abc\rdef", "abc\n\ndef",
                "abc\r\n\r\nxyz", "\r\r\n", "\nfirst\rsecond\n",
                "a\r\r\nb", "before\n\u0001GUI_CLEAR\u0001\nafter",
                "\u0001GUI_CLEAR\u0001", "\u0001GUI_CLEAR\u0001\r\n" +
                    new string('z', 5000) + "\r\nend",
                "Zażółć\r\n gęślą \n jaźń"
            };
            foreach (string output in outputs)
            {
                current.PrintCommandOutput(output);
                old.PrintCommandOutput(output);
                Same(current, old, "command stream");
                checks++;
            }
        }
        Console.WriteLine("PASS: " + checks +
            " production TerminalBox vs pre-batch reference state comparisons");
    }

    private static (double milliseconds, long bytes) Measure(Action action, int iterations)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        long before = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
            action();
        long elapsed = Stopwatch.GetTimestamp() - before;
        return (elapsed * 1000.0 / Stopwatch.Frequency,
            GC.GetAllocatedBytesForCurrentThread() - beforeBytes);
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }

    private static void Benchmark(string label, Action oldAction, Action newAction,
        int iterations, bool requireAllocationReduction = false)
    {
        for (int i = 0; i < 200; i++) { oldAction(); newAction(); }
        var oldTimes = new double[7];
        var newTimes = new double[7];
        long oldBytes = 0, newBytes = 0;
        for (int i = 0; i < 7; i++)
        {
            (double milliseconds, long bytes) a, b;
            if ((i & 1) == 0)
            {
                a = Measure(oldAction, iterations);
                b = Measure(newAction, iterations);
            }
            else
            {
                b = Measure(newAction, iterations);
                a = Measure(oldAction, iterations);
            }
            oldTimes[i] = a.milliseconds;
            newTimes[i] = b.milliseconds;
            oldBytes += a.bytes;
            newBytes += b.bytes;
        }
        double oldMs = Median(oldTimes), newMs = Median(newTimes);
        Console.WriteLine(label + ": legacy " + oldMs.ToString("F3") +
            " ms, batch " + newMs.ToString("F3") + " ms, ratio " +
            (oldMs / newMs).ToString("F2") + "x, bytes/sample " +
            (oldBytes / 7) + " -> " + (newBytes / 7) +
            " (.NET Release host; not kernel/QEMU)");
        // Timing is deliberately informational (shared CI runners are noisy).
        // Allocation bytes are deterministic after warmup for this workload.
        if (requireAllocationReduction && newBytes >= oldBytes)
            throw new Exception(label + ": streaming allocation regression");
    }

    private static int Main()
    {
        Verify();
        VerifyCommandBatch();
        SlicedOutputSmoke.Run();
        ScrollBarRefreshSmoke.Run();
        RenderClipSmoke.Run();
        var monitor = new string[128];
        for (int i = 0; i < monitor.Length; i++)
            monitor[i] = "CPU " + i + " " + new string('x', 64);
        var legacyMonitor = new LegacyOutput(820, 520);
        var currentMonitor = new TerminalBox(0, 0, 820, 520);
        Benchmark("htop visible snapshot x5000",
            () => legacyMonitor.ShowMonitor(monitor, 0),
            () => currentMonitor.ShowMonitor(monitor, 0), 5000);
        Same(currentMonitor, legacyMonitor, "benchmark monitor");

        string wrapped = new string('x', 2048);
        var legacyPrint = new LegacyOutput(820, 520);
        var currentPrint = new TerminalBox(0, 0, 820, 520);
        Benchmark("terminal long line x1500",
            () => legacyPrint.PrintLine(wrapped),
            () => currentPrint.PrintLine(wrapped), 1500);
        Same(currentPrint, legacyPrint, "benchmark print");

        var legacyCommand = new LegacyOutput(820, 520);
        var currentCommand = new TerminalBox(0, 0, 820, 520);
        var commandRows = new string[400];
        for (int i = 0; i < commandRows.Length; i++)
            commandRows[i] = "file " + i + " -- " + new string('x', 40);
        string commandOutput = string.Join("\r\n", commandRows) + "\r\n";
        Benchmark("command output CRLF 400 lines x200",
            () => legacyCommand.PrintCommandOutput(commandOutput),
            () => currentCommand.PrintCommandOutput(commandOutput), 200, true);
        Same(currentCommand, legacyCommand, "benchmark command");

        var previousSlice = new TerminalBox(0, 0, 820, 520);
        var currentSlice = new TerminalBox(0, 0, 820, 520);
        Benchmark("command scrollbar batch vs per-row CRLF 400 lines x200",
            () => PreviousCommandOutput(previousSlice, commandOutput),
            () => currentSlice.PrintCommandOutput(commandOutput), 200);
        SameTerminals(currentSlice, previousSlice, "benchmark command batch");
        Console.WriteLine("PASS: production C# terminal equivalence; timing is informational.");
        return 0;
    }
}