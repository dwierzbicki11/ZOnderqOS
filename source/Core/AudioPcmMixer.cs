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
            if (first == null || second == null ||
                first.Length == 0 || second.Length == 0 ||
                first.Length > MaxPcmBytes || second.Length > MaxPcmBytes ||
                first.Length % 4 != 0 || second.Length % 4 != 0)
                throw new ArgumentException("Two nonempty, frame-aligned, at most two-second stereo16 clips required.");
            if (firstPercent < 0 || firstPercent > 100)
                throw new ArgumentOutOfRangeException(nameof(firstPercent));
            if (secondPercent < 0 || secondPercent > 100)
                throw new ArgumentOutOfRangeException(nameof(secondPercent));

            int length = Math.Max(first.Length, second.Length);
            byte[] output = new byte[length];
            for (int at = 0; at < length; at += 2)
            {
                int a = at < first.Length
                    ? (short)(first[at] | (first[at + 1] << 8)) : 0;
                int b = at < second.Length
                    ? (short)(second[at] | (second[at + 1] << 8)) : 0;
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
