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
            if (stereo16 == null || stereo16.Length == 0 ||
                stereo16.Length % 4 != 0 || stereo16.Length > 48000 * 2 * 4)
                throw new ArgumentException(
                    "Bounded, frame-aligned stereo16 PCM required.", nameof(stereo16));

            byte[] monoFloat = new byte[stereo16.Length];
            for (int at = 0; at < stereo16.Length; at += 4)
            {
                short sample = (short)(stereo16[at] | (stereo16[at + 1] << 8));
                // Little-endian 0x3E000000 / 0xBE000000 = +/-0.125f.
                // A zero sample must remain four zero bytes, especially
                // in the 16 KiB silent HDA drain appended to test tones.
                if (sample != 0)
                    monoFloat[at + 3] = sample < 0 ? (byte)0xBE : (byte)0x3E;
            }
            return monoFloat;
        }
    }
}
