using System;
using System.Diagnostics;
using ZonderqOS.GUI;

// Exercises the production terminal's slice-trimming helper, not a copy.
// Baseline is the exact previous Substring(...).TrimEnd() expression.
internal static class TrimmedSliceSmoke
{
    private static long sink;

    private static string Previous(string text, int start, int length) =>
        text.Substring(start, length).TrimEnd();

    private static void Verify()
    {
        var random = new Random(3095);
        char[] alphabet = {
            'a', 'Z', ' ', '\t', '\r', '\n', '\u00a0',
            '\u2003', '\u2028', '\u3000', '\0',
            '\u0105', '\u6F22', '\uD83D', '\uDE00'
        };
        int checks = 0;
        for (int sample = 0; sample < 5000; sample++)
        {
            int count = random.Next(0, 128);
            var chars = new char[count];
            for (int i = 0; i < count; i++)
                chars[i] = alphabet[random.Next(alphabet.Length)];
            string text = new string(chars);
            int start = random.Next(count + 1);
            int length = random.Next(count - start + 1);
            string expected = Previous(text, start, length);
            string actual = TerminalBox.SliceTrimmedForOutput(text, start, length);
            if (actual != expected)
                throw new Exception("terminal slice trim mismatch at sample " + sample);
            checks++;
        }

        // Exercise both public terminal entry points, not just the helper:
        // a complete printable Unicode line must retain its source string.
        string unicode = "Zażółć gęślą jaźń \u6F22\uD83D\uDE00";
        var direct = new TerminalBox(0, 0, 820, 520);
        direct.PrintLine(unicode);
        if (direct.OutputLines.Count != 1 ||
            !object.ReferenceEquals(direct.OutputLines[0], unicode))
            throw new Exception("PrintLine copied a complete Unicode line");
        checks++;
        direct.PrintLine(unicode + "\u00A0");
        if (direct.OutputLines[1] != unicode)
            throw new Exception("PrintLine failed to trim trailing Unicode whitespace");
        checks++;
        var command = new TerminalBox(0, 0, 820, 520);
        command.PrintCommandOutput(unicode);
        if (command.OutputLines.Count != 1 ||
            !object.ReferenceEquals(command.OutputLines[0], unicode))
            throw new Exception("PrintCommandOutput copied a complete Unicode line");
        checks++;

        string blank = "pre" + new string(' ', 40) + "post";
        for (int i = 0; i < 100; i++)
            sink += TerminalBox.SliceTrimmedForOutput(blank, 3, 40).Length;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1024; i++)
            sink += TerminalBox.SliceTrimmedForOutput(blank, 3, 40).Length;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (allocated != 0)
            throw new Exception("fully blank terminal slices allocated " + allocated + " B");
        Console.WriteLine("PASS: " + checks +
            " exact Unicode slice/TrimEnd equivalence cases; blank-slice allocations=0 B");
    }

    private static (double ms, long bytes) Measure(Func<long> action, int repeats)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++)
            sink ^= action();
        return ((Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency,
            GC.GetAllocatedBytesForCurrentThread() - allocated);
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }

    private static void Benchmark()
    {
        const int count = 128;
        var inputs = new string[count];
        for (int i = 0; i < count; i++)
            inputs[i] = "pre" + new string('x', 64 + i % 20) +
                new string(' ', 12 + i % 8) + "post";

        Func<long> previous = () =>
        {
            long checksum = 0;
            for (int i = 0; i < 1024; i++)
            {
                string text = inputs[i % count];
                string value = Previous(text, 3, text.Length - 7);
                checksum += value.Length + value[0] + value[value.Length - 1];
            }
            return checksum;
        };
        Func<long> optimized = () =>
        {
            long checksum = 0;
            for (int i = 0; i < 1024; i++)
            {
                string text = inputs[i % count];
                string value = TerminalBox.SliceTrimmedForOutput(text, 3, text.Length - 7);
                checksum += value.Length + value[0] + value[value.Length - 1];
            }
            return checksum;
        };

        if (previous() != optimized())
            throw new Exception("terminal trim A/B checksum mismatch");
        for (int i = 0; i < 30; i++) { sink ^= previous(); sink ^= optimized(); }

        var before = new double[7];
        var after = new double[7];
        long oldBytes = 0, newBytes = 0;
        for (int sample = 0; sample < 7; sample++)
        {
            (double ms, long bytes) a, b;
            if ((sample & 1) == 0)
            {
                a = Measure(previous, 100);
                b = Measure(optimized, 100);
            }
            else
            {
                b = Measure(optimized, 100);
                a = Measure(previous, 100);
            }
            before[sample] = a.ms;
            after[sample] = b.ms;
            oldBytes += a.bytes;
            newBytes += b.bytes;
        }

        if (newBytes >= oldBytes)
            throw new Exception("trimmed terminal slices did not reduce allocations: " +
                oldBytes + " -> " + newBytes);
        double aMs = Median(before), bMs = Median(after);
        Console.WriteLine("HOST A/B terminal trailing-whitespace slices, 1024 x 100: old=" +
            aMs.ToString("F3") + "ms new=" + bMs.ToString("F3") +
            "ms ratio=" + (aMs / bMs).ToString("F2") +
            "x allocations/sample=" + (oldBytes / 7) + " -> " + (newBytes / 7) +
            " B (7 alternating .NET Release samples; not kernel CPU)");
    }

    // Guard the common no-whitespace case against hidden extra allocations.
    // Timing is diagnostic only: a shared CI runner cannot enforce speed ratios.
    private static void BenchmarkUnpadded()
    {
        var inputs = new string[128];
        for (int i = 0; i < inputs.Length; i++)
            inputs[i] = "pre" + new string('x', 64 + i % 20) + "post";
        Func<long> previous = () =>
        {
            long checksum = 0;
            for (int i = 0; i < 1024; i++)
            {
                string text = inputs[i % inputs.Length];
                string value = Previous(text, 3, text.Length - 7);
                checksum += value.Length + value[0] + value[value.Length - 1];
            }
            return checksum;
        };
        Func<long> optimized = () =>
        {
            long checksum = 0;
            for (int i = 0; i < 1024; i++)
            {
                string text = inputs[i % inputs.Length];
                string value = TerminalBox.SliceTrimmedForOutput(text, 3, text.Length - 7);
                checksum += value.Length + value[0] + value[value.Length - 1];
            }
            return checksum;
        };
        if (previous() != optimized())
            throw new Exception("unpadded terminal A/B checksum mismatch");
        for (int i = 0; i < 30; i++) { sink ^= previous(); sink ^= optimized(); }
        double[] before = new double[7], after = new double[7];
        long oldBytes = 0, newBytes = 0;
        for (int sample = 0; sample < 7; sample++)
        {
            (double ms, long bytes) a, b;
            if ((sample & 1) == 0)
            {
                a = Measure(previous, 100);
                b = Measure(optimized, 100);
            }
            else
            {
                b = Measure(optimized, 100);
                a = Measure(previous, 100);
            }
            before[sample] = a.ms;
            after[sample] = b.ms;
            oldBytes += a.bytes;
            newBytes += b.bytes;
        }
        if (newBytes > oldBytes)
            throw new Exception("unpadded terminal slices allocated more: " +
                oldBytes + " -> " + newBytes);
        double aMs = Median(before), bMs = Median(after);
        Console.WriteLine("HOST A/B terminal unpadded slices, 1024 x 100: old=" +
            aMs.ToString("F3") + "ms new=" + bMs.ToString("F3") +
            "ms ratio=" + (aMs / bMs).ToString("F2") +
            "x allocations/sample=" + (oldBytes / 7) + " -> " + (newBytes / 7) +
            " B (7 alternating .NET Release samples; not kernel CPU)");
    }

    // Compare the previous span TrimEnd().ToString() path with the
    // production fast path on complete Unicode command lines. Both call
    // the exact production method; the host-only switch is the only change.
    private static void BenchmarkUnicodeUnpadded()
    {
        var inputs = new string[128];
        for (int i = 0; i < inputs.Length; i++)
            inputs[i] = "plik-" + i.ToString("D3") +
                new string('\u0105', 48 + i % 20) + "\u6F22\uD83D\uDE00";

        Func<long> previous = () =>
        {
            TerminalBox.DisableUnicodeTrimFastPathForBenchmark = true;
            long checksum = 0;
            for (int i = 0; i < 1024; i++)
            {
                string source = inputs[i % inputs.Length];
                string value = TerminalBox.SliceTrimmedForOutput(source, 0, source.Length);
                checksum += value.Length + value[0] + value[value.Length - 1];
            }
            return checksum;
        };
        Func<long> optimized = () =>
        {
            TerminalBox.DisableUnicodeTrimFastPathForBenchmark = false;
            long checksum = 0;
            for (int i = 0; i < 1024; i++)
            {
                string source = inputs[i % inputs.Length];
                string value = TerminalBox.SliceTrimmedForOutput(source, 0, source.Length);
                if (!object.ReferenceEquals(source, value))
                    throw new Exception("Unicode full-line slice allocated a copy");
                checksum += value.Length + value[0] + value[value.Length - 1];
            }
            return checksum;
        };

        try
        {
            if (previous() != optimized())
                throw new Exception("Unicode terminal A/B checksum mismatch");
            for (int i = 0; i < 30; i++) { sink ^= previous(); sink ^= optimized(); }
            var before = new double[7];
            var after = new double[7];
            long oldBytes = 0, newBytes = 0;
            for (int sample = 0; sample < 7; sample++)
            {
                (double ms, long bytes) a, b;
                if ((sample & 1) == 0)
                {
                    a = Measure(previous, 100);
                    b = Measure(optimized, 100);
                }
                else
                {
                    b = Measure(optimized, 100);
                    a = Measure(previous, 100);
                }
                before[sample] = a.ms;
                after[sample] = b.ms;
                oldBytes += a.bytes;
                newBytes += b.bytes;
            }
            if (oldBytes <= 0 || newBytes != 0)
                throw new Exception("Unicode full-line allocation regression: " +
                    oldBytes + " -> " + newBytes);
            double aMs = Median(before), bMs = Median(after);
            Console.WriteLine("HOST A/B Unicode full-line slices 1024 x 100: old=" +
                aMs.ToString("F3") + "ms new=" + bMs.ToString("F3") +
                "ms ratio=" + (aMs / bMs).ToString("F2") +
                "x allocations/sample=" + (oldBytes / 7) + " -> " +
                (newBytes / 7) + " B (7 alternating .NET Release samples; not guest CPU)");
        }
        finally
        {
            TerminalBox.DisableUnicodeTrimFastPathForBenchmark = false;
        }
    }

    internal static void Run()
    {
        Verify();
        Benchmark();
        BenchmarkUnpadded();
        BenchmarkUnicodeUnpadded();
    }
}
