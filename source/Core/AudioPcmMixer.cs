using System;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Bounded software mixer for two independent interleaved stereo PCM16
    /// sources at 48 kHz. This is pure arithmetic: no codec, DMA, disk, IRQ
    /// or hardware access. Intended for future overlapping GUI sound effects.
    /// </summary>
    public static class AudioPcmMixer
    {
        private const int MaxPcmBytes = 48000 * 2 * 4;

        /// <summary>
        /// Mix two clips with independent 0..100% gains. Shorter clips are
        /// treated as silence after EOF. Saturating addition prevents signed
        /// 16-bit wraparound; input arrays are never modified.
        /// </summary>
        public static byte[] MixStereo16(
            byte[] first, byte[] second, int firstPercent, int secondPercent)
        {
            return MixStereo16WithDelay(first, second, firstPercent, secondPercent, 0);
        }

        /// <summary>
        /// Schedule the second clip a whole number of 48 kHz stereo frames
        /// after the first. The output remains bounded to two seconds; an
        /// overlong delayed clip is rejected rather than silently truncated.
        /// This is pure PCM arithmetic, not a hardware scheduling guarantee.
        /// </summary>
        public static byte[] MixStereo16WithDelay(
            byte[] first, byte[] second, int firstPercent, int secondPercent,
            int secondDelayFrames)
        {
            if (secondDelayFrames < 0 || secondDelayFrames > MaxPcmBytes / 4)
                throw new ArgumentOutOfRangeException(nameof(secondDelayFrames));
            if (first == null || second == null ||
                first.Length == 0 || second.Length == 0 ||
                first.Length > MaxPcmBytes || second.Length > MaxPcmBytes ||
                first.Length % 4 != 0 || second.Length % 4 != 0)
                throw new ArgumentException("Two nonempty, frame-aligned, at most two-second stereo16 clips required.");
            if (firstPercent < 0 || firstPercent > 100)
                throw new ArgumentOutOfRangeException(nameof(firstPercent));
            if (secondPercent < 0 || secondPercent > 100)
                throw new ArgumentOutOfRangeException(nameof(secondPercent));

            int delayBytes = secondDelayFrames * 4;
            if ((long)delayBytes + second.Length > MaxPcmBytes)
                throw new ArgumentOutOfRangeException(nameof(secondDelayFrames),
                    "Delayed PCM clip would exceed the two-second output budget.");

            int length = Math.Max(first.Length, delayBytes + second.Length);
            byte[] output = new byte[length];
            for (int at = 0; at < length; at += 2)
            {
                int a = at < first.Length
                    ? (short)(first[at] | (first[at + 1] << 8)) : 0;
                int secondAt = at - delayBytes;
                int b = secondAt >= 0 && secondAt < second.Length
                    ? (short)(second[secondAt] | (second[secondAt + 1] << 8)) : 0;
                int sum = a * firstPercent / 100 + b * secondPercent / 100;
                if (sum > short.MaxValue) sum = short.MaxValue;
                else if (sum < short.MinValue) sum = short.MinValue;
                output[at] = (byte)sum;
                output[at + 1] = (byte)(sum >> 8);
            }
            return output;
        }
    }
}
