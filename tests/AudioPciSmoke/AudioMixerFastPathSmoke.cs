using System;
using System.Diagnostics;
using ZonderqOS.SystemCore;

// Deterministic sample-by-sample reference from the pre-optimization mixer.
// Timing is informational, not a flaky CI threshold or a QEMU claim.
internal static class AudioMixerFastPathSmoke
{
    private static int checks;
    private static int checksum;
    private static void Check(bool ok, string message)
    {
        checks++;
        if (!ok) throw new Exception("[AUDIO-MIX-FASTPATH] " + message);
    }
    private static byte[] LegacySingle(byte[] clip, int start, int gain, int tail)
    {
        int end = start * 4 + clip.Length;
        byte[] output = new byte[end + tail * 4];
        for (int at = 0; at < end; at += 2)
        {
            int sourceAt = at - start * 4;
            int sample = sourceAt < 0 || sourceAt >= clip.Length ? 0
                : (short)(clip[sourceAt] | (clip[sourceAt + 1] << 8));
            int scaled = sample * gain / 100;
            output[at] = (byte)scaled;
            output[at + 1] = (byte)(scaled >> 8);
        }
        return output;
    }
    // Independent pre-fast-path reference: sum signed PCM contributions,
    // then saturate once, including silent gaps and an optional silent tail.
    private static byte[] LegacyScheduled(
        byte[][] clips, int[] starts, int[] gains, int tailFrames)
    {
        int length = 0;
        for (int i = 0; i < clips.Length; i++)
            length = Math.Max(length, starts[i] * 4 + clips[i].Length);
        byte[] output = new byte[length + tailFrames * 4];
        for (int at = 0; at < length; at += 2)
        {
            int sum = 0;
            for (int i = 0; i < clips.Length; i++)
            {
                int sourceAt = at - starts[i] * 4;
                if (sourceAt < 0 || sourceAt >= clips[i].Length) continue;
                int sample = (short)(clips[i][sourceAt] | (clips[i][sourceAt + 1] << 8));
                sum += sample * gains[i] / 100;
            }
            sum = Math.Min(short.MaxValue, Math.Max(short.MinValue, sum));
            output[at] = (byte)sum;
            output[at + 1] = (byte)(sum >> 8);
        }
        return output;
    }

    private static void Same(byte[] a, byte[] b, string message)
        => Check(a.AsSpan().SequenceEqual(b), message);

    private static long Batch(byte[] pcm, bool optimized)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < 64; i++)
        {
            byte[] result = optimized
                ? AudioPcmMixer.MixScheduledStereo16(new[] { pcm }, new[] { 2400 }, new[] { 100 })
                : LegacySingle(pcm, 2400, 100, 0);
            checksum = unchecked(checksum * 16777619 ^ result[2400 * 4 + i * 4]);
        }
        return Stopwatch.GetTimestamp() - start;
    }

    private static long BatchDisjoint(
        byte[][] clips, int[] starts, int[] gains, bool optimized)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < 64; i++)
        {
            byte[] output = optimized
                ? AudioPcmMixer.MixScheduledStereo16(clips, starts, gains)
                : LegacyScheduled(clips, starts, gains, 0);
            checksum = unchecked(checksum * 16777619 ^ output[(i % 8) * 300 * 4]);
        }
        return Stopwatch.GetTimestamp() - start;
    }

    private static double MedianSinglePartial(
        byte[][] clips, int[] starts, int[] gains, bool optimized)
    {
        const int iterations = 24;
        double[] times = new double[5];
        for (int trial = 0; trial < times.Length; trial++)
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++)
            {
                byte[] output = optimized
                    ? AudioPcmMixer.MixScheduledStereo16(clips, starts, gains)
                    : LegacyScheduled(clips, starts, gains, 0);
                checksum = unchecked(checksum * 16777619 ^ output[starts[3] * 4 + 1]);
                GC.KeepAlive(output);
            }
            times[trial] = (Stopwatch.GetTimestamp() - start) * 1000.0 /
                Stopwatch.Frequency;
        }
        Array.Sort(times);
        return times[times.Length / 2];
    }

    public static void Run()
    {
        var random = new Random(3095);
        foreach (int frames in new[] { 1, 2, 5, 12000, 84000 })
        {
            byte[] clip = new byte[frames * 4];
            random.NextBytes(clip);
            byte[] original = (byte[])clip.Clone();
            foreach (int start in new[] { 0, 1, 2400 })
            {
                if (frames + start + 4096 > 96000) continue;
                foreach (int gain in new[] { 0, 25, 100 })
                {
                    Same(AudioPcmMixer.MixScheduledStereo16(
                        new[] { clip }, new[] { start }, new[] { gain }),
                        LegacySingle(clip, start, gain, 0), "scheduled PCM reference");
                    Same(AudioPcmMixer.MixScheduledStereo16WithFade(
                        new[] { clip }, new[] { start }, new[] { gain }, 0, 4096),
                        LegacySingle(clip, start, gain, 4096), "zero-fade HDA drain reference");
                    Same(AudioPcmMixer.MixScheduledStereo16WithIndividualFades(
                        new[] { clip }, new[] { start }, new[] { gain }, new[] { 0 }, 4096),
                        LegacySingle(clip, start, gain, 4096), "individual zero-fade reference");
                }
            }
            Same(clip, original, "input PCM must remain unchanged");
        }
        // Fast paths must own their output; never return a mutable alias.
        byte[] owned = new byte[48000];
        random.NextBytes(owned);
        byte[] originalOwned = (byte[])owned.Clone();
        byte[][] ownedClips = { owned };
        int[] ownedStarts = { 2400 };
        int[] ownedGains = { 100 };
        AudioPcmMixer.MixScheduledStereo16(ownedClips, ownedStarts, ownedGains);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        byte[] ownedResult = AudioPcmMixer.MixScheduledStereo16(
            ownedClips, ownedStarts, ownedGains);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Check(allocated >= ownedResult.Length && allocated < ownedResult.Length + 512,
            "unity fast path allocates only its output PCM buffer");
        Same(ownedResult, LegacySingle(owned, 2400, 100, 0),
            "unity fast path retains exact PCM and pre-roll");
        ownedResult[2400 * 4] ^= 0xff;
        Same(owned, originalOwned, "unity output may not alias source PCM");
        byte[] zeroFadeResult = AudioPcmMixer.MixScheduledStereo16WithFade(
            ownedClips, ownedStarts, ownedGains, 0, 4096);
        Same(zeroFadeResult, LegacySingle(owned, 2400, 100, 4096),
            "zero-fade fast path preserves the HDA drain");
        zeroFadeResult[2400 * 4] ^= 0xff;
        Same(owned, originalOwned, "zero-fade output may not alias source PCM");

        // Unsorted disjoint schedules, adjacent boundaries, overlaps and
        // non-unity gains must all retain the exact pre-optimization samples.
        for (int scenario = 0; scenario < 80; scenario++)
        {
            int count = 1 + scenario % 8;
            byte[][] clips = new byte[count][];
            byte[][] originals = new byte[count][];
            int[] starts = new int[count];
            int[] gains = new int[count];
            int[] noFades = new int[count];
            for (int i = 0; i < count; i++)
            {
                int frames = 1 + random.Next(1, 65);
                clips[i] = new byte[frames * 4];
                random.NextBytes(clips[i]);
                originals[i] = (byte[])clips[i].Clone();
                // Alternate disjoint (including exact adjacency) with
                // overlapping schedules. Reverse ordering in disjoint cases.
                starts[i] = scenario % 2 == 0
                    ? (count - 1 - i) * 80
                    : i * 12;
                gains[i] = scenario % 3 == 0 ? 100
                    : scenario % 3 == 1 ? 25 : (i % 2 == 0 ? 100 : 0);
            }
            int tail = scenario % 2 == 0 ? 4096 : 0;
            byte[] expected = LegacyScheduled(clips, starts, gains, tail);
            byte[] scheduled = AudioPcmMixer.MixScheduledStereo16(clips, starts, gains);
            Same(scheduled, LegacyScheduled(clips, starts, gains, 0),
                "random scheduled PCM matches independent reference");
            Same(AudioPcmMixer.MixScheduledStereo16WithFade(
                    clips, starts, gains, 0, tail),
                expected, "random zero-fade PCM matches reference and tail");
            Same(AudioPcmMixer.MixScheduledStereo16WithIndividualFades(
                    clips, starts, gains, noFades, tail),
                expected, "random per-clip zero-fade PCM matches reference and tail");
            for (int i = 0; i < count; i++)
                Same(clips[i], originals[i], "schedule cannot mutate source clips");
            scheduled[0] ^= 0xff;
            for (int i = 0; i < count; i++)
                Same(clips[i], originals[i], "schedule output must not alias input");
        }
        // Exactly one partially audible effect with muted, possibly
        // overlapping neighbors. Compare to an independent full-timeline
        // sample oracle, including out-of-order starts and a silent tail.
        for (int scenario = 0; scenario < 96; scenario++)
        {
            int count = 1 + scenario % 8;
            byte[][] clips = new byte[count][];
            byte[][] originals = new byte[count][];
            int[] starts = new int[count];
            int[] gains = new int[count];
            int[] fades = new int[count];
            int audible = scenario % count;
            for (int i = 0; i < count; i++)
            {
                clips[i] = new byte[(1 + random.Next(1, 65)) * 4];
                random.NextBytes(clips[i]);
                originals[i] = (byte[])clips[i].Clone();
                starts[i] = random.Next(0, 181);
                gains[i] = i == audible ? new[] { 1, 35, 99 }[scenario % 3] : 0;
                fades[i] = i == audible ? 0 : 50;
            }
            int tail = scenario % 2 == 0 ? 128 : 0;
            byte[] expected = LegacyScheduled(clips, starts, gains, tail);
            byte[] actual = AudioPcmMixer.MixScheduledStereo16(clips, starts, gains);
            Same(actual, LegacyScheduled(clips, starts, gains, 0),
                "single partial-gain schedule matches independent oracle");
            Same(AudioPcmMixer.MixScheduledStereo16WithFade(
                    clips, starts, gains, 0, tail),
                expected, "single partial-gain zero-fade schedule retains silent tail");
            Same(AudioPcmMixer.MixScheduledStereo16WithIndividualFades(
                    clips, starts, gains, fades, tail),
                expected, "muted fades do not alter the only audible clip");
            actual[starts[audible] * 4] ^= 0xFF;
            for (int i = 0; i < count; i++)
                Same(clips[i], originals[i],
                    "single partial-gain output never aliases or mutates a clip");
        }

        // Host-only A/B measurement: a 35%-gain effect after a long silent
        // pre-roll, with seven muted clips that still extend the timeline.
        // Correctness is checked first; timings are not CI pass criteria.
        byte[][] partialClips = new byte[8][];
        int[] partialStarts = new int[8];
        int[] partialGains = new int[8];
        for (int i = 0; i < 8; i++)
        {
            partialClips[i] = new byte[(i == 3 ? 2400 : 1) * 4];
            random.NextBytes(partialClips[i]);
            partialStarts[i] = i == 3 ? 50000 : 90000 - i * 100;
            partialGains[i] = i == 3 ? 35 : 0;
        }
        Same(AudioPcmMixer.MixScheduledStereo16(
                partialClips, partialStarts, partialGains),
            LegacyScheduled(partialClips, partialStarts, partialGains, 0),
            "long silent pre-roll and muted tail preserve exact PCM");
        MedianSinglePartial(partialClips, partialStarts, partialGains, false);
        MedianSinglePartial(partialClips, partialStarts, partialGains, true);
        double partialBefore = MedianSinglePartial(
            partialClips, partialStarts, partialGains, false);
        double partialAfter = MedianSinglePartial(
            partialClips, partialStarts, partialGains, true);
        Console.WriteLine("[AUDIO-MIX-SINGLE-PARTIAL] HOST A/B 5x24 median legacy=" +
            partialBefore.ToString("F3") + "ms optimized=" +
            partialAfter.ToString("F3") +
            "ms (host only; no QEMU/HDA timing proof)");

        // A boundary shared by two clips is disjoint, while a one-frame
        // overlap must still take the saturating arithmetic path.
        byte[][] edgeClips = {
            new byte[] { 0xff, 0x7f, 0x00, 0x80 },
            new byte[] { 0xff, 0x7f, 0x00, 0x80 }
        };
        int[] edgeGains = { 100, 100 };
        foreach (int[] edgeStarts in new[] {
            new[] { 1, 0 }, new[] { 0, 0 }, new[] { 0, 1 }
        })
        {
            Same(AudioPcmMixer.MixScheduledStereo16(edgeClips, edgeStarts, edgeGains),
                LegacyScheduled(edgeClips, edgeStarts, edgeGains, 0),
                "adjacent/overlapping clips preserve exact PCM");
        }

        // A zero-gain clip may overlap both audible clips without
        // contributing samples. Its fade cannot change that invariant.
        byte[][] mutedOverlapClips = {
            new byte[] { 0xff, 0x7f, 0x00, 0x80 },
            new byte[] { 0xff, 0x7f, 0x00, 0x80, 0x01, 0x00, 0xff, 0xff },
            new byte[] { 0x34, 0x12, 0x78, 0x56 }
        };
        int[] mutedOverlapStarts = { 0, 0, 1 };
        int[] mutedOverlapGains = { 100, 0, 100 };
        Same(AudioPcmMixer.MixScheduledStereo16(
                mutedOverlapClips, mutedOverlapStarts, mutedOverlapGains),
            LegacyScheduled(mutedOverlapClips, mutedOverlapStarts, mutedOverlapGains, 0),
            "muted overlap cannot change audible PCM samples");
        Same(AudioPcmMixer.MixScheduledStereo16WithIndividualFades(
                mutedOverlapClips, mutedOverlapStarts, mutedOverlapGains,
                new[] { 0, 50, 0 }, 4096),
            LegacyScheduled(mutedOverlapClips, mutedOverlapStarts, mutedOverlapGains, 4096),
            "muted overlapping fade preserves audible PCM and silent tail");

        byte[] a = { 0xff, 0x7f, 0x00, 0x80 };
        byte[] b = { 0x34, 0x12, 0x78, 0x56 };
        byte[] silent = AudioPcmMixer.MixScheduledStereo16(
            new[] { a, b }, new[] { 0, 3 }, new[] { 0, 0 });
        Check(silent.Length == 16 && Array.TrueForAll(silent, x => x == 0),
            "all-muted schedule preserves timeline");
        byte[] fadedSilent = AudioPcmMixer.MixScheduledStereo16WithIndividualFades(
            new[] { a, b }, new[] { 0, 3 }, new[] { 0, 50 }, new[] { 0, 50 }, 4);
        Check(fadedSilent.Length == 32 && Array.TrueForAll(fadedSilent, x => x == 0),
            "all-muted independent fades preserve silent tail");
        bool rejected = false;
        try { AudioPcmMixer.MixScheduledStereo16(
            new[] { new byte[3] }, new[] { 0 }, new[] { 0 }); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "muted path still validates frame alignment");
        rejected = false;
        try { AudioPcmMixer.MixScheduledStereo16WithFade(
            new[] { new byte[8] }, new[] { 96000 }, new[] { 0 }, 0, 0); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "muted path still validates output bound");

        byte[] benchmark = new byte[48000];
        random.NextBytes(benchmark);
        Batch(benchmark, false); Batch(benchmark, true);
        long[] before = new long[7], after = new long[7];
        for (int i = 0; i < 7; i++)
        {
            if (i % 2 == 0) { before[i] = Batch(benchmark, false); after[i] = Batch(benchmark, true); }
            else { after[i] = Batch(benchmark, true); before[i] = Batch(benchmark, false); }
        }
        Array.Sort(before); Array.Sort(after);
        Console.WriteLine("[AUDIO-MIX-FASTPATH] HOST A/B 7x64 median legacy=" +
            (before[3] * 1000.0 / Stopwatch.Frequency).ToString("F3") +
            "ms optimized=" + (after[3] * 1000.0 / Stopwatch.Frequency).ToString("F3") +
            "ms checksum=" + checksum + " (host only; no QEMU timing proof)");
        // Host-only A/B baseline: 8 independent effects in reverse order.
        // Both paths allocate an owned output; timing is informational.
        byte[][] benchClips = new byte[8][];
        int[] benchStarts = new int[8];
        int[] benchGains = new int[8];
        for (int i = 0; i < 8; i++)
        {
            benchClips[i] = new byte[250 * 4];
            random.NextBytes(benchClips[i]);
            benchStarts[i] = (7 - i) * 300;
            benchGains[i] = 100;
        }
        Same(AudioPcmMixer.MixScheduledStereo16(benchClips, benchStarts, benchGains),
            LegacyScheduled(benchClips, benchStarts, benchGains, 0),
            "disjoint eight-clip benchmark uses identical output");
        BatchDisjoint(benchClips, benchStarts, benchGains, false);
        BatchDisjoint(benchClips, benchStarts, benchGains, true);
        long[] disjointBefore = new long[7], disjointAfter = new long[7];
        for (int i = 0; i < 7; i++)
        {
            if (i % 2 == 0)
            {
                disjointBefore[i] = BatchDisjoint(benchClips, benchStarts, benchGains, false);
                disjointAfter[i] = BatchDisjoint(benchClips, benchStarts, benchGains, true);
            }
            else
            {
                disjointAfter[i] = BatchDisjoint(benchClips, benchStarts, benchGains, true);
                disjointBefore[i] = BatchDisjoint(benchClips, benchStarts, benchGains, false);
            }
        }
        Array.Sort(disjointBefore); Array.Sort(disjointAfter);
        Console.WriteLine("[AUDIO-MIX-DISJOINT] HOST A/B 7x64 median legacy=" +
            (disjointBefore[3] * 1000.0 / Stopwatch.Frequency).ToString("F3") +
            "ms optimized=" + (disjointAfter[3] * 1000.0 / Stopwatch.Frequency).ToString("F3") +
            "ms checksum=" + checksum + " (host only; no QEMU timing proof)");
        // Host A/B for overlapping muted effects: only the four audible
        // unity-gain clips are disjoint. Muted clips deliberately overlap
        // them and must not force the per-sample summation path.
        int[] mutedBenchStarts = (int[])benchStarts.Clone();
        int[] mutedBenchGains = (int[])benchGains.Clone();
        for (int i = 1; i < mutedBenchGains.Length; i += 2)
        {
            mutedBenchGains[i] = 0;
            mutedBenchStarts[i] = mutedBenchStarts[i - 1];
        }
        Same(AudioPcmMixer.MixScheduledStereo16(benchClips, mutedBenchStarts, mutedBenchGains),
            LegacyScheduled(benchClips, mutedBenchStarts, mutedBenchGains, 0),
            "muted-overlap benchmark must use bit-exact reference output");
        BatchDisjoint(benchClips, mutedBenchStarts, mutedBenchGains, false);
        BatchDisjoint(benchClips, mutedBenchStarts, mutedBenchGains, true);
        long[] mutedBefore = new long[7], mutedAfter = new long[7];
        for (int i = 0; i < 7; i++)
        {
            if (i % 2 == 0)
            {
                mutedBefore[i] = BatchDisjoint(benchClips, mutedBenchStarts, mutedBenchGains, false);
                mutedAfter[i] = BatchDisjoint(benchClips, mutedBenchStarts, mutedBenchGains, true);
            }
            else
            {
                mutedAfter[i] = BatchDisjoint(benchClips, mutedBenchStarts, mutedBenchGains, true);
                mutedBefore[i] = BatchDisjoint(benchClips, mutedBenchStarts, mutedBenchGains, false);
            }
        }
        Array.Sort(mutedBefore); Array.Sort(mutedAfter);
        Console.WriteLine("[AUDIO-MIX-MUTED-OVERLAP] HOST A/B 7x64 median legacy=" +
            (mutedBefore[3] * 1000.0 / Stopwatch.Frequency).ToString("F3") +
            "ms optimized=" + (mutedAfter[3] * 1000.0 / Stopwatch.Frequency).ToString("F3") +
            "ms checksum=" + checksum + " (host only; no QEMU timing proof)");
        Console.WriteLine("[AUDIO-MIX-FASTPATH] PASS " + checks +
            " exact-sample/bounds/immutability checks");
    }
}
