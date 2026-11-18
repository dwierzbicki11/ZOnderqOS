using System;
using System.IO;
using System.Linq;
using ZonderqOS.SystemCore;

static class Test
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Main()
    {
        var pcm = AudioTonePcm.GenerateSquareStereo16(48000, 440, 250, 4096);
        Check(pcm.Length == 48000, "Unexpected PCM duration");
        var wav = AudioWavePcm.EncodeStereo16(pcm);
        Check(wav.Length > pcm.Length, "RIFF header missing");
        var decoded = AudioWavePcm.DecodeStereo16(wav);
        Check(decoded.SequenceEqual(pcm), "WAV byte array roundtrip");
        using (var stream = new MemoryStream(wav, writable: false))
            Check(AudioWaveInput.DecodeBounded(stream).SequenceEqual(pcm), "Read-only WAV stream roundtrip");
        Check(AudioPcmVolume.ScaleStereo16(pcm, 0).All(b => b == 0), "Mute must output silence");
        Check(AudioPcmVolume.ScaleStereo16(pcm, 100).SequenceEqual(pcm), "100% volume changes signal");
        var mixed = AudioPcmMixer.MixStereo16(pcm, pcm, 50, 50);
        Check(mixed.Length == pcm.Length, "Mixer output length");
        Check(mixed.SequenceEqual(pcm) || mixed.Any(b => b != 0), "Mixer unexpectedly silent");
        AudioPcmVolume.ResolveStereoGains(100, 100, out var left, out var right);
        Check(left == 0 && right == 100, "Balance 100% right");
        bool rejected = false;
        try { AudioWavePcm.DecodeStereo16(new byte[12]); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "Malformed RIFF must be rejected");
        Console.WriteLine("GEN3-AUDIO-3097 PASS: PCM generation, WAV, stream, gain, balance, mixer, invalid RIFF");
    }
}
