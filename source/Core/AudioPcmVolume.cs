using System;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Software PCM16 gain/balance for the opt-in audio probe. Returns a new
    /// buffer; never modifies source PCM, codec registers or DMA descriptors.
    /// </summary>
    public static class AudioPcmVolume
    {
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
            if (pcm == null || pcm.Length == 0 || pcm.Length % 4 != 0)
                throw new ArgumentException("PCM16 stereo frames required.", nameof(pcm));
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
