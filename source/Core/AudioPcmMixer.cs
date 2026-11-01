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
        /// <summary>
        /// Mix up to eight scheduled stereo PCM16 clips at 48 kHz. Each start
        /// offset is measured in whole frames, and every clip has independent
        /// 0..100% gain. Validate the complete schedule before allocating.
        /// All signed contributions are accumulated before a single final
        /// saturation step: intermediate clipping would make cancellation
        /// depend on the order of sound effects.
        /// This is a bounded offline PCM mixer, not a hardware/IRQ queue.
        /// </summary>
        public static byte[] MixScheduledStereo16(
            byte[][] clips, int[] startFrames, int[] gainsPercent)
        {
            if (clips == null || startFrames == null || gainsPercent == null ||
                clips.Length == 0 || clips.Length > 8 ||
                startFrames.Length != clips.Length || gainsPercent.Length != clips.Length)
                throw new ArgumentException("Schedule requires 1..8 clips with matching offsets and gains.");

            int length = 0;
            for (int i = 0; i < clips.Length; i++)
            {
                byte[] clip = clips[i];
                if (clip == null || clip.Length == 0 ||
                    clip.Length > MaxPcmBytes || clip.Length % 4 != 0)
                    throw new ArgumentException("Every clip must be nonempty, frame-aligned stereo16 and at most two seconds.");
                if (startFrames[i] < 0 || startFrames[i] > MaxPcmBytes / 4)
                    throw new ArgumentOutOfRangeException(nameof(startFrames));
                if (gainsPercent[i] < 0 || gainsPercent[i] > 100)
                    throw new ArgumentOutOfRangeException(nameof(gainsPercent));

                long end = (long)startFrames[i] * 4 + clip.Length;
                if (end > MaxPcmBytes)
                    throw new ArgumentOutOfRangeException(nameof(startFrames),
                        "Scheduled clip would exceed the two-second PCM budget.");
                if (end > length) length = (int)end;
            }

            byte[] output = new byte[length];
            for (int at = 0; at < length; at += 2)
            {
                int total = 0;
                for (int i = 0; i < clips.Length; i++)
                {
                    int sourceAt = at - startFrames[i] * 4;
                    byte[] clip = clips[i];
                    if (sourceAt < 0 || sourceAt >= clip.Length)
                        continue;
                    int sample = (short)(clip[sourceAt] | (clip[sourceAt + 1] << 8));
                    total += sample * gainsPercent[i] / 100;
                }
                if (total > short.MaxValue) total = short.MaxValue;
                else if (total < short.MinValue) total = short.MinValue;
                output[at] = (byte)total;
                output[at + 1] = (byte)(total >> 8);
            }
            return output;
        }


        /// <summary>
        /// Schedule up to eight short effects with a symmetric, per-clip
        /// attack/release envelope. A nonzero fade is 1..50 ms at 48 kHz,
        /// limited to half of each clip; it does not move scheduled onsets
        /// or change the output duration. Sum all faded contributions before
        /// final saturation so cancellation is independent of clip order.
        /// No intermediate PCM arrays, codec or DMA access are required.
        /// </summary>
        public static byte[] MixScheduledStereo16WithFade(
            byte[][] clips, int[] startFrames, int[] gainsPercent, int fadeMilliseconds)
        {
            if (fadeMilliseconds < 0 || fadeMilliseconds > 50)
                throw new ArgumentOutOfRangeException(nameof(fadeMilliseconds));
            if (fadeMilliseconds == 0)
                return MixScheduledStereo16(clips, startFrames, gainsPercent);

            if (clips == null || startFrames == null || gainsPercent == null ||
                clips.Length == 0 || clips.Length > 8 ||
                startFrames.Length != clips.Length || gainsPercent.Length != clips.Length)
                throw new ArgumentException("Schedule requires 1..8 clips with matching offsets and gains.");

            int length = 0;
            for (int i = 0; i < clips.Length; i++)
            {
                byte[] clip = clips[i];
                if (clip == null || clip.Length == 0 ||
                    clip.Length > MaxPcmBytes || clip.Length % 4 != 0)
                    throw new ArgumentException("Every clip must be nonempty, frame-aligned stereo16 and at most two seconds.");
                if (startFrames[i] < 0 || startFrames[i] > MaxPcmBytes / 4)
                    throw new ArgumentOutOfRangeException(nameof(startFrames));
                if (gainsPercent[i] < 0 || gainsPercent[i] > 100)
                    throw new ArgumentOutOfRangeException(nameof(gainsPercent));

                long end = (long)startFrames[i] * 4 + clip.Length;
                if (end > MaxPcmBytes)
                    throw new ArgumentOutOfRangeException(nameof(startFrames),
                        "Scheduled clip would exceed the two-second PCM budget.");
                if (end > length) length = (int)end;
            }

            byte[] output = new byte[length];
            for (int at = 0; at < length; at += 2)
            {
                int total = 0;
                for (int i = 0; i < clips.Length; i++)
                {
                    int sourceAt = at - startFrames[i] * 4;
                    byte[] clip = clips[i];
                    if (sourceAt < 0 || sourceAt >= clip.Length)
                        continue;

                    int clipFrames = clip.Length / 4;
                    int frame = sourceAt / 4;
                    int fadeFrames = Math.Min(fadeMilliseconds * 48, clipFrames / 2);
                    if (fadeFrames == 0)
                        continue; // A one-frame effect fades to silence.

                    int envelope = Math.Min(fadeFrames,
                        Math.Min(frame, clipFrames - 1 - frame));
                    if (envelope == 0)
                        continue;

                    int sample = (short)(clip[sourceAt] | (clip[sourceAt + 1] << 8));
                    int scaled = sample * gainsPercent[i] / 100;
                    total += scaled * envelope / fadeFrames;
                }
                if (total > short.MaxValue) total = short.MaxValue;
                else if (total < short.MinValue) total = short.MinValue;
                output[at] = (byte)total;
                output[at + 1] = (byte)(total >> 8);
            }
            return output;
        }

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
