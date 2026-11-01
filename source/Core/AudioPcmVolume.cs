using System;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Software PCM16 gain/balance for the opt-in audio probe. Returns a new
    /// buffer; never modifies source PCM, codec registers or DMA descriptors.
    /// </summary>
    public static class AudioPcmVolume
    {
        /// <summary>
        /// Apply a bounded, symmetric linear fade to interleaved 48 kHz
        /// signed PCM16 stereo. This removes sharp start/end transitions in
        /// short diagnostic sounds without touching codec registers or DMA.
        /// The input is never modified. A zero fade returns an exact copy.
        /// </summary>
        public static byte[] FadeStereo16(byte[] pcm, int fadeMilliseconds)
        {
            if (pcm == null || pcm.Length == 0 || pcm.Length % 4 != 0 ||
                pcm.Length > 48000 * 2 * 4)
                throw new ArgumentException("Up to two seconds of 48 kHz PCM16 stereo required.", nameof(pcm));
            if (fadeMilliseconds < 0 || fadeMilliseconds > 50)
                throw new ArgumentOutOfRangeException(nameof(fadeMilliseconds));

            byte[] result = (byte[])pcm.Clone();
            if (fadeMilliseconds == 0)
                return result;

            int frames = pcm.Length / 4;
            int fadeFrames = Math.Min(fadeMilliseconds * 48, frames / 2);
            if (fadeFrames == 0)
            {
                // Even a one-frame clip must not create an edge transient.
                Array.Clear(result, 0, result.Length);
                return result;
            }

            for (int frame = 0; frame < frames; frame++)
            {
                int fromEnd = frames - 1 - frame;
                int numerator = Math.Min(fadeFrames, Math.Min(frame, fromEnd));
                if (numerator == fadeFrames)
                    continue;
                int at = frame * 4;
                for (int channel = 0; channel < 2; channel++)
                {
                    int offset = at + channel * 2;
                    short sample = (short)(pcm[offset] | (pcm[offset + 1] << 8));
                    int scaled = sample * numerator / fadeFrames;
                    result[offset] = (byte)scaled;
                    result[offset + 1] = (byte)(scaled >> 8);
                }
            }
            return result;
        }

        /// <summary>
        /// Apply independent software gains and a symmetric 48 kHz PCM fade
        /// in one pass, allocating exactly one output array. The two integer
        /// truncations match ScaleStereo16Channels followed by FadeStereo16.
        /// Source PCM is never modified.
        /// </summary>
        public static byte[] ScaleStereo16ChannelsWithFade(
            byte[] pcm, int leftPercent, int rightPercent, int fadeMilliseconds)
        {
            if (pcm == null || pcm.Length == 0 || pcm.Length % 4 != 0 ||
                pcm.Length > 48000 * 2 * 4)
                throw new ArgumentException("Up to two seconds of 48 kHz PCM16 stereo required.", nameof(pcm));
            if (leftPercent < 0 || leftPercent > 100)
                throw new ArgumentOutOfRangeException(nameof(leftPercent));
            if (rightPercent < 0 || rightPercent > 100)
                throw new ArgumentOutOfRangeException(nameof(rightPercent));
            if (fadeMilliseconds < 0 || fadeMilliseconds > 50)
                throw new ArgumentOutOfRangeException(nameof(fadeMilliseconds));

            int frames = pcm.Length / 4;
            int fadeFrames = Math.Min(fadeMilliseconds * 48, frames / 2);
            byte[] output = new byte[pcm.Length];
            for (int frame = 0; frame < frames; frame++)
            {
                int envelope = fadeMilliseconds == 0 ? 1 :
                    Math.Min(fadeFrames, Math.Min(frame, frames - 1 - frame));
                int divisor = fadeMilliseconds == 0 ? 1 : fadeFrames;
                for (int channel = 0; channel < 2; channel++)
                {
                    int offset = frame * 4 + channel * 2;
                    short sample = (short)(pcm[offset] | (pcm[offset + 1] << 8));
                    int gain = channel == 0 ? leftPercent : rightPercent;
                    int scaled = sample * gain / 100;
                    scaled = divisor == 0 ? 0 : scaled * envelope / divisor;
                    output[offset] = (byte)scaled;
                    output[offset + 1] = (byte)(scaled >> 8);
                }
            }
            return output;
        }

        public static byte[] ScaleStereo16(byte[] pcm, int percent)
        {
            return ScaleStereo16Channels(pcm, percent, percent);
        }

        /// <summary>
        /// Independent left/right gains 0..100 on interleaved little-endian
        /// signed PCM16. Multiplication stays within Int32.
        /// </summary>
        public static byte[] ScaleStereo16Channels(byte[] pcm, int leftPercent, int rightPercent)
        {
            // Keep the non-faded path within the same two-second PCM budget
            // as FadeStereo16 and ScaleStereo16ChannelsWithFade. Reject before
            // allocating an output buffer for an untrusted caller.
            if (pcm == null || pcm.Length == 0 || pcm.Length % 4 != 0 ||
                pcm.Length > 48000 * 2 * 4)
                throw new ArgumentException("Up to two seconds of 48 kHz PCM16 stereo required.", nameof(pcm));
            if (leftPercent < 0 || leftPercent > 100)
                throw new ArgumentOutOfRangeException(nameof(leftPercent));
            if (rightPercent < 0 || rightPercent > 100)
                throw new ArgumentOutOfRangeException(nameof(rightPercent));

            byte[] result = new byte[pcm.Length];
            for (int i = 0; i < pcm.Length; i += 4)
            {
                short left = (short)(pcm[i] | (pcm[i + 1] << 8));
                short right = (short)(pcm[i + 2] | (pcm[i + 3] << 8));
                int scaledLeft = left * leftPercent / 100;
                int scaledRight = right * rightPercent / 100;
                result[i] = (byte)scaledLeft;
                result[i + 1] = (byte)(scaledLeft >> 8);
                result[i + 2] = (byte)scaledRight;
                result[i + 3] = (byte)(scaledRight >> 8);
            }
            return result;
        }
    }
}
