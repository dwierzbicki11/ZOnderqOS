using System;
using System.Collections.Generic;
using ZonderqOS.SystemCore;

// Deterministic host-only 16 KiB FIFO model for the opt-in direct HDA
// feeder. This is NOT emulated DMA/IRQ or evidence of audible output.
internal static class AudioRingTimelineSmoke
{
    private const int RingBytes = 16 * 1024;
    private const int BytesPerMillisecond = 48000 * 4 / 1000;

    public static void Run(Action<bool, string> check)
    {
        RunCase(12000, 1024, check); // 250 ms, deliberately short writes
        RunCase(96000, 4096, check); // exact two-second PCM budget
    }

    private static void RunCase(int frames, int maxWriteBytes,
        Action<bool, string> check)
    {
        byte[] pcm = new byte[frames * 4];
        for (int i = 0; i < pcm.Length; i++)
            pcm[i] = (byte)((i * 37 + (i >> 7) + 11) & 255);
        byte[] original = (byte[])pcm.Clone();

        var ring = new Queue<byte>(RingBytes);
        var captured = new List<byte>(pcm.Length + RingBytes);
        int writes = 0;
        int maxOccupancy = 0;
        int oneMsWaits = 0;
        int settleWaits = 0;

        int Write(byte[] source, int offset, int count)
        {
            // Model only frame-aligned writes into a finite cyclic ring.
            // The model does not assert that the real HDA engine advances.
            int accepted = Math.Min(Math.Min(count, maxWriteBytes),
                RingBytes - ring.Count) & ~3;
            for (int i = 0; i < accepted; i++)
                ring.Enqueue(source[offset + i]);
            writes++;
            maxOccupancy = Math.Max(maxOccupancy, ring.Count);
            return accepted;
        }

        void Wait(uint milliseconds)
        {
            if (milliseconds == 1)
                oneMsWaits++;
            else if (milliseconds == 86)
                settleWaits++;
            else
                throw new Exception("Unexpected ring wait duration");

            int consumed = Math.Min(ring.Count,
                checked((int)milliseconds * BytesPerMillisecond));
            for (int i = 0; i < consumed; i++)
                captured.Add(ring.Dequeue());
        }

        bool ok = AudioProbeRingFeeder.FeedAndSettle(pcm,
            Write, () => true, Wait, out int reportedWaits);
        check(ok && writes > 0 && reportedWaits == oneMsWaits &&
            oneMsWaits > 0 && settleWaits == 1,
            "16KiB FIFO: bounded 1ms backpressure and 86ms settle");
        check(maxOccupancy <= RingBytes && ring.Count == 0,
            "16KiB FIFO: no overrun and final silent ring fully consumed");
        check(captured.Count == pcm.Length + RingBytes,
            "16KiB FIFO: every PCM and drain byte consumed exactly once");

        for (int i = 0; i < pcm.Length; i++)
        {
            check(pcm[i] == original[i], "16KiB FIFO: caller PCM unchanged");
            check(captured[i] == original[i],
                "16KiB FIFO: PCM preserved through partial writes and wraps");
        }
        for (int i = pcm.Length; i < captured.Count; i++)
            check(captured[i] == 0,
                "16KiB FIFO: final 16KiB contains silence only");

        Console.WriteLine("[AUDIO-RING-TIMELINE-HOST] PASS frames=" + frames +
            " waits=" + oneMsWaits + " writes=" + writes +
            " (deterministic FIFO only; not DMA/IRQ or QEMU)");
    }
}
