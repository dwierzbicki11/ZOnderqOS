using System;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Strict, allocation-bounded RIFF/WAVE PCM8/PCM16/PCM24/PCM32 mono/stereo decoder for the
    /// opt-in diskless HDA probe. No filesystem, DMA or device access.
    /// 8/16/22.05/24/32/44.1 kHz sources are explicitly resampled to 48 kHz stereo PCM16.
    /// </summary>
    public static class AudioWavePcm
    {
        public const int SampleRate = 48000;
        public const int MaxDurationSeconds = 2;
        // Apply the same upper bound to direct byte[] callers as the read-only
        // stream loader. Large metadata-only RIFFs must not be scanned.
        public const int MaxRiffBytes = 800 * 1024;
        private const int FrameBytes = 4;
        // Largest supported source: two seconds of 48 kHz stereo signed PCM32.
        // Decoded output always remains bounded by two seconds of stereo PCM16.
        private const int MaxDataBytes = SampleRate * MaxDurationSeconds * 2 * 4;
        private const int MaxOutputBytes = SampleRate * MaxDurationSeconds * FrameBytes;

        // Supported uncompressed WAV input rates; HDA always receives 48 kHz PCM16.
        // The existing bounded integer resampler handles all of these rates.
        private static bool IsSupportedSourceRate(int rate)
        {
            return rate == 8000 || rate == 16000 || rate == 22050 || rate == 24000 ||
                rate == 32000 || rate == 44100 || rate == SampleRate;
        }

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

            if (wave.Length > MaxRiffBytes)
                throw new ArgumentException("WAV exceeds bounded RIFF file limit.", nameof(wave));

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
            int sourceValidBits = 0;
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
                    int validBits = bits;
                    if (format == 0xFFFE)
                    {
                        // Extensible PCM may store 16/20/24 valid bits in a
                        // larger 24/32-bit container, left-aligned. Keep
                        // PCM8/PCM16 exact and refuse sub-16-bit precision.
                        // Check chunk bounds before reading extension fields: a
                        // short fmt chunk may be the last bytes of the file.
                        if (size < 40 || Read16(wave, pos + 24) < 22 ||
                            (long)Read16(wave, pos + 24) + 18 > size)
                            throw new ArgumentException("Invalid WAV extensible header.", nameof(wave));
                        validBits = Read16(wave, pos + 26);
                        if (validBits != bits && !(bits >= 24 && validBits >= 16 && validBits < bits))
                            throw new ArgumentException("Invalid WAV extensible valid-bit width.", nameof(wave));

                        uint speakerMask = Read32(wave, pos + 28);
                        if (speakerMask != 0 &&
                            speakerMask != (sourceChannels == 1 ? 0x4U : 0x3U))
                            throw new ArgumentException("Unsupported WAV speaker layout.", nameof(wave));

                        // KSDATAFORMAT_SUBTYPE_PCM:
                        // 00000001-0000-0010-8000-00AA00389B71 (LE bytes).
                        if (Read32(wave, pos + 32) != 1U ||
                            Read16(wave, pos + 36) != 0 ||
                            Read16(wave, pos + 38) != 0x0010 ||
                            wave[pos + 40] != 0x80 || wave[pos + 41] != 0 ||
                            wave[pos + 42] != 0 || wave[pos + 43] != 0xAA ||
                            wave[pos + 44] != 0 || wave[pos + 45] != 0x38 ||
                            wave[pos + 46] != 0x9B || wave[pos + 47] != 0x71)
                            throw new ArgumentException("WAV extensible subtype is not PCM.", nameof(wave));
                        format = 1;
                    }
                    if (format != 1 || (sourceChannels != 1 && sourceChannels != 2) ||
                        !IsSupportedSourceRate((int)rate) || (bits != 8 && bits != 16 && bits != 24 && bits != 32) ||
                        blockAlign != sourceChannels * (bits / 8) ||
                        byteRate != rate * sourceChannels * (uint)(bits / 8))
                        throw new ArgumentException("Unsupported WAV format (requires 8/16/22.05/24/32/44.1/48kHz PCM8/PCM16/PCM24/PCM32 mono/stereo).", nameof(wave));
                    channels = sourceChannels;
                    sourceRate = (int)rate;
                    sourceBits = bits;
                    sourceValidBits = validBits;
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

            // Extensible PCM's unused least-significant container bits must
            // be zero. Validate before any conversion, including resampling,
            // so malformed 20-in-24 / 24-in-32 samples are not silently played.
            int unusedBits = sourceBits - sourceValidBits;
            if (unusedBits > 0)
            {
                int unusedMask = (1 << unusedBits) - 1; // at most 16 bits
                int sampleBytes = sourceBits / 8;
                for (int at = dataOffset; at < dataOffset + dataSize; at += sampleBytes)
                {
                    int low = wave[at] | (sampleBytes == 4 ? wave[at + 1] << 8 : 0);
                    if ((low & unusedMask) != 0)
                        throw new ArgumentException("Nonzero WAV extensible padding bits.", nameof(wave));
                }
            }

            if (sourceRate != SampleRate)
                return ResampleToStereo16(wave, dataOffset, dataSize, channels, sourceRate, sourceBits);

            if (channels == 2 && sourceBits == 16)
            {
                byte[] stereo = new byte[dataSize];
                Array.Copy(wave, dataOffset, stereo, 0, dataSize);
                return stereo;
            }

            if (sourceBits == 24 || sourceBits == 32)
            {
                // Signed little-endian PCM24/PCM32 -> PCM16: retain the
                // most significant two bytes, preserving the sign and channels.
                int frames = dataSize / sourceFrameBytes;
                byte[] downmixed = new byte[frames * FrameBytes];
                for (int frame = 0; frame < frames; frame++)
                {
                    for (int channel = 0; channel < 2; channel++)
                    {
                        int sourceChannel = channels == 1 ? 0 : channel;
                        int src = dataOffset + frame * sourceFrameBytes + sourceChannel * (sourceBits / 8);
                        int dst = frame * FrameBytes + channel * 2;
                        int high = sourceBits == 24 ? 1 : 2;
                        downmixed[dst] = wave[src + high];
                        downmixed[dst + 1] = wave[src + high + 1];
                    }
                }
                return downmixed;
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
        /// Convert supported non-48 kHz PCM mono/stereo to 48 kHz stereo by bounded linear
        /// interpolation. The input is a validated RIFF data chunk; no IO or HAL.
        /// Integer arithmetic keeps the two channels phase-aligned.
        /// </summary>
        private static int ReadSourceSample16(byte[] wave, int offset, int bits)
        {
            if (bits == 8) return (wave[offset] - 128) << 8;
            if (bits == 24) return (short)(wave[offset + 1] | (wave[offset + 2] << 8));
            if (bits == 32) return (short)(wave[offset + 2] | (wave[offset + 3] << 8));
            return (short)Read16(wave, offset);
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

        /// <summary>
        /// Convert an already validated canonical PCM fixture into extensible
        /// RIFF, preserving the data chunk and any even-boundary padding.
        /// This helper never opens a filesystem or touches a device.
        /// </summary>
        private static byte[] ToExtensiblePcm(byte[] basic, int channels, int validBits)
        {
            byte[] wave = new byte[basic.Length + 24];
            Array.Copy(basic, 0, wave, 0, 36);
            Array.Copy(basic, 36, wave, 60, basic.Length - 36);
            Write32(wave, 4, wave.Length - 8);
            Write32(wave, 16, 40); // extended fmt chunk size
            Write16(wave, 20, 0xFFFE);
            Write16(wave, 36, 22); // cbSize
            Write16(wave, 38, validBits);
            Write32(wave, 40, channels == 1 ? 0x4 : 0x3);
            Write32(wave, 44, 1); // PCM subtype GUID
            Write16(wave, 48, 0);
            Write16(wave, 50, 0x0010);
            wave[52] = 0x80;
            wave[55] = 0xAA;
            wave[57] = 0x38;
            wave[58] = 0x9B;
            wave[59] = 0x71;
            return wave;
        }

        public static byte[] EncodeExtensibleStereo16AtRate(byte[] pcm, int sampleRate)
        {
            return ToExtensiblePcm(EncodeStereo16AtRate(pcm, sampleRate), 2, 16);
        }

        /// <summary>Build 16/20/24 valid-bit PCM in a signed 24-bit container.</summary>
        public static byte[] EncodeExtensiblePcm24AtRate(byte[] pcm, int sampleRate, int channels, int validBits)
        {
            if (validBits != 16 && validBits != 20 && validBits != 24)
                throw new ArgumentOutOfRangeException(nameof(validBits));
            return ToExtensiblePcm(EncodePcm24AtRate(pcm, sampleRate, channels), channels, validBits);
        }

        /// <summary>Build 16/24/32 valid-bit PCM in a signed 32-bit container.</summary>
        public static byte[] EncodeExtensiblePcm32AtRate(byte[] pcm, int sampleRate, int channels, int validBits)
        {
            if (validBits != 16 && validBits != 24 && validBits != 32)
                throw new ArgumentOutOfRangeException(nameof(validBits));
            return ToExtensiblePcm(EncodePcm32AtRate(pcm, sampleRate, channels), channels, validBits);
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

        /// <summary>Build a bounded signed PCM24 mono/stereo WAV fixture.</summary>
        public static byte[] EncodePcm24AtRate(byte[] pcm, int sampleRate, int channels)
        {
            if (!IsSupportedSourceRate(sampleRate))
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (channels != 1 && channels != 2)
                throw new ArgumentOutOfRangeException(nameof(channels));
            int frameBytes = channels * 3;
            if (pcm == null || pcm.Length == 0 ||
                pcm.Length > sampleRate * MaxDurationSeconds * frameBytes ||
                pcm.Length % frameBytes != 0)
                throw new ArgumentException("Invalid PCM24 duration or frame alignment.", nameof(pcm));

            byte[] wave = new byte[44 + pcm.Length + (pcm.Length & 1)];
            PutTag(wave, 0, "RIFF");
            Write32(wave, 4, wave.Length - 8);
            PutTag(wave, 8, "WAVE");
            PutTag(wave, 12, "fmt ");
            Write32(wave, 16, 16);
            Write16(wave, 20, 1);
            Write16(wave, 22, channels);
            Write32(wave, 24, sampleRate);
            Write32(wave, 28, sampleRate * frameBytes);
            Write16(wave, 32, frameBytes);
            Write16(wave, 34, 24);
            PutTag(wave, 36, "data");
            Write32(wave, 40, pcm.Length);
            Array.Copy(pcm, 0, wave, 44, pcm.Length);
            return wave;
        }

        /// <summary>Build a bounded signed PCM32 mono/stereo WAV fixture.</summary>
        public static byte[] EncodePcm32AtRate(byte[] pcm, int sampleRate, int channels)
        {
            if (!IsSupportedSourceRate(sampleRate))
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (channels != 1 && channels != 2)
                throw new ArgumentOutOfRangeException(nameof(channels));
            int frameBytes = channels * 4;
            if (pcm == null || pcm.Length == 0 ||
                pcm.Length > sampleRate * MaxDurationSeconds * frameBytes ||
                pcm.Length % frameBytes != 0)
                throw new ArgumentException("Invalid PCM32 duration or frame alignment.", nameof(pcm));

            byte[] wave = new byte[44 + pcm.Length];
            PutTag(wave, 0, "RIFF");
            Write32(wave, 4, wave.Length - 8);
            PutTag(wave, 8, "WAVE");
            PutTag(wave, 12, "fmt ");
            Write32(wave, 16, 16);
            Write16(wave, 20, 1);
            Write16(wave, 22, channels);
            Write32(wave, 24, sampleRate);
            Write32(wave, 28, sampleRate * frameBytes);
            Write16(wave, 32, frameBytes);
            Write16(wave, 34, 32);
            PutTag(wave, 36, "data");
            Write32(wave, 40, pcm.Length);
            Array.Copy(pcm, 0, wave, 44, pcm.Length);
            return wave;
        }

        /// <summary>Build a bounded unsigned PCM8 mono/stereo WAV fixture.</summary>
        public static byte[] EncodePcm8AtRate(byte[] pcm, int sampleRate, int channels)
        {
            if (!IsSupportedSourceRate(sampleRate))
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
        /// Construct a supported-rate stereo PCM16 WAV fixture for the resampler
        /// test. This helper never touches a guest or host filesystem.
        /// </summary>
        public static byte[] EncodeStereo16AtRate(byte[] pcm, int sampleRate)
        {
            if (!IsSupportedSourceRate(sampleRate))
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
            if (pcm == null || pcm.Length == 0 || pcm.Length > MaxOutputBytes ||
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
