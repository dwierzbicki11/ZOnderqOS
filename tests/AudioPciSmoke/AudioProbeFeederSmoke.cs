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

        int stalledWaits = 0;
        ok = AudioProbeRingFeeder.Feed(pcm, (a, b, c) => 0,
            () => true, _ => stalledWaits++, out waits);
        Check(!ok && stalledWaits == 1000 && waits == 1001,
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
