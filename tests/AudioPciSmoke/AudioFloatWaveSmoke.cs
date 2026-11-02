using System;
using System.IO;
using ZonderqOS.SystemCore;

static class AudioFloatWaveSmoke
{
    private static short Sample(byte[] pcm, int index)
    {
        int at = index * 2;
        return (short)(pcm[at] | (pcm[at + 1] << 8));
    }

    private static byte[] Floats(params float[] samples)
    {
        byte[] bytes = new byte[samples.Length * 4];
        for (int i = 0; i < samples.Length; i++)
        {
            byte[] value = BitConverter.GetBytes(samples[i]);
            if (!BitConverter.IsLittleEndian) Array.Reverse(value);
            Array.Copy(value, 0, bytes, i * 4, 4);
        }
        return bytes;
    }

    private static bool Rejects(byte[] wave)
    {
        try { AudioWavePcm.DecodeStereo16(wave); }
        catch (ArgumentException) { return true; }
        return false;
    }

    public static void Run(Action<bool, string> expect)
    {
        // Canonical IEEE_FLOAT, 48kHz stereo: independent channels,
        // sign, asymmetrical PCM16 endpoints and finite clipping.
        byte[] samples = Floats(0f, 0.5f, -0.5f, 1f, -1f, 2f, -2f, -0.25f);
        byte[] unchanged = (byte[])samples.Clone();
        byte[] wave = AudioWavePcm.EncodeFloat32AtRate(samples, 48000, 2);
        expect(wave[20] == 3 && wave[21] == 0, "float WAV uses IEEE format tag 3");
        byte[] decoded = AudioWavePcm.DecodeStereo16(wave);
        short[] expected = { 0, 16383, -16384, 32767, -32768, 32767, -32768, -8192 };
        expect(decoded.Length == expected.Length * 2, "float stereo output frame length");
        for (int i = 0; i < expected.Length; i++)
            expect(Sample(decoded, i) == expected[i], "float stereo sample " + i);
        for (int i = 0; i < samples.Length; i++)
            expect(samples[i] == unchanged[i], "float source remains immutable");

        using (var input = new MemoryStream(wave, false))
        {
            byte[] streamed = AudioWaveInput.DecodeBounded(input);
            expect(streamed.Length == decoded.Length, "read-only float WAV stream length");
            for (int i = 0; i < decoded.Length; i++)
                expect(streamed[i] == decoded[i], "streamed float WAV exact PCM");
        }

        // 44.1 -> 48kHz uses the same bounded linear resampler as integer PCM.
        byte[] mono44 = AudioWavePcm.EncodeFloat32AtRate(
            Floats(0.5f, -0.5f), 44100, 1);
        byte[] upmixed = AudioWavePcm.DecodeStereo16(mono44);
        expect(upmixed.Length == 12, "two 44.1kHz frames become three stereo frames");
        expect(Sample(upmixed, 0) == 16383 && Sample(upmixed, 1) == 16383,
            "first float frame is upmixed bit-exact");
        expect(Sample(upmixed, 4) == Sample(upmixed, 5) && Sample(upmixed, 4) < 0,
            "resampled mono channels remain equal and interpolate sign");

        // Refuse non-finite source samples even when a resampler could skip them.
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            expect(Rejects(AudioWavePcm.EncodeFloat32AtRate(Floats(invalid), 48000, 1)),
                "reject non-finite 48kHz float");
            expect(Rejects(AudioWavePcm.EncodeFloat32AtRate(Floats(invalid), 44100, 1)),
                "reject non-finite resampled float");
        }
        byte[] invalidBits = (byte[])wave.Clone();
        invalidBits[34] = 16;
        expect(Rejects(invalidBits), "reject IEEE float with non-32-bit container");
        byte[] invalidRate = (byte[])wave.Clone();
        invalidRate[28] ^= 1;
        expect(Rejects(invalidRate), "reject inconsistent float byte rate");
        byte[] unknownFormat = (byte[])wave.Clone();
        unknownFormat[20] = 7;
        expect(Rejects(unknownFormat), "reject unsupported float-like format tag");

        // Extensible PCM is not silently interpreted as float. A mismatched
        // subtype GUID must be refused, not sent to HDA as signed PCM.
        byte[] ext = AudioWavePcm.EncodeExtensiblePcm32AtRate(Floats(0f), 48000, 1, 32);
        ext[44] = 3;
        expect(Rejects(ext), "reject unsupported extensible IEEE float GUID");

        bool refused = false;
        try { AudioWavePcm.EncodeFloat32AtRate(new byte[3], 48000, 1); }
        catch (ArgumentException) { refused = true; }
        expect(refused, "reject incomplete float frame");
        refused = false;
        try { AudioWavePcm.EncodeFloat32AtRate(new byte[48000 * 2 * 8 + 8], 48000, 2); }
        catch (ArgumentException) { refused = true; }
        expect(refused, "reject over-two-second float source");

        Console.WriteLine("[AUDIO-FLOAT-HOST] bounded IEEE_FLOAT WAV32 decode/resample/invalid fixtures PASS");
    }
}
