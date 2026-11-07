using System;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// RAM-only fixture conversion for the opt-in IEEE float32 HDA probes.
    /// Maps the sign of a stereo16 tone's left channel to +/-0.125f mono;
    /// exact PCM silence remains 0.0f. This is not a general-purpose
    /// PCM16-to-float converter or a hardware codec operation.
    /// </summary>
    public static class AudioFloatToneFixture
    {
        public static byte[] FromStereo16Left(byte[] stereo16)
        {
            return FromStereo16LeftWithSilentTail(stereo16, 0);
        }

        /// <summary>
        /// Append up to one 16 KiB HDA ring of exact zero float samples
        /// to the RAM-only mono fixture. A playback driver's later EOF drain
        /// is not part of the WAV data, so QEMU cannot verify that drain
        /// unless the source itself includes it. Allocate only the final
        /// mono float buffer, without an intermediate padded stereo copy.
        /// </summary>
        public static byte[] FromStereo16LeftWithSilentTail(
            byte[] stereo16, int silentFrames)
        {
            const int maxFrames = 48000 * 2;
            const int maxTailFrames = 4096;
            if (silentFrames < 0 || silentFrames > maxTailFrames)
                throw new ArgumentOutOfRangeException(nameof(silentFrames));
            if (stereo16 == null || stereo16.Length == 0 ||
                stereo16.Length % 4 != 0 ||
                stereo16.Length / 4 > maxFrames - silentFrames)
                throw new ArgumentException(
                    "Bounded, frame-aligned stereo16 PCM required.", nameof(stereo16));

            // New arrays are zero-initialized: the tail is exactly +0.0f.
            byte[] monoFloat = new byte[stereo16.Length + silentFrames * 4];
            for (int at = 0; at < stereo16.Length; at += 4)
            {
                short sample = (short)(stereo16[at] | (stereo16[at + 1] << 8));
                // Little-endian 0x3E000000 / 0xBE000000 = +/-0.125f.
                // A zero sample must remain four zero bytes.
                if (sample != 0)
                    monoFloat[at + 3] = sample < 0 ? (byte)0xBE : (byte)0x3E;
            }
            return monoFloat;
        }
    }
}
