using System;
using System.Diagnostics;
using ZonderqOS.GUI;

// LegacyOutput is the frozen pre-optimization terminal implementation from
// Program.cs. Both paths exercise actual PrintLine and scrollbar updates.
internal static class TrimmedSliceSmoke
{
    private static void Same(LegacyOutput old, TerminalBox current, string label)
    {
        if (old.OutputLines.Count != current.OutputLines.Count)
            throw new Exception(label + ": count mismatch");
        for (int i = 0; i < old.OutputLines.Count; i++)
            if (old.OutputLines[i] != current.OutputLines[i])
                throw new Exception(label + ": row mismatch " + i);
    }

    private static void Verify()
    {
        var old = new LegacyOutput(820, 520);
        var current = new TerminalBox(0, 0, 820, 520);
        int checks = 0;
        // All UTF-16 code units: the inlined char.IsWhiteSpace loop must
        // produce exactly the previous Substring(...).TrimEnd() output.
        for (int code = 0; code <= char.MaxValue; code++)
        {
            string line = "X" + (char)code;
            old.PrintLine(line);
            current.PrintLine(line);
            if (old.OutputLines.Count != current.OutputLines.Count ||
                old.OutputLines[old.OutputLines.Count - 1] !=
                    current.OutputLines[current.OutputLines.Count - 1])
                throw new Exception("Unicode trim mismatch U+" + code.ToString("X4"));
            checks++;
        }
        Same(old, current, "Unicode stream");
        var random = new Random(3095);
        const string alphabet = "abc XYZ \t\r\n\u00a0\u2002\u200b\u2028\u3000";
        for (int i = 0; i < 10000; i++)
        {
            var chars = new char[random.Next(0, 140)];
            for (int j = 0; j < chars.Length; j++)
                chars[j] = alphabet[random.Next(alphabet.Length)];
            string line = new string(chars);
            old.PrintLine(line);
            current.PrintLine(line);
            Same(old, current, "random Unicode wrapping");
            checks++;
        }
        Console.WriteLine("PASS: " + checks +
            " production PrintLine vs legacy Unicode whitespace and wrapping comparisons");
    }

    private static (double ms, long bytes) Measure(Action action, int count)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < count; i++)
            action();
        return ((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency,
            GC.GetAllocatedBytesForCurrentThread() - bytes);
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }

    private static void Benchmark(string label, string line, int iterations,
        bool expectReduction)
    {
        // Force wrapping so the final whitespace-terminated segment is
        // a genuine substring, not the original full input reference.
        var old = new LegacyOutput(320, 520);
        var current = new TerminalBox(0, 0, 320, 520);
        Action before = () => old.PrintLine(line);
        Action after = () => current.PrintLine(line);
        for (int i = 0; i < 100; i++) { before(); after(); }
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
        Same(old, current, "trim benchmark");
        if (expectReduction && newBytes >= oldBytes)
            throw new Exception("wrapped-row trim allocation regression");
        Console.WriteLine("HOST A/B " + label + ": legacy-PrintLine=" +
            Median(oldTimes).ToString("F3") + " ms optimized-PrintLine=" +
            Median(newTimes).ToString("F3") + " ms bytes/sample=" +
            (oldBytes / 7) + " -> " + (newBytes / 7) +
            " (7 alternating samples; production C# with fake scrollbar, not QEMU)");
    }

    internal static void Run()
    {
        Verify();
        const int iterations = 20000;
        Benchmark("terminal rows with trailing spaces x20000",
            "entry " + new string('x', 80) + "\t  ", iterations, true);
        Benchmark("terminal rows without trailing spaces x20000",
            "entry " + new string('x', 80), iterations, false);
    }
}
