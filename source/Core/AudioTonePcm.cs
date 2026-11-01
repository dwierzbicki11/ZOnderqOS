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
        /// <summary>
        /// Bounded true-stereo diagnostic: independent left/right frequencies.
        /// Reuses the validated PCM16 generator, then replaces only right
        /// samples. Does not access a codec, DMA or filesystem.
        /// </summary>
        public static byte[] GenerateDualSquareStereo16(int sampleRate,
            int leftFrequency, int rightFrequency, int durationMs, short amplitude)
        {
            // The previous implementation allocated two full stereo buffers
            // and discarded three quarters of the second one. Generate both
            // independent channels directly into one bounded output buffer.
            if (sampleRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (leftFrequency <= 0 || (long)leftFrequency * 2 >= sampleRate)
                throw new ArgumentOutOfRangeException(nameof(leftFrequency));
            if (rightFrequency <= 0 || (long)rightFrequency * 2 >= sampleRate)
                throw new ArgumentOutOfRangeException(nameof(rightFrequency));
            if (durationMs <= 0 || durationMs > 2000)
                throw new ArgumentOutOfRangeException(nameof(durationMs));
            if (amplitude <= 0)
                throw new ArgumentOutOfRangeException(nameof(amplitude));

            long framesLong = (long)sampleRate * durationMs / 1000;
            if (framesLong <= 0 || framesLong > int.MaxValue / 4)
                throw new ArgumentOutOfRangeException(nameof(durationMs));
            int frames = (int)framesLong;
            byte[] stereo = new byte[frames * 4];
            for (int frame = 0; frame < frames; frame++)
            {
                short left = (short)(((((long)frame * leftFrequency * 2) / sampleRate) & 1) == 0
                    ? amplitude : -amplitude);
                short right = (short)(((((long)frame * rightFrequency * 2) / sampleRate) & 1) == 0
                    ? amplitude : -amplitude);
                int at = frame * 4;
                stereo[at] = (byte)left;
                stereo[at + 1] = (byte)(left >> 8);
                stereo[at + 2] = (byte)right;
                stereo[at + 3] = (byte)(right >> 8);
            }
            return stereo;
        }
    }
}
