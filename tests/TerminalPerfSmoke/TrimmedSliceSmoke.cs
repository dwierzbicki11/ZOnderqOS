using System;
using System.Diagnostics;
using System.Reflection;
using ZonderqOS.GUI;

// The production helper is invoked directly: no reimplementation of its
// whitespace classification or allocation behavior is used as the oracle.
internal static class TrimmedSliceSmoke
{
    private static readonly Func<string, int, int, string> Trim =
        (Func<string, int, int, string>)Delegate.CreateDelegate(
            typeof(Func<string, int, int, string>),
            typeof(TerminalBox).GetMethod("SliceTrimmed",
                BindingFlags.Static | BindingFlags.NonPublic));

    private static void Check(string source, int start, int length)
    {
        string expected = source.Substring(start, length).TrimEnd();
        string actual = Trim(source, start, length);
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new Exception("trimmed slice mismatch start=" + start +
                " length=" + length);
    }

    private static void Verify()
    {
        int checks = 0;
        // .NET TrimEnd and Char.IsWhiteSpace must agree for all UTF-16
        // code units, including NBSP, Unicode separators and surrogates.
        for (int code = 0; code <= char.MaxValue; code++)
        {
            Check("X" + (char)code, 0, 2);
            checks++;
        }
        var random = new Random(3095);
        const string alphabet = "abc XYZ \t\r\n\u00a0\u2002\u200b\u2028\u3000";
        for (int i = 0; i < 10000; i++)
        {
            var chars = new char[random.Next(0, 100)];
            for (int j = 0; j < chars.Length; j++)
                chars[j] = alphabet[random.Next(alphabet.Length)];
            string source = "prefix" + new string(chars) + "suffix";
            int start = random.Next(0, source.Length + 1);
            int length = random.Next(0, source.Length - start + 1);
            Check(source, start, length);
            checks++;
        }
        Console.WriteLine("PASS: " + checks +
            " exact Unicode TrimEnd vs production C# SliceTrimmed comparisons");
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

    private static void Benchmark(string label, string input, int start, int length,
        int iterations, bool expectReduction)
    {
        Action oldAction = () => GC.KeepAlive(input.Substring(start, length).TrimEnd());
        Action newAction = () => GC.KeepAlive(Trim(input, start, length));
        for (int i = 0; i < 100; i++) { oldAction(); newAction(); }
        double[] oldTimes = new double[7], newTimes = new double[7];
        long oldBytes = 0, newBytes = 0;
        for (int i = 0; i < 7; i++)
        {
            (double ms, long bytes) a, b;
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
            oldTimes[i] = a.ms;
            newTimes[i] = b.ms;
            oldBytes += a.bytes;
            newBytes += b.bytes;
        }
        if (expectReduction && newBytes >= oldBytes)
            throw new Exception("trim optimization allocation regression");
        Console.WriteLine("HOST A/B " + label + ": Substring.TrimEnd=" +
            Median(oldTimes).ToString("F3") + " ms SliceTrimmed=" +
            Median(newTimes).ToString("F3") + " ms bytes/sample=" +
            (oldBytes / 7) + " -> " + (newBytes / 7) +
            " (7 alternating samples; host .NET, not QEMU)");
    }

    internal static void Run()
    {
        Verify();
        const int iterations = 20000;
        string withSpaces = "prefix" + new string('x', 80) + "\t  " + "suffix";
        Benchmark("wrapped row with trailing spaces x20000",
            withSpaces, 6, 84, iterations, true);
        string noSpaces = "prefix" + new string('x', 84) + "suffix";
        Benchmark("wrapped row without trailing spaces x20000",
            noSpaces, 6, 84, iterations, false);
    }
}
