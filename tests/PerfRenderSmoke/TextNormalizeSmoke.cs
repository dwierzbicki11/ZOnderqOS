using System;
using System.Diagnostics;
using System.Reflection;
using ZonderqOS.GUI;

// Compare the real production private normalizer with the exact old switch.
// Delegates on both sides make call overhead comparable.
internal static class TextNormalizeSmoke
{
    private static readonly Func<char, char> Optimized =
        (Func<char, char>)typeof(SmallTextRenderer)
            .GetMethod("NormalizeGlyph", BindingFlags.Static | BindingFlags.NonPublic)
            .CreateDelegate(typeof(Func<char, char>));
    private static readonly Func<char, char> Baseline = LegacyNormalize;
    private static int sink;

    private static char LegacyNormalize(char ch)
    {
        switch (ch)
        {
            case 'ą': case 'Ą': return 'A';
            case 'ć': case 'Ć': return 'C';
            case 'ę': case 'Ę': return 'E';
            case 'ł': case 'Ł': return 'L';
            case 'ń': case 'Ń': return 'N';
            case 'ó': case 'Ó': return 'O';
            case 'ś': case 'Ś': return 'S';
            case 'ź': case 'Ź': case 'ż': case 'Ż': return 'Z';
            case '–': case '—': return '-';
            case '‘': case '’': return '\'';
            case '“': case '”': return '"';
            case '\u00A0': return ' ';
            default: return ch;
        }
    }

    private static double Time(Func<char, char> normalize, char[] text, int repeats)
    {
        int checksum = 0;
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++)
            checksum += normalize(text[i % text.Length]);
        long elapsed = Stopwatch.GetTimestamp() - start;
        sink = checksum;
        return elapsed * 1000.0 / Stopwatch.Frequency;
    }

    internal static void Verify()
    {
        for (int code = 0; code <= char.MaxValue; code++)
        {
            char ch = (char)code;
            if (Optimized(ch) != Baseline(ch))
                throw new Exception("Normalization mismatch U+" + code.ToString("X4"));
        }

        char[] text = ("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz" +
            "0123456789 /.-_:%()[]ąćęłńóśżź").ToCharArray();
        Time(Baseline, text, 10000);
        Time(Optimized, text, 10000);
        const int repeats = 5000000;
        double[] before = new double[5];
        double[] after = new double[5];
        for (int sample = 0; sample < 5; sample++)
        {
            if ((sample & 1) == 0)
            {
                before[sample] = Time(Baseline, text, repeats);
                after[sample] = Time(Optimized, text, repeats);
            }
            else
            {
                after[sample] = Time(Optimized, text, repeats);
                before[sample] = Time(Baseline, text, repeats);
            }
        }
        Array.Sort(before);
        Array.Sort(after);

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            Optimized(text[i % text.Length]);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        if (allocated != 0)
            throw new Exception("ASCII normalization allocated " + allocated + " B");

        Console.WriteLine("Glyph normalization: all 65536 UTF-16 code units identical, 0 B allocations.");
        Console.WriteLine("Host .NET mixed ASCII/Polish normalization: " + repeats +
            " calls, median 5 alternating A/B; legacy " + before[2].ToString("F2") +
            " ms, ASCII fast path " + after[2].ToString("F2") +
            " ms (not QEMU FPS), sink=" + sink + ".");
    }
}
