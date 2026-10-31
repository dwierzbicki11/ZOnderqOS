using System;
using System.IO;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Bounded, read-only RIFF/WAVE stream loader. Accepts a caller-provided
    /// stream, never opens a disk, seeks, writes or mounts a volume. Intended
    /// for a future explicitly authorized read-only WAV source.
    /// </summary>
    public static class AudioWaveInput
    {
        // Up to two seconds of stereo PCM16 plus bounded RIFF metadata.
        public const int MaxFileBytes = 512 * 1024;

        public static byte[] DecodeBounded(Stream input)
        {
            if (input == null || !input.CanRead)
                throw new ArgumentException("Readable WAV stream required.", nameof(input));

            byte[] header = new byte[12];
            ReadExact(input, header, 0, header.Length);
            if (header[0] != 'R' || header[1] != 'I' ||
                header[2] != 'F' || header[3] != 'F' ||
                header[8] != 'W' || header[9] != 'A' ||
                header[10] != 'V' || header[11] != 'E')
                throw new ArgumentException("Missing RIFF/WAVE header.", nameof(input));

            long riffLength = (long)(uint)(header[4] |
                ((uint)header[5] << 8) |
                ((uint)header[6] << 16) |
                ((uint)header[7] << 24)) + 8;
            if (riffLength < 44 || riffLength > MaxFileBytes)
                throw new ArgumentException("WAV stream exceeds the bounded file limit.", nameof(input));

            byte[] wave = new byte[(int)riffLength];
            Array.Copy(header, 0, wave, 0, header.Length);
            ReadExact(input, wave, header.Length, wave.Length - header.Length);
            if (input.ReadByte() != -1)
                throw new ArgumentException("Trailing bytes after RIFF/WAVE payload.", nameof(input));

            return AudioWavePcm.DecodeStereo16(wave);
        }

        private static void ReadExact(Stream input, byte[] target, int offset, int count)
        {
            while (count > 0)
            {
                int read = input.Read(target, offset, count);
                if (read <= 0)
                    throw new EndOfStreamException("Truncated RIFF/WAVE stream.");
                offset += read;
                count -= read;
            }
        }
    }
}
