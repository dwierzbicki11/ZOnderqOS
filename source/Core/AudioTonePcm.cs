using System;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Pure signed PCM16 stereo tone generator. No HAL, DMA, disk or audio
    /// device dependency; the same bytes can be checked by host-side C# tests.
    /// </summary>
    public static class AudioTonePcm
    {
        public static byte[] GenerateSquareStereo16(int sampleRate, int frequency,
            int durationMs, short amplitude)
        {
            if (sampleRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (frequency <= 0 || (long)frequency * 2 >= sampleRate)
                throw new ArgumentOutOfRangeException(nameof(frequency));
            if (durationMs <= 0 || durationMs > 2000)
                throw new ArgumentOutOfRangeException(nameof(durationMs));
            if (amplitude <= 0)
                throw new ArgumentOutOfRangeException(nameof(amplitude));

            long framesLong = (long)sampleRate * durationMs / 1000;
            if (framesLong <= 0 || framesLong > int.MaxValue / 4)
                throw new ArgumentOutOfRangeException(nameof(durationMs));
            int frames = (int)framesLong;
            byte[] pcm = new byte[frames * 4];

            for (int i = 0; i < frames; i++)
            {
                // Two sign transitions make one period, not two. Use 64-bit
                // arithmetic so long buffers cannot overflow the phase.
                short sample = (short)(((((long)i * frequency * 2) / sampleRate) & 1) == 0
                    ? amplitude : -amplitude);
                byte lo = (byte)sample;
                byte hi = (byte)(sample >> 8);
                int offset = i * 4;
                pcm[offset] = lo;
                pcm[offset + 1] = hi;
                pcm[offset + 2] = lo;
                pcm[offset + 3] = hi;
            }
            return pcm;
        }
    }
}
