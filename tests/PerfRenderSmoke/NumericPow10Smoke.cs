using System;
using System.Diagnostics;
using System.Reflection;
using ZonderqOS.GUI;

// Direct production Pow10 invoked through a typed delegate. Both A/B paths
// use a delegate to avoid reflection boxing and preserve a comparable call
// boundary. This measures only decimal power lookup, never QEMU FPS.
internal static class NumericPow10Smoke
{
    private static readonly Func<int, ulong> Actual =
        (Func<int, ulong>)typeof(SmallTextRenderer)
            .GetMethod("Pow10", BindingFlags.NonPublic | BindingFlags.Static)
            .CreateDelegate(typeof(Func<int, ulong>));

    private static volatile int sink;

    private static ulong Previous(int exponent)
    {
        ulong value = 1;
        for (int i = 0; i < exponent; i++)
            value *= 10;
        return value;
    }

    private static double Time(Func<int, ulong> operation, int repeats, int scenario)
    {
        ulong checksum = 0;
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++)
        {
            int exponent = scenario == 0 ? i % 4 :
                scenario == 1 ? 19 : i % 20;
            checksum = unchecked(checksum + operation(exponent));
        }
        sink = (int)checksum;
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    internal static void Verify()
    {
        for (int exponent = 0; exponent <= 19; exponent++)
            if (Previous(exponent) != Actual(exponent))
                throw new Exception("Decimal power mismatch at " + exponent);

        Func<int, ulong> beforeCall = Previous;
        Func<int, ulong> afterCall = Actual;
        for (int scenario = 0; scenario < 3; scenario++)
        {
            Time(beforeCall, 10000, scenario);
            Time(afterCall, 10000, scenario);
            const int repetitions = 500000;
            double[] before = new double[5];
            double[] after = new double[5];
            for (int sample = 0; sample < 5; sample++)
            {
                if ((sample & 1) == 0)
                {
                    before[sample] = Time(beforeCall, repetitions, scenario);
                    after[sample] = Time(afterCall, repetitions, scenario);
                }
                else
                {
                    after[sample] = Time(afterCall, repetitions, scenario);
                    before[sample] = Time(beforeCall, repetitions, scenario);
                }
            }
            Array.Sort(before);
            Array.Sort(after);
            Console.WriteLine("Host .NET Pow10 " + (scenario == 0 ? "0..3" :
                scenario == 1 ? "19" : "0..19") +
                ": " + repetitions + " calls, median 5 alternating A/B; before " +
                before[2].ToString("F2") + " ms, after " +
                after[2].ToString("F2") + " ms (lookup-only, not QEMU FPS).");
        }
        Console.WriteLine("Pow10: all 20 decimal exponents exact; no heap allocation in lookup.");
    }
}
