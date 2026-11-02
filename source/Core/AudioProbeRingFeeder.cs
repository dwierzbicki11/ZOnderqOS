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
        // Exactly two seconds of 48 kHz stereo16 (4 bytes per frame).
        // Keep the direct HDA path and feeder on one shared byte limit.
        public const int MaxPcmBytes = 48000 * 2 * 4;
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

        /// <summary>
        /// Diagnostic-only extension: after one full silent ring has been
        /// accepted, leave the 48 kHz stereo16 HDA stream running for at
        /// least one complete 16 KiB ring period (85.34 ms, rounded up).
        /// The caller must still stop the output in finally. This is a
        /// bounded timing experiment, NOT proof that DMA/IRQ advanced or
        /// that speakers played every frame. waitCalls counts only the 1 ms
        /// ring-full waits; the single settle wait is reported separately.
        /// </summary>
        public static bool FeedAndSettle(
            byte[] pcm, Func<byte[], int, int, int> write,
            Func<bool> ready, Action<uint> wait, out int waitCalls)
        {
            if (!Feed(pcm, write, ready, wait, out waitCalls))
                return false;
            if (!ready())
                return false;
            // ceil(16384 bytes * 1000 / (48000 frames/s * 4 bytes/frame))
            wait(86);
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
                    // Count only waits that are actually requested. The
                    // previous pre-increment could report 6001 waits even
                    // though the global 6000-wait limit refused the last one.
                    if (consecutiveWaits >= MaxConsecutiveWaits ||
                        waitCalls >= MaxTotalWaits)
                        return false;
                    consecutiveWaits++;
                    waitCalls++;
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
