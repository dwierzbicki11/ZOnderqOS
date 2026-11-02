using System;
using System.Diagnostics;
using ZonderqOS.SystemCore;

// Independent sample-by-sample oracle for the delayed mixer fast paths.
// This file is compiled into the existing real C# AudioPciSmoke executable.
static class AudioDelayedMixerSmoke
{
    private static byte[] Reference(byte[] first, byte[] second, int firstGain,
        int secondGain, int delayFrames)
    {
        int delayBytes = delayFrames * 4;
        byte[] output = new byte[Math.Max(first.Length, delayBytes + second.Length)];
        for (int at = 0; at < output.Length; at += 2)
        {
            int a = at < first.Length
                ? (short)(first[at] | (first[at + 1] << 8)) : 0;
            int secondAt = at - delayBytes;
            int b = secondAt >= 0 && secondAt < second.Length
                ? (short)(second[secondAt] | (second[secondAt + 1] << 8)) : 0;
            int sum = a * firstGain / 100 + b * secondGain / 100;
            if (sum > short.MaxValue) sum = short.MaxValue;
            else if (sum < short.MinValue) sum = short.MinValue;
            output[at] = (byte)sum;
            output[at + 1] = (byte)(sum >> 8);
        }
        return output;
    }

    private static byte[] RandomPcm(Random random, int frames)
    {
        byte[] pcm = new byte[frames * 4];
        random.NextBytes(pcm);
        return pcm;
    }

    private static double MedianMilliseconds(Func<byte[]> action, int iterations)
    {
        double[] times = new double[5];
        GC.KeepAlive(action()); // JIT and allocation warmup
        for (int trial = 0; trial < times.Length; trial++)
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++)
                GC.KeepAlive(action());
            times[trial] = (Stopwatch.GetTimestamp() - start) * 1000.0 /
                Stopwatch.Frequency;
        }
        Array.Sort(times);
        return times[times.Length / 2];
    }

    public static void Run(Action<bool, string> expect)
    {
        Random random = new Random(0xA0D10);
        int[] gains = { 0, 1, 25, 50, 99, 100 };
        for (int test = 0; test < 1200; test++)
        {
            int firstFrames = random.Next(1, 45);
            int secondFrames = random.Next(1, 45);
            int delay = random.Next(0, 95);
            int firstGain = gains[random.Next(gains.Length)];
            int secondGain = gains[random.Next(gains.Length)];
            byte[] first = RandomPcm(random, firstFrames);
            byte[] second = RandomPcm(random, secondFrames);
            byte[] firstCopy = (byte[])first.Clone();
            byte[] secondCopy = (byte[])second.Clone();
            byte[] expected = Reference(first, second, firstGain, secondGain, delay);
            byte[] actual = AudioPcmMixer.MixStereo16WithDelay(
                first, second, firstGain, secondGain, delay);
            expect(actual.AsSpan().SequenceEqual(expected),
                "delayed mixer fast path matches independent PCM oracle #" + test);
            expect(first.AsSpan().SequenceEqual(firstCopy) &&
                second.AsSpan().SequenceEqual(secondCopy),
                "delayed mixer preserves both inputs #" + test);
            expect(!ReferenceEquals(actual, first) && !ReferenceEquals(actual, second),
                "delayed mixer owns independent output #" + test);
        }

        // The exact upper bound is valid; the first clip may remain longer
        // than a muted second clip without losing the scheduled zero tail.
        byte[] lastFrame = { 0x01, 0x80, 0xFF, 0x7F };
        byte[] boundary = AudioPcmMixer.MixStereo16WithDelay(
            lastFrame, lastFrame, 0, 100, 95999);
        expect(boundary.Length == 384000 &&
            boundary[383996] == 0x01 && boundary[383997] == 0x80 &&
            boundary[383998] == 0xFF && boundary[383999] == 0x7F,
            "muted first clip preserves last allowed delayed PCM frame");

        // Performance measurements are informative only: CI host jitter must
        // never turn a correctness test into a timing-based PASS/FAIL gate.
        byte[] a = RandomPcm(random, 6000);
        byte[] b = RandomPcm(random, 6000);
        const int iterations = 200;
        double baseline = MedianMilliseconds(
            () => Reference(a, b, 100, 100, 6000), iterations);
        double optimized = MedianMilliseconds(
            () => AudioPcmMixer.MixStereo16WithDelay(a, b, 100, 100, 6000),
            iterations);
        Console.WriteLine("[AUDIO-DELAY] host .NET disjoint unity-gain PCM: " +
            "reference=" + baseline.ToString("F3") + "ms optimized=" +
            optimized.ToString("F3") + "ms for " + iterations +
            " iterations (median of 5; NOT QEMU/HDA throughput)");

        // A short notification after a long silent pre-roll exercises the
        // partial-gain single-audible-source path (not just the copy path).
        byte[] muted = new byte[4];
        byte[] notification = RandomPcm(random, 2400);
        double baselinePreRoll = MedianMilliseconds(
            () => Reference(muted, notification, 0, 35, 50000), iterations);
        double optimizedPreRoll = MedianMilliseconds(
            () => AudioPcmMixer.MixStereo16WithDelay(
                muted, notification, 0, 35, 50000), iterations);
        Console.WriteLine("[AUDIO-DELAY] host .NET muted pre-roll + 35% notification: " +
            "reference=" + baselinePreRoll.ToString("F3") + "ms optimized=" +
            optimizedPreRoll.ToString("F3") + "ms for " + iterations +
            " iterations (median of 5; NOT QEMU/HDA throughput)");
    }
}
