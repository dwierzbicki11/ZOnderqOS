using System;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Strict, allocation-bounded RIFF/WAVE PCM8/PCM16 mono/stereo decoder for the
    /// opt-in diskless HDA probe. No filesystem, DMA or device access.
    /// 44.1 kHz sources are explicitly resampled to 48 kHz stereo PCM16.
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
            int channels = 0;
            int sourceRate = 0;
            int sourceBits = 0;
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
                    int sourceChannels = Read16(wave, pos + 10);
                    uint rate = Read32(wave, pos + 12);
                    uint byteRate = Read32(wave, pos + 16);
                    int blockAlign = Read16(wave, pos + 20);
                    int bits = Read16(wave, pos + 22);
                    if (format != 1 || (sourceChannels != 1 && sourceChannels != 2) ||
                        (rate != SampleRate && rate != 44100) || (bits != 8 && bits != 16) ||
                        blockAlign != sourceChannels * (bits / 8) ||
                        byteRate != rate * sourceChannels * (uint)(bits / 8))
                        throw new ArgumentException("Unsupported WAV format (requires 44.1/48kHz PCM8/PCM16 mono/stereo).", nameof(wave));
                    channels = sourceChannels;
                    sourceRate = (int)rate;
                    sourceBits = bits;
                    hasFormat = true;
                }
                else if (Tag(wave, pos, "data"))
                {
                    if (hasData || size == 0 || size > MaxDataBytes)
                        throw new ArgumentException("Invalid WAV data chunk.", nameof(wave));
                    dataOffset = pos + 8;
                    dataSize = (int)size;
                    hasData = true;
                }
                pos = (int)next;
            }
            if (!hasFormat || !hasData)
                throw new ArgumentException("WAV fmt or data chunk missing.", nameof(wave));

            int sourceFrameBytes = channels * (sourceBits / 8);
            if (dataSize % sourceFrameBytes != 0 ||
                dataSize > sourceRate * MaxDurationSeconds * sourceFrameBytes)
                throw new ArgumentException("WAV data exceeds two seconds or has an incomplete frame.", nameof(wave));

            if (sourceRate != SampleRate)
                return ResampleToStereo16(wave, dataOffset, dataSize, channels, sourceRate, sourceBits);

            if (channels == 2 && sourceBits == 16)
            {
                byte[] stereo = new byte[dataSize];
                Array.Copy(wave, dataOffset, stereo, 0, dataSize);
                return stereo;
            }

            if (sourceBits == 8)
            {
                // WAV PCM8 is unsigned, with silence at 128. Convert to signed
                // PCM16 before handing frames to the stock HDA output.
                int frames = dataSize / sourceFrameBytes;
                byte[] expanded = new byte[frames * FrameBytes];
                for (int frame = 0; frame < frames; frame++)
                {
                    for (int channel = 0; channel < 2; channel++)
                    {
                        int sourceChannel = channels == 1 ? 0 : channel;
                        int sample = (wave[dataOffset + frame * sourceFrameBytes + sourceChannel] - 128) << 8;
                        int dst = frame * FrameBytes + channel * 2;
                        expanded[dst] = (byte)sample;
                        expanded[dst + 1] = (byte)(sample >> 8);
                    }
                }
                return expanded;
            }

            // Mono PCM16 is duplicated to left and right, without resampling.
            // The two-second bound above limits this output to MaxDataBytes.
            byte[] upmixed = new byte[dataSize * 2];
            for (int i = 0; i < dataSize; i += 2)
            {
                int dst = i * 2;
                byte lo = wave[dataOffset + i];
                byte hi = wave[dataOffset + i + 1];
                upmixed[dst] = lo;
                upmixed[dst + 1] = hi;
                upmixed[dst + 2] = lo;
                upmixed[dst + 3] = hi;
            }
            return upmixed;
        }

        /// <summary>
        /// Convert 44.1 kHz PCM16 mono/stereo to 48 kHz stereo by bounded linear
        /// interpolation. The input is a validated RIFF data chunk; no IO or HAL.
        /// Integer arithmetic keeps the two channels phase-aligned.
        /// </summary>
        private static int ReadSourceSample16(byte[] wave, int offset, int bits)
        {
            return bits == 8 ? (wave[offset] - 128) << 8 : (short)Read16(wave, offset);
        }

        private static byte[] ResampleToStereo16(byte[] wave, int offset, int dataSize,
            int channels, int sourceRate, int sourceBits)
        {
            int sourceFrameBytes = channels * (sourceBits / 8);
            int sourceFrames = dataSize / sourceFrameBytes;
            int targetFrames = (int)(((long)sourceFrames * SampleRate + sourceRate - 1) / sourceRate);
            if (targetFrames <= 0 || targetFrames > SampleRate * MaxDurationSeconds)
                throw new ArgumentException("Resampled WAV exceeds two seconds.", nameof(wave));

            byte[] output = new byte[targetFrames * FrameBytes];
            for (int frame = 0; frame < targetFrames; frame++)
            {
                long position = (long)frame * sourceRate;
                int firstFrame = (int)(position / SampleRate);
                int remainder = (int)(position % SampleRate);
                if (firstFrame >= sourceFrames) firstFrame = sourceFrames - 1;
                int nextFrame = firstFrame + 1 < sourceFrames ? firstFrame + 1 : firstFrame;
                for (int channel = 0; channel < 2; channel++)
                {
                    int sourceChannel = channels == 1 ? 0 : channel;
                    int a = offset + firstFrame * sourceFrameBytes + sourceChannel * (sourceBits / 8);
                    int b = offset + nextFrame * sourceFrameBytes + sourceChannel * (sourceBits / 8);
                    int first = ReadSourceSample16(wave, a, sourceBits);
                    int next = ReadSourceSample16(wave, b, sourceBits);
                    int sample = (int)(((long)first * (SampleRate - remainder) +
                        (long)next * remainder) / SampleRate);
                    int dst = frame * FrameBytes + channel * 2;
                    output[dst] = (byte)sample;
                    output[dst + 1] = (byte)(sample >> 8);
                }
            }
            return output;
        }

        /// <summary>Build a bounded mono PCM16 RIFF fixture without disk access.</summary>
        public static byte[] EncodeMono16(byte[] pcm)
        {
            if (pcm == null || pcm.Length == 0 ||
                pcm.Length > SampleRate * MaxDurationSeconds * 2 || pcm.Length % 2 != 0)
                throw new ArgumentException("PCM must be nonempty, mono16 and at most 2 seconds.", nameof(pcm));
            byte[] wave = new byte[44 + pcm.Length];
            PutTag(wave, 0, "RIFF");
            Write32(wave, 4, wave.Length - 8);
            PutTag(wave, 8, "WAVE");
            PutTag(wave, 12, "fmt ");
            Write32(wave, 16, 16);
            Write16(wave, 20, 1);
            Write16(wave, 22, 1);
            Write32(wave, 24, SampleRate);
            Write32(wave, 28, SampleRate * 2);
            Write16(wave, 32, 2);
            Write16(wave, 34, 16);
            PutTag(wave, 36, "data");
            Write32(wave, 40, pcm.Length);
            Array.Copy(pcm, 0, wave, 44, pcm.Length);
            return wave;
        }

        /// <summary>Build a bounded unsigned PCM8 mono/stereo WAV fixture.</summary>
        public static byte[] EncodePcm8AtRate(byte[] pcm, int sampleRate, int channels)
        {
            if (sampleRate != 44100 && sampleRate != SampleRate)
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (channels != 1 && channels != 2)
                throw new ArgumentOutOfRangeException(nameof(channels));
            if (pcm == null || pcm.Length == 0 ||
                pcm.Length > sampleRate * MaxDurationSeconds * channels ||
                pcm.Length % channels != 0)
                throw new ArgumentException("Invalid PCM8 duration or frame alignment.", nameof(pcm));

            // RIFF chunks are padded to an even boundary, but the data chunk
            // length itself excludes the pad byte.
            byte[] wave = new byte[44 + pcm.Length + (pcm.Length & 1)];
            PutTag(wave, 0, "RIFF");
            Write32(wave, 4, wave.Length - 8);
            PutTag(wave, 8, "WAVE");
            PutTag(wave, 12, "fmt ");
            Write32(wave, 16, 16);
            Write16(wave, 20, 1);
            Write16(wave, 22, channels);
            Write32(wave, 24, sampleRate);
            Write32(wave, 28, sampleRate * channels);
            Write16(wave, 32, channels);
            Write16(wave, 34, 8);
            PutTag(wave, 36, "data");
            Write32(wave, 40, pcm.Length);
            Array.Copy(pcm, 0, wave, 44, pcm.Length);
            return wave;
        }

        /// <summary>
        /// Construct a 44.1/48 kHz stereo PCM16 WAV fixture for the resampler
        /// test. This helper never touches a guest or host filesystem.
        /// </summary>
        public static byte[] EncodeStereo16AtRate(byte[] pcm, int sampleRate)
        {
            if (sampleRate != 44100 && sampleRate != SampleRate)
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (pcm == null || pcm.Length == 0 ||
                pcm.Length > sampleRate * MaxDurationSeconds * FrameBytes ||
                pcm.Length % FrameBytes != 0)
                throw new ArgumentException("Invalid PCM16 stereo duration or alignment.", nameof(pcm));
            byte[] wave = EncodeStereo16(pcm);
            Write32(wave, 24, sampleRate);
            Write32(wave, 28, sampleRate * FrameBytes);
            return wave;
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
