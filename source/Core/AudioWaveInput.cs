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
        // Up to two seconds of stereo signed PCM32 plus bounded RIFF metadata.
        // The decoded stereo PCM16 remains capped at 384 KiB (384,000 bytes).
        public const int MaxFileBytes = AudioWavePcm.MaxRiffBytes;

        public static byte[] DecodeBounded(Stream input)
        {
            if (input == null || !input.CanRead || input.CanWrite)
                throw new ArgumentException("Read-only WAV stream required.", nameof(input));

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

            // Seekable read-only files expose their remaining length. Reject a
            // truncated or trailing RIFF before allocating up to 800 KiB or
            // reading the payload. Non-seekable streams keep the bounded
            // ReadExact + EOF path below. This never seeks or writes.
            if (input.CanSeek && input.Length - input.Position != riffLength - header.Length)
                throw new ArgumentException(
                    "RIFF length differs from read-only stream length.", nameof(input));

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
