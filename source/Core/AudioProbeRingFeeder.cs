using System;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Bounded, single-caller ring writer for the diskless AUDIO diagnostic ISO.
    /// A 1 ms full-ring wait is an A/B experiment against stock AudioPlayer's
    /// 5 ms wait, NOT a replacement for its output ownership/claim protocol.
    /// No driver registers, DMA descriptors, interrupts or disks are accessed.
    /// </summary>
    public static class AudioProbeRingFeeder
    {
        // Match the two-second 48 kHz stereo16 limit used by the WAV decoder,
        // PCM mixer and volume stage. The previous one-second limit rejected
        // otherwise valid opt-in diagnostic PCM after it was decoded.
        public const int MaxPcmBytes = 48000 * 2 * 4 * 2;
        private const int RingBytes = 16 * 1024;
        private const int BlockBytes = 4096;
        private const int MaxConsecutiveWaits = 1000;
        private const int MaxTotalWaits = 6000;

        /// <summary>
        /// Write at most two seconds of stereo16, then one complete silent
        /// HDA ring. Only the isolated console may invoke this on an idle,
        /// unclaimed output. A zero write waits 1 ms, never spins forever.
        /// The caller must stop the device in a finally block.
        /// </summary>
        public static bool Feed(
            byte[] pcm, Func<byte[], int, int, int> write,
            Func<bool> ready, Action<uint> wait, out int waitCalls)
        {
            if (pcm == null || pcm.Length == 0 || pcm.Length > MaxPcmBytes ||
                pcm.Length % 4 != 0)
                throw new ArgumentException("Bounded frame-aligned stereo16 PCM required.", nameof(pcm));
            if (write == null || ready == null || wait == null)
                throw new ArgumentNullException("Ring writer callbacks are required.");

            waitCalls = 0;
            int consecutiveWaits = 0;
            if (!WriteAll(pcm, pcm.Length, write, ready, wait,
                ref waitCalls, ref consecutiveWaits))
                return false;

            // One ring of silence after the final PCM frame is necessary:
            // the HDA DMA engine is cyclic and may otherwise replay old data.
            byte[] silence = new byte[BlockBytes];
            for (int i = 0; i < RingBytes / BlockBytes; i++)
                if (!WriteAll(silence, silence.Length, write, ready, wait,
                    ref waitCalls, ref consecutiveWaits))
                    return false;
            // A device withdrawn just after the final silent write must not
            // be reported as successfully drained. This is a final snapshot,
            // not a claim that DMA/IRQ or audible playback succeeded.
            return ready();
        }

        private static bool WriteAll(
            byte[] source, int length, Func<byte[], int, int, int> write,
            Func<bool> ready, Action<uint> wait,
            ref int waitCalls, ref int consecutiveWaits)
        {
            int offset = 0;
            while (offset < length)
            {
                if (!ready())
                    return false;

                int wanted = Math.Min(BlockBytes, length - offset);
                int written = write(source, offset, wanted);
                if (written < 0 || written > wanted || written % 4 != 0)
                    throw new InvalidOperationException("Audio device returned an invalid PCM frame count.");
                if (written == 0)
                {
                    if (++consecutiveWaits > MaxConsecutiveWaits ||
                        ++waitCalls > MaxTotalWaits)
                        return false;
                    wait(1);
                    continue;
                }

                consecutiveWaits = 0;
                offset += written;
            }
            return true;
        }
    }
}
