using System;
using System.Collections.Generic;
using ZonderqOS.SystemCore;

internal static class AudioProbeFeederSmoke
{
    private static int checks;

    private static void Check(bool ok, string name)
    {
        checks++;
        if (!ok) throw new Exception("[AUDIO-FEEDER-HOST] FAIL " + name);
    }

    public static void Run()
    {
        byte[] pcm = new byte[4800 * 4];
        for (int i = 0; i < pcm.Length; i++)
            pcm[i] = (byte)((i * 17 + 3) & 255);
        byte[] original = (byte[])pcm.Clone();
        var written = new List<byte>();
        int pending = 0;
        int ticks = 0;
        int Write(byte[] source, int offset, int count)
        {
            int accepted = Math.Min(count, 4096 - pending);
            accepted -= accepted % 4;
            for (int i = 0; i < accepted; i++)
                written.Add(source[offset + i]);
            pending += accepted;
            return accepted;
        }
        void Wait(uint milliseconds)
        {
            Check(milliseconds == 1, "one-millisecond wait");
            ticks++;
            pending = Math.Max(0, pending - 1024);
        }
        bool ok = AudioProbeRingFeeder.Feed(pcm, Write, () => true, Wait, out int waits);
        Check(ok, "partial writes complete");
        Check(waits == ticks && waits > 0, "full ring actually waited");
        Check(written.Count == pcm.Length + 16384, "one silent ring drained");
        for (int i = 0; i < pcm.Length; i++)
        {
            Check(written[i] == pcm[i], "PCM byte order preserved");
            Check(pcm[i] == original[i], "source PCM unchanged");
        }
        for (int i = pcm.Length; i < written.Count; i++)
            Check(written[i] == 0, "drain is silent");

        // A two-second WAV is valid elsewhere in the pipeline. The opt-in
        // direct HDA feeder must accept the same exact PCM bound, including
        // partial-ring-sized tails, without truncation or source mutation.
        Check(AudioProbeRingFeeder.MaxPcmBytes == 48000 * 2 * 4,
            "two-second stereo16 diagnostic bound");
        byte[] twoSeconds = new byte[AudioProbeRingFeeder.MaxPcmBytes];
        twoSeconds[0] = 0x5A;
        twoSeconds[twoSeconds.Length - 1] = 0xA5;
        int pcmAccepted = 0;
        int silenceAccepted = 0;
        ok = AudioProbeRingFeeder.Feed(twoSeconds,
            (source, offset, count) =>
            {
                Check(count > 0 && count <= 4096 && count % 4 == 0,
                    "bounded frame-aligned writer request");
                Check(offset >= 0 && offset + count <= source.Length,
                    "writer request within source bounds");
                if (ReferenceEquals(source, twoSeconds))
                {
                    Check(offset == pcmAccepted, "sequential PCM writes");
                    pcmAccepted += count;
                }
                else
                {
                    Check(source.Length == 4096, "bounded silent ring block");
                    for (int i = offset; i < offset + count; i++)
                        Check(source[i] == 0, "silent ring contains only zeroes");
                    silenceAccepted += count;
                }
                return count;
            },
            () => true, _ => throw new Exception("unexpected wait with unblocked writer"),
            out waits);
        Check(ok && waits == 0 && pcmAccepted == twoSeconds.Length &&
            silenceAccepted == 16384, "full two-second PCM plus silent ring accepted");
        Check(twoSeconds[0] == 0x5A && twoSeconds[twoSeconds.Length - 1] == 0xA5,
            "two-second source remains unmodified");

        // The opt-in direct path must not stop the cyclic DMA stream
        // immediately after queuing its last silent frame. A single bounded
        // 86 ms settle wait covers the nominal 16 KiB/48 kHz stereo16 ring.
        int settleCalls = 0;
        int settledBytes = 0;
        ok = AudioProbeRingFeeder.FeedAndSettle(new byte[4],
            (source, offset, count) => { settledBytes += count; return count; },
            () => true,
            milliseconds =>
            {
                Check(milliseconds == 86, "one complete HDA ring settle interval");
                settleCalls++;
            }, out waits);
        Check(ok && waits == 0 && settleCalls == 1 && settledBytes == 4 + 16384,
            "direct probe queues silence and waits once before stop");

        bool withdrawnDuringSettle = false;
        settleCalls = 0;
        ok = AudioProbeRingFeeder.FeedAndSettle(new byte[4],
            (source, offset, count) => count,
            () => !withdrawnDuringSettle,
            milliseconds => { withdrawnDuringSettle = true; settleCalls++; }, out waits);
        Check(!ok && settleCalls == 1 && waits == 0,
            "withdrawn output during final settle fails closed");

        settleCalls = 0;
        ok = AudioProbeRingFeeder.FeedAndSettle(new byte[4],
            (source, offset, count) => count,
            () => false,
            milliseconds => settleCalls++, out waits);
        Check(!ok && settleCalls == 0 && waits == 0,
            "withdrawn output before feeding never enters settle wait");

        bool overBoundRejected = false;
        try
        {
            AudioProbeRingFeeder.Feed(new byte[AudioProbeRingFeeder.MaxPcmBytes + 4],
                (a, b, c) => c, () => true, _ => { }, out waits);
        }
        catch (ArgumentException) { overBoundRejected = true; }
        Check(overBoundRejected, "two seconds plus one frame rejected");

        // The output can disappear immediately after accepting the final
        // silent frame. A successful byte count alone is not enough.
        int acceptedBeforeWithdrawal = 0;
        bool withdrawnAfterLastFrame = false;
        ok = AudioProbeRingFeeder.Feed(new byte[4],
            (a, b, c) =>
            {
                acceptedBeforeWithdrawal += c;
                if (acceptedBeforeWithdrawal == 4 + 16384)
                    withdrawnAfterLastFrame = true;
                return c;
            },
            () => !withdrawnAfterLastFrame, _ => { }, out waits);
        Check(!ok && withdrawnAfterLastFrame && acceptedBeforeWithdrawal == 4 + 16384,
            "withdrawn output after final silent frame fails closed");

        int acceptedBeforeMidDrainWithdrawal = 0;
        bool withdrawnDuringDrain = false;
        ok = AudioProbeRingFeeder.Feed(new byte[4],
            (a, b, c) =>
            {
                acceptedBeforeMidDrainWithdrawal += c;
                if (acceptedBeforeMidDrainWithdrawal >= 4 + 4096)
                    withdrawnDuringDrain = true;
                return c;
            },
            () => !withdrawnDuringDrain, _ => { }, out waits);
        Check(!ok && withdrawnDuringDrain && acceptedBeforeMidDrainWithdrawal == 4 + 4096,
            "withdrawn output during silent drain fails closed");

        int stalledWaits = 0;
        ok = AudioProbeRingFeeder.Feed(pcm, (a, b, c) => 0,
            () => true, _ => stalledWaits++, out waits);
        Check(!ok && stalledWaits == 1000 && waits == 1000,
            "permanently full ring fails closed after bounded waits");

        int readyCalls = 0;
        ok = AudioProbeRingFeeder.Feed(pcm, (a, b, c) => 4,
            () => ++readyCalls == 1, _ => { }, out waits);
        Check(!ok && readyCalls == 2, "withdrawn output stops immediately");

        bool rejected = false;
        try
        {
            AudioProbeRingFeeder.Feed(pcm, (a, b, c) => 3,
                () => true, _ => { }, out waits);
        }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "unaligned driver write rejected");

        rejected = false;
        try
        {
            AudioProbeRingFeeder.Feed(pcm, (a, b, c) => c + 4,
                () => true, _ => { }, out waits);
        }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "overreported driver write rejected");

        rejected = false;
        try
        {
            AudioProbeRingFeeder.Feed(pcm, (a, b, c) => -4,
                () => true, _ => { }, out waits);
        }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "negative driver write rejected");

        rejected = false;
        try
        {
            AudioProbeRingFeeder.Feed(new byte[3], (a, b, c) => c,
                () => true, _ => { }, out waits);
        }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "unaligned input rejected");

        rejected = false;
        try
        {
            AudioProbeRingFeeder.Feed(new byte[384004], (a, b, c) => c,
                () => true, _ => { }, out waits);
        }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "oversized PCM rejected");
        Console.WriteLine("[AUDIO-FEEDER-HOST] PASS " + checks +
            " checks (fake ring; not DMA, IRQ or real playback)");
    }
}
