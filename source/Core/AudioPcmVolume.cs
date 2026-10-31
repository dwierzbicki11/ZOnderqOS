using System;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Software PCM16 gain for the opt-in audio probe. Returns a new buffer;
    /// never mutates the caller's samples, codec registers or DMA descriptors.
    /// </summary>
    public static class AudioPcmVolume
    {
        public static byte[] ScaleStereo16(byte[] pcm, int percent)
        {
            if (pcm == null || pcm.Length == 0 || pcm.Length % 4 != 0)
                throw new ArgumentException("PCM16 stereo frames required.", nameof(pcm));
            if (percent < 0 || percent > 100)
                throw new ArgumentOutOfRangeException(nameof(percent));

            byte[] result = new byte[pcm.Length];
            for (int i = 0; i < pcm.Length; i += 2)
            {
                short sample = (short)(pcm[i] | (pcm[i + 1] << 8));
                int scaled = sample * percent / 100;
                result[i] = (byte)scaled;
                result[i + 1] = (byte)(scaled >> 8);
            }
            return result;
        }
    }
}
