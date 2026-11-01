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
        Console.WriteLine("[AUDIO-MIX-FASTPATH] PASS " + checks +
            " exact-sample/bounds/immutability checks");
    }
}
