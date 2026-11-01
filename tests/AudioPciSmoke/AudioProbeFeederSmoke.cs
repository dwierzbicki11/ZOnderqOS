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
