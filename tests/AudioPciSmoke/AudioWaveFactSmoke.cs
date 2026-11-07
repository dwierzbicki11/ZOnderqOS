using System;
using ZonderqOS.SystemCore;

// Real production RIFF decoder, in-memory fixtures only; no HDA or disks.
internal static class AudioWaveFactSmoke
{
    private static void Put32(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    // Insert a well-formed optional fact chunk between canonical fmt and
    // data. fact.dwSampleLength counts frames per channel, not PCM bytes.
    private static byte[] WithFact(byte[] canonical, int frames)
    {
        byte[] wave = new byte[canonical.Length + 12];
        Array.Copy(canonical, 0, wave, 0, 36);
        wave[36] = (byte)'f';
        wave[37] = (byte)'a';
        wave[38] = (byte)'c';
        wave[39] = (byte)'t';
        Put32(wave, 40, 4);
        Put32(wave, 44, frames);
        Array.Copy(canonical, 36, wave, 48, canonical.Length - 36);
        Put32(wave, 4, wave.Length - 8);
        return wave;
    }

    private static void RejectFact(byte[] wave, Action<bool, string> check, string label)
    {
        bool rejected = false;
        try { AudioWavePcm.DecodeStereo16(wave); }
        catch (ArgumentException) { rejected = true; }
        check(rejected, label);
    }

    public static void Run(Action<bool, string> check)
    {
        byte[] stereo = AudioWavePcm.EncodeStereo16(new byte[4 * 4]);
        byte[] validStereo = WithFact(stereo, 4);
        check(AudioWavePcm.DecodeStereo16(validStereo).Length == 16,
            "optional PCM16 fact with four stereo frames accepted");
        RejectFact(WithFact(stereo, 16), check,
            "PCM16 fact counts frames, not interleaved bytes");
        RejectFact(WithFact(stereo, 0), check,
            "PCM16 fact zero must not contradict nonempty data");

        byte[] mono8 = AudioWavePcm.EncodePcm8AtRate(
            new byte[] { 128, 129, 127 }, 48000, 1);
        check(AudioWavePcm.DecodeStereo16(WithFact(mono8, 3)).Length == 12,
            "odd-sized PCM8 mono with fact and RIFF pad accepted");
        RejectFact(WithFact(mono8, 2), check,
            "PCM8 fact mismatch rejected despite odd RIFF padding");

        byte[] mono24 = AudioWavePcm.EncodePcm24AtRate(new byte[6], 48000, 1);
        check(AudioWavePcm.DecodeStereo16(WithFact(mono24, 2)).Length == 8,
            "PCM24 mono fact uses frames, not three-byte samples");
        RejectFact(WithFact(mono24, 6), check,
            "PCM24 fact mismatch rejected");

        byte[] floating = AudioWavePcm.EncodeFloat32AtRate(new byte[16], 48000, 1);
        check(AudioWavePcm.DecodeStereo16(floating).Length == 16,
            "existing IEEE float fact remains accepted");
        Put32(floating, 46, 3); // Canonical float fact.dwSampleLength
        RejectFact(floating, check, "IEEE float fact mismatch still rejected");
    }
}
