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

    static void WriteLittle32(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
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
        // The independent mono WAV QEMU path uses 660Hz so a stray 440Hz tone cannot pass.
        byte[] monoProbeSource = AudioTonePcm.GenerateSquareStereo16(48000, 660, 250, 4096);
        int monoTransitions = 0;
        for (int i = 1; i < monoProbeSource.Length / 4; i++)
        {
            int prior = (i - 1) * 4;
            int current = i * 4;
            short a = (short)(monoProbeSource[prior] | (monoProbeSource[prior + 1] << 8));
            short b = (short)(monoProbeSource[current] | (monoProbeSource[current + 1] << 8));
            if ((a < 0) != (b < 0)) monoTransitions++;
        }
        double monoHz = monoTransitions * 48000.0 / (2 * (monoProbeSource.Length / 4 - 1));
        Expect(Math.Abs(monoHz - 660) < 6, "distinct 660Hz mono QEMU probe frequency");
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

        // WAVE_FORMAT_EXTENSIBLE PCM16 is common in modern WAV exports.
        // Exercise the actual C# decoder, not a synthetic Python reimplementation.
        byte[] extensible = AudioWavePcm.EncodeExtensibleStereo16AtRate(pcm, 48000);
        Expect(extensible.Length == encoded.Length + 24, "extensible fmt has 24 additional bytes");
        byte[] extensibleDecoded = AudioWavePcm.DecodeStereo16(extensible);
        Expect(extensibleDecoded.Length == pcm.Length, "extensible PCM16 decoded length");
        for (int i = 0; i < pcm.Length; i++)
            Expect(extensibleDecoded[i] == pcm[i], "extensible PCM16 exact round-trip");

        foreach (int corruptField in new[] { 36, 38, 40, 44, 55 })
        {
            byte[] invalidExtensible = (byte[])extensible.Clone();
            if (corruptField == 36) invalidExtensible[36] = 21; // cbSize < 22
            if (corruptField == 38) invalidExtensible[38] = 8; // valid bits != container
            if (corruptField == 40) invalidExtensible[40] = 4; // mono speaker mask in stereo
            if (corruptField == 44) invalidExtensible[44] = 3; // IEEE float GUID
            if (corruptField == 55) invalidExtensible[55] = 0; // damaged PCM GUID
            bool refusedExtensible = false;
            try { AudioWavePcm.DecodeStereo16(invalidExtensible); }
            catch (ArgumentException) { refusedExtensible = true; }
            Expect(refusedExtensible, "reject malformed extensible WAV field " + corruptField);
        }

        // Real 20 valid bits in a 24-bit WAVE_FORMAT_EXTENSIBLE container.
        // The upper 16 bits are bit-exact and the low padding nibble is zero.
        byte[] pcm20 = new byte[pcm.Length / 2 * 3];
        for (int i = 0; i < pcm.Length / 2; i++)
        {
            pcm20[i * 3] = 0;
            pcm20[i * 3 + 1] = pcm[i * 2];
            pcm20[i * 3 + 2] = pcm[i * 2 + 1];
        }
        byte[] extensible20 = AudioWavePcm.EncodeExtensiblePcm24AtRate(pcm20, 48000, 2, 20);
        byte[] decoded20 = AudioWavePcm.DecodeStereo16(extensible20);
        Expect(decoded20.Length == pcm.Length, "20-in-24 extensible output length");
        for (int i = 0; i < pcm.Length; i++)
            Expect(decoded20[i] == pcm[i], "20-in-24 extensible signed samples bit-exact");
        using (var readOnly20 = new MemoryStream(extensible20, false))
            Expect(AudioWaveInput.DecodeBounded(readOnly20).Length == pcm.Length,
                "20-in-24 read-only stream path");
        byte[] nonzeroPadding20 = (byte[])extensible20.Clone();
        nonzeroPadding20[68] = 1; // first sample's unused low four bits
        rejected = false;
        try { AudioWavePcm.DecodeStereo16(nonzeroPadding20); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "reject nonzero 20-in-24 padding bits");
        byte[] invalidValidBits20 = (byte[])extensible20.Clone();
        invalidValidBits20[38] = 15;
        rejected = false;
        try { AudioWavePcm.DecodeStereo16(invalidValidBits20); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "reject less than 16 valid bits in 24-bit container");
        invalidValidBits20 = (byte[])extensible20.Clone();
        invalidValidBits20[38] = 25;
        rejected = false;
        try { AudioWavePcm.DecodeStereo16(invalidValidBits20); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "reject valid bits exceeding 24-bit container");

        // A short extensible fmt at the very end must fail cleanly, not read
        // validBits beyond the input buffer (malformed external WAV safety).
        byte[] shortExtensibleFmt = new byte[44];
        Array.Copy(encoded, 0, shortExtensibleFmt, 0, 12);
        WriteLittle32(shortExtensibleFmt, 4, shortExtensibleFmt.Length - 8);
        shortExtensibleFmt[12] = (byte)'J'; shortExtensibleFmt[13] = (byte)'U';
        shortExtensibleFmt[14] = (byte)'N'; shortExtensibleFmt[15] = (byte)'K';
        WriteLittle32(shortExtensibleFmt, 16, 0);
        shortExtensibleFmt[20] = (byte)'f'; shortExtensibleFmt[21] = (byte)'m';
        shortExtensibleFmt[22] = (byte)'t'; shortExtensibleFmt[23] = (byte)' ';
        WriteLittle32(shortExtensibleFmt, 24, 16);
        shortExtensibleFmt[28] = 0xFE; shortExtensibleFmt[29] = 0xFF;
        rejected = false;
        try { AudioWavePcm.DecodeStereo16(shortExtensibleFmt); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "short final extensible fmt rejected without out-of-range read");

        // A real-world PCM16 mono WAV should decode into bounded stereo frames.
        byte[] mono = new byte[44 + pcm.Length / 2];
        Array.Copy(encoded, mono, 44);
        mono[22] = 1; mono[23] = 0; // one channel
        WriteLittle32(mono, 28, 48000 * 2); // 16-bit mono byte rate
        mono[32] = 2; mono[33] = 0; // mono block alignment
        WriteLittle32(mono, 4, mono.Length - 8);
        WriteLittle32(mono, 40, mono.Length - 44);
        for (int i = 0; i < pcm.Length / 4; i++)
        {
            mono[44 + i * 2] = pcm[i * 4];
            mono[44 + i * 2 + 1] = pcm[i * 4 + 1];
        }
        byte[] upmixed = AudioWavePcm.DecodeStereo16(mono);
        Expect(upmixed.Length == pcm.Length, "mono WAV upmix output length");
        for (int i = 0; i < pcm.Length; i++)
            Expect(upmixed[i] == pcm[i], "mono upmix duplicates PCM16 into both channels");

        byte[] monoPcm = new byte[mono.Length - 44];
        Array.Copy(mono, 44, monoPcm, 0, monoPcm.Length);
        byte[] generatedMono = AudioWavePcm.EncodeMono16(monoPcm);
        Expect(generatedMono.Length == mono.Length, "canonical mono WAV fixture length");
        for (int i = 0; i < mono.Length; i++)
            Expect(generatedMono[i] == mono[i], "mono encoder matches RIFF fixture");
        byte[] monoDecoded = AudioWavePcm.DecodeStereo16(generatedMono);
        for (int i = 0; i < pcm.Length; i++)
            Expect(monoDecoded[i] == pcm[i], "mono WAV encoder/decoder round-trip upmix");
        bool invalidMonoEncode = false;
        try { AudioWavePcm.EncodeMono16(new byte[3]); }
        catch (ArgumentException) { invalidMonoEncode = true; }
        Expect(invalidMonoEncode, "reject partial mono PCM16 sample");
        invalidMonoEncode = false;
        try { AudioWavePcm.EncodeMono16(new byte[48000 * 2 * 2 + 2]); }
        catch (ArgumentException) { invalidMonoEncode = true; }
        Expect(invalidMonoEncode, "reject mono encode longer than two seconds");

        byte[] invalidMono = (byte[])mono.Clone();
        invalidMono[32] = 4; // invalid mono block alignment
        bool badMonoRejected = false;
        try { AudioWavePcm.DecodeStereo16(invalidMono); }
        catch (ArgumentException) { badMonoRejected = true; }
        Expect(badMonoRejected, "reject invalid mono block alignment");

        byte[] overlongMono = new byte[44 + 48000 * 2 * 2 + 2];
        Array.Copy(mono, overlongMono, 44);
        WriteLittle32(overlongMono, 4, overlongMono.Length - 8);
        WriteLittle32(overlongMono, 40, overlongMono.Length - 44);
        badMonoRejected = false;
        try { AudioWavePcm.DecodeStereo16(overlongMono); }
        catch (ArgumentException) { badMonoRejected = true; }
        Expect(badMonoRejected, "reject mono WAV longer than two seconds before upmix");

        // The mono extension must not weaken the stereo frame-alignment gate.
        byte[] partialStereo = new byte[encoded.Length - 2];
        Array.Copy(encoded, partialStereo, partialStereo.Length);
        WriteLittle32(partialStereo, 4, partialStereo.Length - 8);
        WriteLittle32(partialStereo, 40, pcm.Length - 2);
        bool partialRejected = false;
        try { AudioWavePcm.DecodeStereo16(partialStereo); }
        catch (ArgumentException) { partialRejected = true; }
        Expect(partialRejected, "reject stereo data with a partial 4-byte frame");

        byte[] maxMonoPcm = new byte[48000 * 2 * 2];
        byte[] maxMonoWav = AudioWavePcm.EncodeMono16(maxMonoPcm);
        byte[] maxMonoDecoded = AudioWavePcm.DecodeStereo16(maxMonoWav);
        Expect(maxMonoDecoded.Length == 48000 * 2 * 4,
            "accept exact two-second mono limit with bounded stereo output");

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

        // 44.1 kHz source WAV must produce bounded 48 kHz stereo PCM
        // before reaching the stock HDA driver. This is host C#, not QEMU.
        byte[] source44100 = AudioTonePcm.GenerateSquareStereo16(44100, 880, 250, 4096);
        byte[] wave44100 = AudioWavePcm.EncodeStereo16AtRate(source44100, 44100);
        byte[] converted44100 = AudioWavePcm.DecodeStereo16(wave44100);
        Expect(converted44100.Length == 48000, "44.1k stereo 250ms converts to 48k stereo 250ms");
        byte[] pcm20at44100 = new byte[source44100.Length / 2 * 3];
        for (int i = 0; i < source44100.Length / 2; i++)
        {
            pcm20at44100[i * 3] = 0;
            pcm20at44100[i * 3 + 1] = source44100[i * 2];
            pcm20at44100[i * 3 + 2] = source44100[i * 2 + 1];
        }
        byte[] extensible20at44100 = AudioWavePcm.EncodeExtensiblePcm24AtRate(pcm20at44100, 44100, 2, 20);
        byte[] converted20at44100 = AudioWavePcm.DecodeStereo16(extensible20at44100);
        Expect(converted20at44100.Length == converted44100.Length,
            "20-in-24 extensible 44.1kHz resampler length");
        for (int i = 0; i < converted44100.Length; i++)
            Expect(converted20at44100[i] == converted44100[i],
                "20-in-24 extensible 44.1kHz resampler matches PCM16");

        byte[] extensible44100 = AudioWavePcm.EncodeExtensibleStereo16AtRate(source44100, 44100);
        byte[] extensibleConverted = AudioWavePcm.DecodeStereo16(extensible44100);
        Expect(extensibleConverted.Length == converted44100.Length, "44.1k extensible resampling length");
        for (int i = 0; i < extensibleConverted.Length; i++)
            Expect(extensibleConverted[i] == converted44100[i], "44.1k extensible PCM matches standard RIFF");
        int crossings44100 = 0;
        for (int i = 0; i < converted44100.Length / 4; i++)
        {
            int at = i * 4;
            short left = (short)(converted44100[at] | (converted44100[at + 1] << 8));
            short right = (short)(converted44100[at + 2] | (converted44100[at + 3] << 8));
            Expect(left == right, "resampling preserves stereo phase");
            Expect(left >= -4096 && left <= 4096, "resampling does not exceed source amplitude");
            if (i > 0)
            {
                int prior = at - 4;
                short old = (short)(converted44100[prior] | (converted44100[prior + 1] << 8));
                if ((old < 0) != (left < 0)) crossings44100++;
            }
        }
        double convertedHz = crossings44100 * 48000.0 / (2 * (converted44100.Length / 4 - 1));
        Expect(Math.Abs(convertedHz - 880) < 10, "44.1k->48k resampler preserves 880Hz pitch");
        byte[] sourceMono44100 = new byte[source44100.Length / 2];
        for (int i = 0; i < sourceMono44100.Length / 2; i++)
        {
            sourceMono44100[i * 2] = source44100[i * 4];
            sourceMono44100[i * 2 + 1] = source44100[i * 4 + 1];
        }
        byte[] monoWave44100 = AudioWavePcm.EncodeMono16(sourceMono44100);
        WriteLittle32(monoWave44100, 24, 44100);
        WriteLittle32(monoWave44100, 28, 44100 * 2);
        byte[] monoConverted44100 = AudioWavePcm.DecodeStereo16(monoWave44100);
        Expect(monoConverted44100.Length == 48000, "44.1k mono upmix and resample duration");
        for (int i = 0; i < monoConverted44100.Length; i++)
            Expect(monoConverted44100[i] == converted44100[i], "mono 44.1k upmix matches stereo source");
        byte[] invalidRate44100 = (byte[])wave44100.Clone();
        WriteLittle32(invalidRate44100, 24, 32000);
        WriteLittle32(invalidRate44100, 28, 32000 * 4);
        rejected = false;
        try { AudioWavePcm.DecodeStereo16(invalidRate44100); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "unsupported 32k WAV still refused");
        byte[] overlong44100 = new byte[44 + 44100 * 2 * 4 + 4];
        Array.Copy(wave44100, overlong44100, wave44100.Length);
        WriteLittle32(overlong44100, 4, overlong44100.Length - 8);
        WriteLittle32(overlong44100, 40, overlong44100.Length - 44);
        rejected = false;
        try { AudioWavePcm.DecodeStereo16(overlong44100); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "44.1k WAV longer than two seconds refused");
        rejected = false;
        try { AudioWavePcm.EncodeStereo16AtRate(source44100, 32000); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "unsupported encoder rate refused");

        // Read-only bounded Stream loader: future external WAV integration
        // must not read unbounded metadata or write/mount a volume.
        using (var stream = new MemoryStream(wave44100, false))
        {
            byte[] fromStream = AudioWaveInput.DecodeBounded(stream);
            Expect(fromStream.Length == converted44100.Length, "44.1k stream conversion length");
            for (int i = 0; i < fromStream.Length; i++)
                Expect(fromStream[i] == converted44100[i], "stream reader matches direct RIFF decoder");
        }
        using (var stream = new MemoryStream(encoded, false))
        {
            byte[] fromStream = AudioWaveInput.DecodeBounded(stream);
            Expect(fromStream.Length == pcm.Length, "48k stream conversion length");
        }
        // The future FAT32 adapter must pass a read-only handle, never a
        // writable stream, even when its caller promises not to write.
        rejected = false;
        using (var writable = new MemoryStream((byte[])encoded.Clone(), true))
        {
            try { AudioWaveInput.DecodeBounded(writable); }
            catch (ArgumentException) { rejected = true; }
            Expect(writable.Position == 0, "reject writable WAV before reading any bytes");
            Expect(writable.CanWrite, "rejecting input must not dispose caller stream");
        }
        Expect(rejected, "writable WAV stream must be refused");
        byte[] truncatedStream = new byte[encoded.Length - 1];
        Array.Copy(encoded, truncatedStream, truncatedStream.Length);
        rejected = false;
        try { using var stream = new MemoryStream(truncatedStream, false); AudioWaveInput.DecodeBounded(stream); }
        catch (EndOfStreamException) { rejected = true; }
        Expect(rejected, "truncated stream must not be accepted");
        byte[] trailingStream = new byte[encoded.Length + 1];
        Array.Copy(encoded, trailingStream, encoded.Length);
        rejected = false;
        try { using var stream = new MemoryStream(trailingStream, false); AudioWaveInput.DecodeBounded(stream); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "trailing stream data must not be accepted");
        byte[] oversizedHeader = new byte[12];
        Array.Copy(encoded, oversizedHeader, 12);
        WriteLittle32(oversizedHeader, 4, AudioWaveInput.MaxFileBytes);
        rejected = false;
        try { using var stream = new MemoryStream(oversizedHeader, false); AudioWaveInput.DecodeBounded(stream); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "oversized RIFF length rejected before allocation");
        rejected = false;
        try { AudioWaveInput.DecodeBounded(null); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "null stream rejected");

        // External read-only streams may return tiny chunks and cannot seek.
        // Exercise the actual bounded reader without mounting a filesystem.
        using (var fragmented = new ShortReadStream(wave44100))
        {
            byte[] fromFragments = AudioWaveInput.DecodeBounded(fragmented);
            Expect(fromFragments.Length == converted44100.Length,
                "nonseekable fragmented read-only stream decodes full WAV");
            for (int i = 0; i < fromFragments.Length; i++)
                Expect(fromFragments[i] == converted44100[i],
                    "fragmented stream PCM matches contiguous input");
            Expect(fragmented.CanRead, "bounded loader does not dispose caller-owned stream");
        }
        rejected = false;
        try { using var fragmented = new ShortReadStream(truncatedStream); AudioWaveInput.DecodeBounded(fragmented); }
        catch (EndOfStreamException) { rejected = true; }
        Expect(rejected, "fragmented truncated WAV must fail");
        rejected = false;
        try { using var fragmented = new ShortReadStream(trailingStream); AudioWaveInput.DecodeBounded(fragmented); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "fragmented trailing WAV must fail");

        // Unsigned PCM8 WAV is distinct from signed PCM16: silence is 128.
        // Exercise actual decoder bytes, not a model of the conversion.
        byte[] pcm8 = new byte[pcm.Length / 2];
        for (int i = 0; i < pcm8.Length; i++)
        {
            int at = i * 2;
            short sample = (short)(pcm[at] | (pcm[at + 1] << 8));
            pcm8[i] = (byte)(128 + (sample >> 8));
        }
        byte[] stereo8 = AudioWavePcm.EncodePcm8AtRate(pcm8, 48000, 2);
        byte[] decoded8 = AudioWavePcm.DecodeStereo16(stereo8);
        Expect(decoded8.Length == pcm.Length, "48k PCM8 stereo expands to PCM16");
        for (int i = 0; i < pcm.Length; i++)
            Expect(decoded8[i] == pcm[i], "48k PCM8 stereo conversion preserves quantized square wave");

        byte[] mono8 = new byte[pcm8.Length / 2];
        for (int i = 0; i < mono8.Length; i++)
            mono8[i] = pcm8[i * 2];
        byte[] mono8Wave = AudioWavePcm.EncodePcm8AtRate(mono8, 48000, 1);
        byte[] decodedMono8 = AudioWavePcm.DecodeStereo16(mono8Wave);
        Expect(decodedMono8.Length == pcm.Length, "48k PCM8 mono upmix length");
        for (int i = 0; i < pcm.Length; i++)
            Expect(decodedMono8[i] == pcm[i], "48k PCM8 mono upmix duplicates channels");

        // Single-frame odd-sized mono8 data is legal RIFF; no 16-bit alignment.
        byte[] oddMono8 = AudioWavePcm.EncodePcm8AtRate(new byte[] { 0, 128, 255 }, 48000, 1);
        Expect(oddMono8.Length == 48, "PCM8 odd-sized chunk is RIFF-padded");
        byte[] decodedOddMono8 = AudioWavePcm.DecodeStereo16(oddMono8);
        Expect(decodedOddMono8.Length == 12, "odd-length mono8 with RIFF pad is accepted");
        Expect(decodedOddMono8[0] == 0 && decodedOddMono8[1] == 128,
            "PCM8 zero maps to signed -32768");
        Expect(decodedOddMono8[4] == 0 && decodedOddMono8[5] == 0,
            "PCM8 128 maps to signed zero");
        Expect(decodedOddMono8[8] == 0 && decodedOddMono8[9] == 127,
            "PCM8 255 maps to signed 32512");

        byte[] pcm8at44100 = new byte[source44100.Length / 2];
        for (int i = 0; i < pcm8at44100.Length; i++)
        {
            int at = i * 2;
            short sample = (short)(source44100[at] | (source44100[at + 1] << 8));
            pcm8at44100[i] = (byte)(128 + (sample >> 8));
        }
        byte[] wave8at44100 = AudioWavePcm.EncodePcm8AtRate(pcm8at44100, 44100, 2);
        byte[] converted8at44100 = AudioWavePcm.DecodeStereo16(wave8at44100);
        Expect(converted8at44100.Length == converted44100.Length, "44.1k PCM8 resample duration");
        for (int i = 0; i < converted44100.Length; i++)
            Expect(converted8at44100[i] == converted44100[i],
                "44.1k PCM8 resampling matches PCM16 for quantized samples");

        byte[] malformed8 = (byte[])stereo8.Clone();
        malformed8[32] = 4; // incorrect block alignment
        rejected = false;
        try { AudioWavePcm.DecodeStereo16(malformed8); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "reject PCM8 malformed block alignment");
        rejected = false;
        try { AudioWavePcm.EncodePcm8AtRate(new byte[] { 128 }, 48000, 2); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "reject PCM8 incomplete stereo frame");
        rejected = false;
        try { AudioWavePcm.EncodePcm8AtRate(new byte[48000 * 2 + 1], 48000, 1); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "reject PCM8 longer than two seconds");

        // Signed PCM24 (little-endian) must downconvert to PCM16 with the
        // sign preserved; low 8 bits are discarded, not rounded or saturated.
        byte[] pcm24 = new byte[pcm.Length / 2 * 3];
        for (int i = 0; i < pcm.Length / 2; i++)
        {
            pcm24[i * 3] = 0x7f; // deliberately nonzero discarded low bits
            pcm24[i * 3 + 1] = pcm[i * 2];
            pcm24[i * 3 + 2] = pcm[i * 2 + 1];
        }
        byte[] stereo24 = AudioWavePcm.EncodePcm24AtRate(pcm24, 48000, 2);
        byte[] decoded24 = AudioWavePcm.DecodeStereo16(stereo24);
        Expect(decoded24.Length == pcm.Length, "48k PCM24 stereo output frame count");
        for (int i = 0; i < pcm.Length; i++)
            Expect(decoded24[i] == pcm[i], "PCM24 stereo signed downconversion bit-exact");

        byte[] mono24 = new byte[monoPcm.Length / 2 * 3];
        for (int i = 0; i < monoPcm.Length / 2; i++)
        {
            mono24[i * 3] = 0;
            mono24[i * 3 + 1] = monoPcm[i * 2];
            mono24[i * 3 + 2] = monoPcm[i * 2 + 1];
        }
        byte[] mono24Wave = AudioWavePcm.EncodePcm24AtRate(mono24, 48000, 1);
        byte[] decodedMono24 = AudioWavePcm.DecodeStereo16(mono24Wave);
        Expect(decodedMono24.Length == pcm.Length, "48k PCM24 mono upmix output frame count");
        for (int i = 0; i < pcm.Length; i++)
            Expect(decodedMono24[i] == pcm[i], "PCM24 mono downmix duplicates both channels");

        byte[] odd24 = AudioWavePcm.EncodePcm24AtRate(new byte[] { 0xff, 0x00, 0x80 }, 48000, 1);
        Expect(odd24.Length == 48 && odd24[44] == 0xff && odd24[47] == 0,
            "odd-sized PCM24 mono data is RIFF padded, pad excluded from data size");
        byte[] decodedOdd24 = AudioWavePcm.DecodeStereo16(odd24);
        Expect(decodedOdd24.Length == 4 && decodedOdd24[0] == 0 && decodedOdd24[1] == 128 &&
            decodedOdd24[2] == 0 && decodedOdd24[3] == 128,
            "signed PCM24 minimum maps to signed PCM16 minimum on both channels");
        byte[] max24 = AudioWavePcm.DecodeStereo16(
            AudioWavePcm.EncodePcm24AtRate(new byte[] { 0xff, 0xff, 0x7f }, 48000, 1));
        Expect(max24[0] == 255 && max24[1] == 127 && max24[2] == 255 && max24[3] == 127,
            "signed PCM24 maximum maps to signed PCM16 maximum");

        byte[] pcm24at44100 = new byte[source44100.Length / 2 * 3];
        for (int i = 0; i < source44100.Length / 2; i++)
        {
            pcm24at44100[i * 3] = 0;
            pcm24at44100[i * 3 + 1] = source44100[i * 2];
            pcm24at44100[i * 3 + 2] = source44100[i * 2 + 1];
        }
        byte[] wave24at44100 = AudioWavePcm.EncodePcm24AtRate(pcm24at44100, 44100, 2);
        byte[] converted24at44100 = AudioWavePcm.DecodeStereo16(wave24at44100);
        Expect(converted24at44100.Length == converted44100.Length,
            "44.1k PCM24 stereo resampling preserves duration");
        for (int i = 0; i < converted44100.Length; i++)
            Expect(converted24at44100[i] == converted44100[i],
                "44.1k PCM24 stereo resampling matches equivalent PCM16");

        byte[] mono24at44100 = new byte[sourceMono44100.Length / 2 * 3];
        for (int i = 0; i < sourceMono44100.Length / 2; i++)
        {
            mono24at44100[i * 3] = 0;
            mono24at44100[i * 3 + 1] = sourceMono44100[i * 2];
            mono24at44100[i * 3 + 2] = sourceMono44100[i * 2 + 1];
        }
        byte[] convertedMono24at44100 = AudioWavePcm.DecodeStereo16(
            AudioWavePcm.EncodePcm24AtRate(mono24at44100, 44100, 1));
        for (int i = 0; i < converted44100.Length; i++)
            Expect(convertedMono24at44100[i] == converted44100[i],
                "44.1k PCM24 mono resampling matches equivalent stereo");

        byte[] max24Pcm = new byte[48000 * 2 * 2 * 3];
        byte[] max24Wave = AudioWavePcm.EncodePcm24AtRate(max24Pcm, 48000, 2);
        using (var stream = new MemoryStream(max24Wave, false))
        {
            byte[] max24Decoded = AudioWaveInput.DecodeBounded(stream);
            Expect(max24Decoded.Length == 48000 * 2 * 4,
                "exact two-second PCM24 stereo fits bounded stream and PCM16 output");
        }
        byte[] invalid24 = (byte[])stereo24.Clone();
        invalid24[32] = 4; // PCM24 stereo must have six-byte frames
        rejected = false;
        try { AudioWavePcm.DecodeStereo16(invalid24); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "reject incorrect PCM24 block alignment");
        rejected = false;
        try { AudioWavePcm.EncodePcm24AtRate(new byte[5], 48000, 2); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "reject incomplete PCM24 stereo frame");
        rejected = false;
        try { AudioWavePcm.EncodePcm24AtRate(new byte[48000 * 2 * 2 * 3 + 6], 48000, 2); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "reject PCM24 duration exceeding two seconds");
        rejected = false;
        try { AudioWavePcm.EncodePcm24AtRate(new byte[3], 32000, 1); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "reject unsupported PCM24 sample rate");

        // Signed PCM32 (little-endian) retains its high 16 bits; this
        // exercises actual decoder C#, not a synthetic Python conversion.
        byte[] pcm32 = new byte[pcm.Length / 2 * 4];
        for (int i = 0; i < pcm.Length / 2; i++)
        {
            pcm32[i * 4] = 0x34;
            pcm32[i * 4 + 1] = 0x12; // deliberately discarded low 16 bits
            pcm32[i * 4 + 2] = pcm[i * 2];
            pcm32[i * 4 + 3] = pcm[i * 2 + 1];
        }
        // 24 valid bits in a signed 32-bit extensible container; low 8 bits
        // are padding, while the decoded high 16 bits retain the sign.
        byte[] pcm32valid24 = (byte[])pcm32.Clone();
        for (int i = 0; i < pcm32valid24.Length; i += 4)
            pcm32valid24[i] = 0; // unused low eight bits must be zero
        byte[] extensible32valid24 = AudioWavePcm.EncodeExtensiblePcm32AtRate(pcm32valid24, 48000, 2, 24);
        byte[] decoded32valid24 = AudioWavePcm.DecodeStereo16(extensible32valid24);
        Expect(decoded32valid24.Length == pcm.Length, "24-in-32 extensible output length");
        for (int i = 0; i < pcm.Length; i++)
            Expect(decoded32valid24[i] == pcm[i], "24-in-32 extensible signed samples bit-exact");
        byte[] nonzeroPadding32 = (byte[])extensible32valid24.Clone();
        nonzeroPadding32[68] = 1;
        rejected = false;
        try { AudioWavePcm.DecodeStereo16(nonzeroPadding32); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "reject nonzero 24-in-32 padding bits");

        byte[] stereo32 = AudioWavePcm.EncodePcm32AtRate(pcm32, 48000, 2);
        byte[] decoded32 = AudioWavePcm.DecodeStereo16(stereo32);
        Expect(decoded32.Length == pcm.Length, "PCM32 stereo output frame count");
        for (int i = 0; i < pcm.Length; i++)
            Expect(decoded32[i] == pcm[i], "PCM32 signed stereo downconversion bit-exact");

        byte[] mono32 = new byte[monoPcm.Length / 2 * 4];
        for (int i = 0; i < monoPcm.Length / 2; i++)
        {
            mono32[i * 4] = 0;
            mono32[i * 4 + 1] = 0;
            mono32[i * 4 + 2] = monoPcm[i * 2];
            mono32[i * 4 + 3] = monoPcm[i * 2 + 1];
        }
        byte[] mono32Wave = AudioWavePcm.EncodePcm32AtRate(mono32, 48000, 1);
        byte[] decodedMono32 = AudioWavePcm.DecodeStereo16(mono32Wave);
        Expect(decodedMono32.Length == pcm.Length, "PCM32 mono upmix output length");
        for (int i = 0; i < pcm.Length; i++)
            Expect(decodedMono32[i] == pcm[i], "PCM32 mono upmix duplicates channels");

        byte[] min32 = AudioWavePcm.DecodeStereo16(
            AudioWavePcm.EncodePcm32AtRate(new byte[] { 0, 0, 0, 128 }, 48000, 1));
        Expect(min32.Length == 4 && min32[0] == 0 && min32[1] == 128 &&
            min32[2] == 0 && min32[3] == 128, "PCM32 minimum maps to signed PCM16 minimum");
        byte[] max32 = AudioWavePcm.DecodeStereo16(
            AudioWavePcm.EncodePcm32AtRate(new byte[] { 255, 255, 255, 127 }, 48000, 1));
        Expect(max32.Length == 4 && max32[0] == 255 && max32[1] == 127 &&
            max32[2] == 255 && max32[3] == 127, "PCM32 maximum maps to signed PCM16 maximum");

        byte[] pcm32at44100 = new byte[source44100.Length / 2 * 4];
        for (int i = 0; i < source44100.Length / 2; i++)
        {
            pcm32at44100[i * 4] = 0;
            pcm32at44100[i * 4 + 1] = 0;
            pcm32at44100[i * 4 + 2] = source44100[i * 2];
            pcm32at44100[i * 4 + 3] = source44100[i * 2 + 1];
        }
        byte[] converted32at44100 = AudioWavePcm.DecodeStereo16(
            AudioWavePcm.EncodePcm32AtRate(pcm32at44100, 44100, 2));
        Expect(converted32at44100.Length == converted44100.Length,
            "44.1k PCM32 resampling preserves duration");
        for (int i = 0; i < converted44100.Length; i++)
            Expect(converted32at44100[i] == converted44100[i],
                "44.1k PCM32 stereo resampling matches equivalent PCM16");

        byte[] mono32at44100 = new byte[sourceMono44100.Length / 2 * 4];
        for (int i = 0; i < sourceMono44100.Length / 2; i++)
        {
            mono32at44100[i * 4] = 0;
            mono32at44100[i * 4 + 1] = 0;
            mono32at44100[i * 4 + 2] = sourceMono44100[i * 2];
            mono32at44100[i * 4 + 3] = sourceMono44100[i * 2 + 1];
        }
        byte[] convertedMono32at44100 = AudioWavePcm.DecodeStereo16(
            AudioWavePcm.EncodePcm32AtRate(mono32at44100, 44100, 1));
        for (int i = 0; i < converted44100.Length; i++)
            Expect(convertedMono32at44100[i] == converted44100[i],
                "44.1k PCM32 mono resampling matches equivalent stereo");

        byte[] max32Pcm = new byte[48000 * 2 * 2 * 4];
        byte[] max32Wave = AudioWavePcm.EncodePcm32AtRate(max32Pcm, 48000, 2);
        Expect(max32Wave.Length < AudioWaveInput.MaxFileBytes,
            "maximum PCM32 source fits bounded RIFF input");
        using (var stream = new MemoryStream(max32Wave, false))
        {
            byte[] max32Decoded = AudioWaveInput.DecodeBounded(stream);
            Expect(max32Decoded.Length == 48000 * 2 * 4,
                "exact two-second PCM32 stereo decodes into bounded PCM16");
        }
        using (var fragmented = new ShortReadStream(stereo32))
        {
            byte[] fromFragments32 = AudioWaveInput.DecodeBounded(fragmented);
            Expect(fromFragments32.Length == pcm.Length, "fragmented PCM32 stream length");
            for (int i = 0; i < pcm.Length; i++)
                Expect(fromFragments32[i] == pcm[i], "fragmented PCM32 stream bit-exact");
        }

        byte[] malformed32 = (byte[])stereo32.Clone();
        malformed32[32] = 4; // PCM32 stereo must have eight-byte frames
        rejected = false;
        try { AudioWavePcm.DecodeStereo16(malformed32); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "reject incorrect PCM32 block alignment");
        malformed32 = (byte[])stereo32.Clone();
        malformed32[20] = 3; // IEEE float is not signed integer PCM
        rejected = false;
        try { AudioWavePcm.DecodeStereo16(malformed32); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "reject IEEE float PCM32 masquerading as signed integer");
        rejected = false;
        try { AudioWavePcm.EncodePcm32AtRate(new byte[7], 48000, 2); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "reject incomplete PCM32 stereo frame");
        rejected = false;
        try { AudioWavePcm.EncodePcm32AtRate(new byte[48000 * 2 * 2 * 4 + 8], 48000, 2); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "reject PCM32 duration exceeding two seconds");
        rejected = false;
        try { AudioWavePcm.EncodePcm32AtRate(new byte[4], 16000, 1); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "reject unsupported PCM32 sample rate");

        // Common 22.05/32 kHz PCM16 WAV inputs must use the real bounded
        // read-only stream loader and the existing integer 48 kHz resampler.
        foreach (int inputRate in new[] { 22050, 32000 })
        {
            byte[] sourceLowRate = AudioTonePcm.GenerateSquareStereo16(inputRate, 3520, 250, 4096);
            byte[] lowRateWave = AudioWavePcm.EncodeStereo16AtRate(sourceLowRate, inputRate);
            using (var lowRateInput = new MemoryStream(lowRateWave, false))
            {
                byte[] resampled = AudioWaveInput.DecodeBounded(lowRateInput);
                Expect(resampled.Length == 48000, "low-rate WAV produces 250ms 48kHz stereo16");
                int crossings = 0;
                for (int frame = 1; frame < resampled.Length / 4; frame++)
                {
                    int at = frame * 4;
                    int before = at - 4;
                    short current = (short)(resampled[at] | (resampled[at + 1] << 8));
                    short prior = (short)(resampled[before] | (resampled[before + 1] << 8));
                    Expect(resampled[at] == resampled[at + 2] &&
                        resampled[at + 1] == resampled[at + 3], "low-rate resampler stereo phase");
                    if ((current < 0) != (prior < 0)) crossings++;
                }
                double pitch = crossings * 48000.0 / (2 * (resampled.Length / 4 - 1));
                Expect(Math.Abs(pitch - 3520) < 60, "low-rate resampler preserves 3520Hz pitch");
            }
        }
        // The same 32 kHz source is supported in unsigned PCM8 and signed
        // PCM24/PCM32, without enabling any new device or filesystem access.
        byte[] mono8at32k = new byte[32000 / 4];
        for (int i = 0; i < mono8at32k.Length; i++)
            mono8at32k[i] = (byte)((i & 1) == 0 ? 192 : 64);
        byte[] low8 = AudioWavePcm.DecodeStereo16(
            AudioWavePcm.EncodePcm8AtRate(mono8at32k, 32000, 1));
        Expect(low8.Length == 48000, "32kHz unsigned PCM8 mono resamples to stereo16");
        byte[] low24 = AudioWavePcm.DecodeStereo16(
            AudioWavePcm.EncodePcm24AtRate(new byte[32000 / 4 * 3], 32000, 1));
        Expect(low24.Length == 48000, "32kHz signed PCM24 mono resamples to stereo16");
        byte[] low32 = AudioWavePcm.DecodeStereo16(
            AudioWavePcm.EncodePcm32AtRate(new byte[32000 / 4 * 4], 32000, 1));
        Expect(low32.Length == 48000, "32kHz signed PCM32 mono resamples to stereo16");
        rejected = false;
        try { AudioWavePcm.EncodeStereo16AtRate(new byte[4], 16000); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "unsupported 16kHz rate remains rejected");

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

// Simulates a read-only filesystem/device stream that cannot seek and
// returns at most seven bytes per read. No real disk is accessed.
sealed class ShortReadStream : Stream
{
    private readonly byte[] data;
    private int offset;

    public ShortReadStream(byte[] bytes) { data = bytes; }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }
    public override int Read(byte[] buffer, int index, int count)
    {
        if (offset == data.Length) return 0;
        int take = Math.Min(7, Math.Min(count, data.Length - offset));
        Array.Copy(data, offset, buffer, index, take);
        offset += take;
        return take;
    }
    public override int ReadByte()
    {
        if (offset == data.Length) return -1;
        return data[offset++];
    }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int index, int count) => throw new NotSupportedException();
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
