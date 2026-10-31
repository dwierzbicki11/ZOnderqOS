using System;
using System.Collections.Generic;
using System.IO;
using ZonderqOS.Hardware;
using ZonderqOS.Platform;
using ZonderqOS.SystemCore;

static class Test
{
    static int checks;

    static void Expect(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("FAIL: " + message);
    }

    static string Capture(Func<bool> action, out bool result)
    {
        TextWriter previous = Console.Out;
        using var buffer = new StringWriter();
        try
        {
            Console.SetOut(buffer);
            result = action();
        }
        finally
        {
            Console.SetOut(previous);
        }
        return buffer.ToString();
    }

    public static void Main()
    {
        Expect(AudioPciDiagnostics.IsAudioClass(0x04, 0x01), "legacy audio");
        Expect(AudioPciDiagnostics.IsAudioClass(0x04, 0x03), "HDA");
        Expect(!AudioPciDiagnostics.IsAudioClass(0x04, 0x00), "video must not be audio");
        Expect(!AudioPciDiagnostics.IsAudioClass(0x02, 0x00), "network must not be audio");
        Expect(!AudioPciDiagnostics.IsAudioClass(0x04, 0x80), "other multimedia not assumed audio");
        Expect(AudioPciDiagnostics.DescribeController(0x04, 0x03, 0x00).Contains("HdAudioDriver"), "HDA candidate");
        Expect(AudioPciDiagnostics.DescribeController(0x04, 0x03, 0x01).Contains("unsupported"), "unknown prog-if");
        Expect(AudioPciDiagnostics.DescribeController(0x04, 0x01, 0x00).Contains("AC97"), "legacy controller");

        PciSysfsProvider.Available = true;
        PciSysfsProvider.Devices = new List<DeviceDescriptor>
        {
            new("00:01.0", 0x8086, 0x2668, 0x04, 0x03, 0x00),
            new("00:02.0", 0x8086, 0x2415, 0x04, 0x01, 0x00),
            new("00:03.0", 0x1234, 0x1111, 0x04, 0x00, 0x00),
            new("00:04.0", 0x8086, 0x10D3, 0x02, 0x00, 0x00)
        };
        string report = Capture(() => AudioPciDiagnostics.PrintReport(false), out bool ok);
        Expect(ok, "PCI enumeration success");
        Expect(report.Contains("Discovered PCI audio functions: 2"), "count only audio");
        Expect(report.Contains("00:01.0") && report.Contains("00:02.0"), "both audio functions");
        Expect(!report.Contains("00:03.0") && !report.Contains("00:04.0"), "exclude video and NIC");
        Expect(report.Contains("driver=UNVERIFIED codec=UNKNOWN pcm=NOT_TESTED"), "no fabricated binding");

        PciSysfsProvider.Devices.Clear();
        report = Capture(() => AudioPciDiagnostics.PrintReport(false), out ok);
        Expect(ok && report.Contains("Discovered PCI audio functions: 0"), "empty scan is valid");

        PciSysfsProvider.Available = false;
        report = Capture(() => AudioPciDiagnostics.PrintReport(false), out ok);
        Expect(!ok && report.Contains("PCI discovery unavailable"), "failed scan not reported as empty success");

        byte[] pcm = AudioTonePcm.GenerateSquareStereo16(48000, 440, 250, 4096);
        Expect(pcm.Length == 48000, "250ms stereo16 48kHz length");
        int transitions = 0;
        short previous = 0;
        for (int i = 0; i < pcm.Length / 4; i++)
        {
            int offset = i * 4;
            short sample = (short)(pcm[offset] | (pcm[offset + 1] << 8));
            short right = (short)(pcm[offset + 2] | (pcm[offset + 3] << 8));
            Expect(sample == right, "stereo channels equal");
            Expect(sample == 4096 || sample == -4096, "bounded PCM amplitude");
            if (i > 0 && (sample < 0) != (previous < 0)) transitions++;
            previous = sample;
        }
        double measuredHz = transitions * 48000.0 / (2 * (pcm.Length / 4 - 1));
        Expect(Math.Abs(measuredHz - 440) < 5, "actual 440Hz square-wave period");
        bool rejected = false;
        try { AudioTonePcm.GenerateSquareStereo16(48000, 24000, 250, 4096); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "Nyquist guard");
        rejected = false;
        try { AudioTonePcm.GenerateSquareStereo16(48000, 440, 3000, 4096); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "duration guard");

        // Pure C# RIFF/WAVE round-trip; no Cosmos HAL or filesystem access.
        byte[] encoded = AudioWavePcm.EncodeStereo16(pcm);
        byte[] decoded = AudioWavePcm.DecodeStereo16(encoded);
        Expect(encoded.Length == pcm.Length + 44, "canonical WAV header length");
        Expect(decoded.Length == pcm.Length, "WAV PCM length");
        for (int i = 0; i < pcm.Length; i++)
            Expect(decoded[i] == pcm[i], "WAV round-trip preserves PCM");

        byte[] corrupt = (byte[])encoded.Clone();
        corrupt[0] = (byte)'X';
        rejected = false;
        try { AudioWavePcm.DecodeStereo16(corrupt); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "reject missing RIFF signature");

        corrupt = (byte[])encoded.Clone();
        corrupt[4] = 0;
        rejected = false;
        try { AudioWavePcm.DecodeStereo16(corrupt); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "reject inconsistent RIFF size");

        corrupt = (byte[])encoded.Clone();
        corrupt[20] = 3; // IEEE float is not signed PCM16.
        rejected = false;
        try { AudioWavePcm.DecodeStereo16(corrupt); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "reject non-PCM format");

        corrupt = (byte[])encoded.Clone();
        corrupt[24] = 0x44; // Not 48 kHz; never silently resample.
        rejected = false;
        try { AudioWavePcm.DecodeStereo16(corrupt); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "reject unsupported sample rate");

        corrupt = (byte[])encoded.Clone();
        corrupt[32] = 2; // Wrong block alignment.
        rejected = false;
        try { AudioWavePcm.DecodeStereo16(corrupt); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "reject invalid frame alignment");

        corrupt = (byte[])encoded.Clone();
        corrupt[40] = 0xff; corrupt[41] = 0xff; corrupt[42] = 0xff; corrupt[43] = 0x7f;
        rejected = false;
        try { AudioWavePcm.DecodeStereo16(corrupt); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "reject overflowing data chunk");

        // Insert a JUNK metadata chunk with odd payload and one pad byte.
        byte[] withMetadata = new byte[encoded.Length + 10];
        Array.Copy(encoded, 0, withMetadata, 0, 36);
        withMetadata[36] = (byte)'J'; withMetadata[37] = (byte)'U';
        withMetadata[38] = (byte)'N'; withMetadata[39] = (byte)'K';
        withMetadata[40] = 1;
        withMetadata[44] = 0x42;
        Array.Copy(encoded, 36, withMetadata, 46, encoded.Length - 36);
        int riffSize = withMetadata.Length - 8;
        withMetadata[4] = (byte)riffSize;
        withMetadata[5] = (byte)(riffSize >> 8);
        withMetadata[6] = (byte)(riffSize >> 16);
        withMetadata[7] = (byte)(riffSize >> 24);
        decoded = AudioWavePcm.DecodeStereo16(withMetadata);
        Expect(decoded.Length == pcm.Length && decoded[0] == pcm[0],
            "skip odd-sized metadata chunk and padding");

        rejected = false;
        try { AudioWavePcm.EncodeStereo16(new byte[AudioWavePcm.SampleRate * 2 * 4 + 4]); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "reject WAV larger than 2 seconds");

        byte[] quarter = AudioPcmVolume.ScaleStereo16(pcm, 25);
        byte[] muted = AudioPcmVolume.ScaleStereo16(pcm, 0);
        byte[] full = AudioPcmVolume.ScaleStereo16(pcm, 100);
        Expect(quarter.Length == pcm.Length, "gain preserves frame count");
        for (int i = 0; i < pcm.Length; i += 2)
        {
            short original = (short)(pcm[i] | (pcm[i + 1] << 8));
            short quiet = (short)(quarter[i] | (quarter[i + 1] << 8));
            Expect(quiet == original / 4, "25 percent signed sample gain");
            Expect(muted[i] == 0 && muted[i + 1] == 0, "mute is silent");
            Expect(full[i] == pcm[i] && full[i + 1] == pcm[i + 1], "100 percent is bit-exact");
        }
        Expect(pcm[0] == 0 && pcm[1] == 16, "gain must not modify original PCM");
        rejected = false;
        try { AudioPcmVolume.ScaleStereo16(pcm, 101); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "reject gain above 100");
        rejected = false;
        try { AudioPcmVolume.ScaleStereo16(pcm, -1); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "reject negative gain");
        rejected = false;
        try { AudioPcmVolume.ScaleStereo16(new byte[3], 25); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "reject incomplete stereo frame");

        Console.WriteLine("[AUDIO-PCI-HOST] PASS " + checks + " checks (stubbed PCI provider; no hardware)");
    }
}

namespace ZonderqOS.Hardware
{
    public sealed class DeviceId
    {
        public DeviceId(string address) { Address = address; }
        public string Address { get; }
    }

    public sealed class DeviceDescriptor
    {
        public DeviceDescriptor(string address, ushort vendor, ushort device, byte cls, byte sub, byte progIf)
        {
            Id = new DeviceId(address);
            VendorId = vendor;
            DeviceId = device;
            ClassCode = cls;
            Subclass = sub;
            ProgrammingInterface = progIf;
        }

        public DeviceId Id { get; }
        public ushort VendorId { get; }
        public ushort DeviceId { get; }
        public byte ClassCode { get; }
        public byte Subclass { get; }
        public byte ProgrammingInterface { get; }
    }
}

namespace ZonderqOS.Platform
{
    public static class PciSysfsProvider
    {
        public static bool Available;
        public static List<DeviceDescriptor> Devices = new();
        public static bool TryDiscover(out List<DeviceDescriptor> devices)
        {
            devices = Available ? Devices : null;
            return Available;
        }
    }
}

namespace ZonderqOS
{
    public static class CommandIO
    {
        public static void WriteLine(string message) => Console.WriteLine(message);
    }
}
