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

    private static void Write32(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    public static void Run(Action<bool, string> expect)
    {
        // The actual QEMU tone fixture must not turn its silent HDA
        // drain into +0.125f DC. Exercise the production conversion helper
        // independently of the device, including sign and ownership.
        byte[] fixtureSource = { 0, 0, 0, 0, 0, 0x10, 0, 0, 0, 0xF0, 0, 0 };
        byte[] fixtureOriginal = (byte[])fixtureSource.Clone();
        byte[] fixtureFloat = AudioFloatToneFixture.FromStereo16Left(fixtureSource);
        expect(fixtureFloat.Length == 12 &&
            fixtureFloat[3] == 0 && fixtureFloat[7] == 0x3E &&
            fixtureFloat[11] == 0xBE,
            "float fixture preserves zero, positive and negative samples");
        for (int i = 0; i < fixtureFloat.Length; i++)
        {
            if (i != 7 && i != 11)
                expect(fixtureFloat[i] == 0, "float fixture little-endian zero bytes");
            expect(fixtureSource[i] == fixtureOriginal[i],
                "float fixture never modifies source PCM");
        }
        // Regression: the real wavfloat/wavextfloat commands must append
        // silence to their WAV payload, not rely on AudioPlayer's EOF drain.
        // The previous runtime sent a 250ms tone only; QEMU captured a
        // non-silent final 20ms despite the decoder passing host tests.
        byte[] tone = AudioTonePcm.GenerateSquareStereo16(
            48000, 2860, 250, 4096);
        byte[] toneOriginal = (byte[])tone.Clone();
        byte[] floatWithDrain =
            AudioFloatToneFixture.FromStereo16LeftWithSilentTail(tone, 4096);
        expect(floatWithDrain.Length == tone.Length + 16 * 1024 &&
            floatWithDrain[3] == 0x3E &&
            floatWithDrain[tone.Length - 1] == 0xBE,
            "float HDA fixture retains tone before silent drain");
        for (int i = 0; i < tone.Length; i++)
            expect(tone[i] == toneOriginal[i],
                "float HDA fixture leaves original PCM unchanged");
        for (int i = tone.Length; i < floatWithDrain.Length; i++)
            expect(floatWithDrain[i] == 0, "float HDA 16KiB drain stays silent");
        byte[] drainedWave = AudioWavePcm.EncodeFloat32AtRate(
            floatWithDrain, 48000, 1);
        byte[] drainedPcm = AudioWavePcm.DecodeStereo16(drainedWave);
        expect(drainedPcm.Length == floatWithDrain.Length &&
            Sample(drainedPcm, 0) == 4095 &&
            Sample(drainedPcm, tone.Length / 2 - 2) < 0,
            "canonical float WAV keeps tone before silent drain");
        for (int i = tone.Length; i < drainedPcm.Length; i++)
            expect(drainedPcm[i] == 0,
                "canonical float WAV decodes silent HDA drain to exact PCM zero");

        byte[] source44100 = AudioTonePcm.GenerateSquareStereo16(
            44100, 3960, 250, 4096);
        byte[] extensibleFloat =
            AudioFloatToneFixture.FromStereo16LeftWithSilentTail(source44100, 4096);
        byte[] extensibleDrain = AudioWavePcm.EncodeExtensibleFloat32AtRate(
            extensibleFloat, 44100, 1);
        byte[] resampledDrain = AudioWavePcm.DecodeStereo16(extensibleDrain);
        expect(extensibleFloat.Length == source44100.Length + 16 * 1024 &&
            resampledDrain.Length > extensibleFloat.Length &&
            Sample(resampledDrain, 0) == 4095,
            "extensible float WAV resamples tone and drain to 48 kHz");
        for (int i = resampledDrain.Length - 48000 / 50 * 4;
             i < resampledDrain.Length; i++)
            expect(resampledDrain[i] == 0,
                "extensible float 44.1kHz drain ends with 20ms exact PCM silence");
        bool fixtureRejected = false;
        try { AudioFloatToneFixture.FromStereo16Left(new byte[3]); }
        catch (ArgumentException) { fixtureRejected = true; }
        expect(fixtureRejected, "reject unaligned float fixture PCM");
        fixtureRejected = false;
        try { AudioFloatToneFixture.FromStereo16Left(new byte[384004]); }
        catch (ArgumentException) { fixtureRejected = true; }
        expect(fixtureRejected, "reject over-two-second float fixture PCM");
        fixtureRejected = false;
        try { AudioFloatToneFixture.FromStereo16LeftWithSilentTail(tone, -1); }
        catch (ArgumentOutOfRangeException) { fixtureRejected = true; }
        expect(fixtureRejected, "reject negative float fixture drain");
        fixtureRejected = false;
        try { AudioFloatToneFixture.FromStereo16LeftWithSilentTail(tone, 4097); }
        catch (ArgumentOutOfRangeException) { fixtureRejected = true; }
        expect(fixtureRejected, "reject oversized float fixture drain");
        fixtureRejected = false;
        try
        {
            AudioFloatToneFixture.FromStereo16LeftWithSilentTail(
                new byte[48000 * 2 * 4], 4096);
        }
        catch (ArgumentException) { fixtureRejected = true; }
        expect(fixtureRejected, "reject float fixture exceeding two-second PCM budget");

        // Canonical IEEE_FLOAT, 48kHz stereo: independent channels,
        // sign, asymmetrical PCM16 endpoints and finite clipping.
        byte[] samples = Floats(0f, 0.5f, -0.5f, 1f, -1f, 2f, -2f, -0.25f);
        byte[] unchanged = (byte[])samples.Clone();
        byte[] wave = AudioWavePcm.EncodeFloat32AtRate(samples, 48000, 2);
        expect(wave[20] == 3 && wave[21] == 0, "float WAV uses IEEE format tag 3");
        expect(wave.Length == samples.Length + 58, "IEEE float WAV contains WAVEFORMATEX and fact");
        expect(wave[16] == 18 && wave[36] == 0 && wave[37] == 0,
            "IEEE float fmt has 18 bytes and zero extension");
        expect(wave[38] == 'f' && wave[39] == 'a' && wave[40] == 'c' && wave[41] == 't' &&
            wave[46] == 4 && wave[47] == 0, "IEEE float fact records four frames");
        expect(wave[50] == 'd' && wave[51] == 'a' && wave[52] == 't' && wave[53] == 'a',
            "IEEE float data follows fact metadata");
        // Validate RIFF fact metadata rather than trusting a self-inconsistent
        // IEEE_FLOAT frame count. A missing fact remains accepted for legacy
        // files; when present, it must be unique, complete and correct.
        byte[] wrongFact = (byte[])wave.Clone();
        Write32(wrongFact, 46, 3);
        expect(Rejects(wrongFact), "reject IEEE float fact with wrong frame count");
        Write32(wrongFact, 46, 5);
        expect(Rejects(wrongFact), "reject IEEE float fact with excess frame count");

        byte[] duplicateFact = new byte[wave.Length + 12];
        Array.Copy(wave, duplicateFact, wave.Length);
        duplicateFact[wave.Length] = (byte)'f';
        duplicateFact[wave.Length + 1] = (byte)'a';
        duplicateFact[wave.Length + 2] = (byte)'c';
        duplicateFact[wave.Length + 3] = (byte)'t';
        Write32(duplicateFact, wave.Length + 4, 4);
        Write32(duplicateFact, wave.Length + 8, 4);
        Write32(duplicateFact, 4, duplicateFact.Length - 8);
        expect(Rejects(duplicateFact), "reject duplicate IEEE float fact chunk");

        byte[] shortFact = new byte[wave.Length - 2];
        Array.Copy(wave, 0, shortFact, 0, 48);
        Array.Copy(wave, 50, shortFact, 48, wave.Length - 50);
        Write32(shortFact, 42, 2);
        Write32(shortFact, 4, shortFact.Length - 8);
        expect(Rejects(shortFact), "reject truncated IEEE float fact payload");

        byte[] noFact = new byte[wave.Length - 12];
        Array.Copy(wave, 0, noFact, 0, 38);
        Array.Copy(wave, 50, noFact, 38, wave.Length - 50);
        Write32(noFact, 4, noFact.Length - 8);
        expect(!Rejects(noFact), "accept legacy IEEE float WAV without fact");

        byte[] shortFmt = (byte[])wave.Clone();
        Write32(shortFmt, 16, 17);
        expect(Rejects(shortFmt), "reject 17-byte WAVEFORMATEX fmt chunk");
        byte[] oversizedFmtExtension = (byte[])wave.Clone();
        oversizedFmtExtension[36] = 1;
        expect(Rejects(oversizedFmtExtension), "reject WAVEFORMATEX cbSize exceeding fmt chunk");

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

        // WAVE_FORMAT_EXTENSIBLE IEEE_FLOAT is common in DAW exports.
        // Its exact float subtype GUID must be recognized, while integer
        // PCM and unrelated GUIDs must never be reinterpreted as float.
        byte[] ext = AudioWavePcm.EncodeExtensibleFloat32AtRate(samples, 48000, 2);
        expect(ext.Length == wave.Length + 22 && ext[16] == 40 &&
            ext[20] == 0xFE && ext[21] == 0xFF &&
            ext[36] == 22 && ext[38] == 32 && ext[44] == 3,
            "extensible float32 fmt has 40 bytes and subtype 3");
        expect(ext[60] == 'f' && ext[61] == 'a' &&
            ext[72] == 'd' && ext[73] == 'a',
            "extensible float retains fact and data chunks");
        byte[] extDecoded = AudioWavePcm.DecodeStereo16(ext);
        expect(extDecoded.Length == decoded.Length, "extensible float decoded duration");
        for (int i = 0; i < decoded.Length; i++)
            expect(extDecoded[i] == decoded[i],
                "extensible float samples match canonical float");
        using (var extInput = new MemoryStream(ext, false))
        {
            byte[] extStream = AudioWaveInput.DecodeBounded(extInput);
            for (int i = 0; i < extDecoded.Length; i++)
                expect(extStream[i] == extDecoded[i],
                    "read-only extensible float stream matches decoded PCM");
        }

        byte[] extMono44 = AudioWavePcm.EncodeExtensibleFloat32AtRate(
            Floats(0.5f, -0.5f), 44100, 1);
        byte[] extResampled = AudioWavePcm.DecodeStereo16(extMono44);
        expect(extResampled.Length == upmixed.Length,
            "extensible float mono 44.1kHz resampled duration");
        for (int i = 0; i < upmixed.Length; i++)
            expect(extResampled[i] == upmixed[i],
                "extensible float resampler matches canonical float");

        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            expect(Rejects(AudioWavePcm.EncodeExtensibleFloat32AtRate(
                Floats(invalid), 48000, 1)),
                "reject non-finite extensible float32");
        }
        byte[] badExt = (byte[])ext.Clone();
        badExt[38] = 24; // Float has no integer-style 24 valid bits.
        expect(Rejects(badExt), "reject extensible float32 with 24 valid bits");
        badExt = (byte[])ext.Clone();
        badExt[34] = 24; // 24-bit container contradicts float32.
        expect(Rejects(badExt), "reject extensible float with non-32-bit container");
        badExt = (byte[])ext.Clone();
        badExt[44] = 4; // Unknown subtype GUID.
        expect(Rejects(badExt), "reject unknown extensible float subtype");
        badExt = (byte[])ext.Clone();
        badExt[55] = 0; // Corrupt canonical GUID tail.
        expect(Rejects(badExt), "reject damaged extensible float GUID tail");
        badExt = (byte[])ext.Clone();
        badExt[40] = 4; // Mono mask is invalid for stereo.
        expect(Rejects(badExt), "reject invalid extensible float speaker mask");
        badExt = (byte[])ext.Clone();
        Write32(badExt, 68, 3); // fact: expected four frames.
        expect(Rejects(badExt), "reject inconsistent extensible float fact count");

        byte[] extPcm = AudioWavePcm.EncodeExtensiblePcm32AtRate(
            Floats(0.5f), 48000, 1, 32);
        byte[] pcmInterpreted = AudioWavePcm.DecodeStereo16(extPcm);
        expect(pcmInterpreted.Length == 4 &&
            Sample(pcmInterpreted, 0) != 16383,
            "integer PCM subtype is never interpreted as IEEE float");

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
