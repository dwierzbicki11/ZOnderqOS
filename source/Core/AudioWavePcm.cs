using System;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Strict, allocation-bounded RIFF/WAVE PCM16 stereo decoder for the
    /// opt-in diskless HDA probe. No filesystem, DMA or device access.
    /// Currently only 48 kHz PCM is supported; do not silently resample.
    /// </summary>
    public static class AudioWavePcm
    {
        public const int SampleRate = 48000;
        public const int MaxDurationSeconds = 2;
        private const int FrameBytes = 4;
        private const int MaxDataBytes = SampleRate * MaxDurationSeconds * FrameBytes;

        private static uint Read32(byte[] bytes, int index)
        {
            return (uint)(bytes[index] | ((uint)bytes[index + 1] << 8) |
                ((uint)bytes[index + 2] << 16) | ((uint)bytes[index + 3] << 24));
        }

        private static int Read16(byte[] bytes, int index)
        {
            return bytes[index] | (bytes[index + 1] << 8);
        }

        private static void Write32(byte[] bytes, int index, int value)
        {
            bytes[index] = (byte)value;
            bytes[index + 1] = (byte)(value >> 8);
            bytes[index + 2] = (byte)(value >> 16);
            bytes[index + 3] = (byte)(value >> 24);
        }

        private static void Write16(byte[] bytes, int index, int value)
        {
            bytes[index] = (byte)value;
            bytes[index + 1] = (byte)(value >> 8);
        }

        private static bool Tag(byte[] bytes, int index, string tag)
        {
            for (int i = 0; i < 4; i++)
                if (bytes[index + i] != (byte)tag[i]) return false;
            return true;
        }

        private static void PutTag(byte[] bytes, int index, string tag)
        {
            for (int i = 0; i < 4; i++) bytes[index + i] = (byte)tag[i];
        }

        public static byte[] DecodeStereo16(byte[] wave)
        {
            if (wave == null || wave.Length < 44 || !Tag(wave, 0, "RIFF") ||
                !Tag(wave, 8, "WAVE"))
                throw new ArgumentException("Missing RIFF/WAVE header.", nameof(wave));

            long riffEnd = 8L + Read32(wave, 4);
            if (riffEnd != wave.Length)
                throw new ArgumentException("Invalid RIFF length.", nameof(wave));

            bool hasFormat = false;
            bool hasData = false;
            int dataOffset = 0;
            int dataSize = 0;
            int pos = 12;
            while (pos < wave.Length)
            {
                if (wave.Length - pos < 8)
                    throw new ArgumentException("Truncated WAV chunk header.", nameof(wave));
                long size = Read32(wave, pos + 4);
                long end = (long)pos + 8 + size;
                long next = end + (size & 1);
                if (next > wave.Length)
                    throw new ArgumentException("Truncated WAV chunk payload.", nameof(wave));

                if (Tag(wave, pos, "fmt "))
                {
                    if (hasFormat || size < 16)
                        throw new ArgumentException("Duplicate or short WAV fmt chunk.", nameof(wave));
                    int format = Read16(wave, pos + 8);
                    int channels = Read16(wave, pos + 10);
                    uint rate = Read32(wave, pos + 12);
                    uint byteRate = Read32(wave, pos + 16);
                    int blockAlign = Read16(wave, pos + 20);
                    int bits = Read16(wave, pos + 22);
                    if (format != 1 || channels != 2 || rate != SampleRate ||
                        bits != 16 || blockAlign != FrameBytes ||
                        byteRate != SampleRate * FrameBytes)
                        throw new ArgumentException("Unsupported WAV format (requires 48kHz PCM16 stereo).", nameof(wave));
                    hasFormat = true;
                }
                else if (Tag(wave, pos, "data"))
                {
                    if (hasData || size == 0 || size > MaxDataBytes || size % FrameBytes != 0)
                        throw new ArgumentException("Invalid WAV data chunk.", nameof(wave));
                    dataOffset = pos + 8;
                    dataSize = (int)size;
                    hasData = true;
                }
                pos = (int)next;
            }
            if (!hasFormat || !hasData)
                throw new ArgumentException("WAV fmt or data chunk missing.", nameof(wave));

            byte[] pcm = new byte[dataSize];
            Array.Copy(wave, dataOffset, pcm, 0, dataSize);
            return pcm;
        }

        /// <summary>Build an in-memory RIFF fixture for exercising the decoder.</summary>
        public static byte[] EncodeStereo16(byte[] pcm)
        {
            if (pcm == null || pcm.Length == 0 || pcm.Length > MaxDataBytes ||
                pcm.Length % FrameBytes != 0)
                throw new ArgumentException("PCM must be nonempty, stereo16 and at most 2 seconds.", nameof(pcm));
            byte[] wave = new byte[44 + pcm.Length];
            PutTag(wave, 0, "RIFF");
            Write32(wave, 4, wave.Length - 8);
            PutTag(wave, 8, "WAVE");
            PutTag(wave, 12, "fmt ");
            Write32(wave, 16, 16);
            Write16(wave, 20, 1);
            Write16(wave, 22, 2);
            Write32(wave, 24, SampleRate);
            Write32(wave, 28, SampleRate * FrameBytes);
            Write16(wave, 32, FrameBytes);
            Write16(wave, 34, 16);
            PutTag(wave, 36, "data");
            Write32(wave, 40, pcm.Length);
            Array.Copy(pcm, 0, wave, 44, pcm.Length);
            return wave;
        }
    }
}
