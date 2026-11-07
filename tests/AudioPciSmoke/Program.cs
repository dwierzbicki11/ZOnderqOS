using System;
using System.Collections.Generic;
using System.Diagnostics;
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

    // Independent pre-optimization reference for the bounded PCM drain.
    // It deliberately does not call AudioPcmVolume, so an incorrect fast
    // path cannot validate itself against the same implementation.
    static byte[] ReferenceGainFadeDrain(
        byte[] pcm, int leftPercent, int rightPercent, int fadeMilliseconds, int tailBytes)
    {
        int frames = pcm.Length / 4;
        int fadeFrames = Math.Min(fadeMilliseconds * 48, frames / 2);
        byte[] output = new byte[pcm.Length + tailBytes];
        for (int frame = 0; frame < frames; frame++)
        {
            int envelope = fadeMilliseconds == 0 ? 1 :
                Math.Min(fadeFrames, Math.Min(frame, frames - 1 - frame));
            int divisor = fadeMilliseconds == 0 ? 1 : fadeFrames;
            for (int channel = 0; channel < 2; channel++)
            {
                int offset = frame * 4 + channel * 2;
                short sample = (short)(pcm[offset] | (pcm[offset + 1] << 8));
                int gain = channel == 0 ? leftPercent : rightPercent;
                int scaled = sample * gain / 100;
                scaled = divisor == 0 ? 0 : scaled * envelope / divisor;
                output[offset] = (byte)scaled;
                output[offset + 1] = (byte)(scaled >> 8);
            }
        }
        return output;
    }

    static double MedianPcmMilliseconds(Func<byte[]> action, int iterations)
    {
        long[] elapsed = new long[5];
        action(); // JIT and allocation warm-up; never count it.
        for (int trial = 0; trial < elapsed.Length; trial++)
        {
            long start = Stopwatch.GetTimestamp();
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                byte[] result = action();
                GC.KeepAlive(result);
            }
            elapsed[trial] = Stopwatch.GetTimestamp() - start;
        }
        Array.Sort(elapsed);
        return elapsed[elapsed.Length / 2] * 1000.0 / Stopwatch.Frequency;
    }

    public static void Main()
    {
        // Pure C# regression/benchmark for the delayed PCM mixer. This does
        // not claim hardware playback, HDA DMA, IRQ or GUI integration.
        AudioDelayedMixerSmoke.Run(Expect);
        AudioFloatWaveSmoke.Run(Expect);
        AudioWaveFactSmoke.Run(Expect);
        Expect(AudioPciDiagnostics.IsAudioClass(0x04, 0x01), "legacy audio");
        Expect(AudioPciDiagnostics.IsAudioClass(0x04, 0x03), "HDA");
        Expect(!AudioPciDiagnostics.IsAudioClass(0x04, 0x00), "video must not be audio");
        Expect(!AudioPciDiagnostics.IsAudioClass(0x02, 0x00), "network must not be audio");
        Expect(!AudioPciDiagnostics.IsAudioClass(0x04, 0x80), "other multimedia not assumed audio");
        Expect(AudioPciDiagnostics.DescribeController(0x04, 0x03, 0x00).Contains("HdAudioDriver"), "HDA candidate");
        Expect(AudioPciDiagnostics.DescribeController(0x04, 0x03, 0x01).Contains("unsupported"), "unknown prog-if");
        Expect(AudioPciDiagnostics.DescribeController(0x04, 0x01, 0x00).Contains("AC97"), "legacy controller");

        Expect(AudioPciDiagnostics.DescribePciIdentity(0x8086, 0x2668, 0x04, 0x03)
            .Contains("QEMU intel-hda"), "QEMU Intel HDA PCI identity");
        Expect(AudioPciDiagnostics.DescribePciIdentity(0x8086, 0x2415, 0x04, 0x01)
            .Contains("no stock AC97 driver"), "QEMU Intel AC97 PCI identity");
        Expect(AudioPciDiagnostics.DescribePciIdentity(0x8086, 0x2668, 0x04, 0x03, 0x01)
            .Contains("UNKNOWN_AUDIO_IDENTITY"), "unsupported HDA prog-if must not inherit QEMU identity");
        Expect(AudioPciDiagnostics.DescribePciIdentity(0x8086, 0x2415, 0x04, 0x01, 0x01)
            .Contains("UNKNOWN_AUDIO_IDENTITY"), "unsupported AC97 prog-if must not inherit QEMU identity");
        Expect(AudioPciDiagnostics.DescribePciIdentity(0x8086, 0x2668, 0x04, 0x01)
            .Contains("UNKNOWN_AUDIO_IDENTITY"), "wrong subclass cannot inherit HDA identity");
        Expect(AudioPciDiagnostics.DescribePciIdentity(0x8086, 0x2415, 0x04, 0x03)
            .Contains("UNKNOWN_AUDIO_IDENTITY"), "wrong subclass cannot inherit AC97 identity");
        Expect(AudioPciDiagnostics.DescribePciIdentity(0x10EC, 0x0887, 0x04, 0x03)
            .Contains("UNKNOWN_AUDIO_IDENTITY"), "physical vendor must not be guessed");
        Expect(AudioPciDiagnostics.DescribePciIdentity(0x8086, 0x2668, 0x02, 0x00)
            == "NOT_AUDIO", "network device must never be classified as HDA");

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
        Expect(report.Contains("pci-identity=Intel ICH6 HDA"), "report identifies QEMU HDA candidate");
        Expect(report.Contains("pci-identity=Intel ICH AC97"), "report identifies QEMU AC97 candidate");
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
        // True stereo must preserve independent left/right frequencies, not
        // just duplicate the mono channel. This tests production PCM C#.
        byte[] split = AudioTonePcm.GenerateDualSquareStereo16(48000, 440, 880, 250, 4096);
        Expect(split.Length == pcm.Length, "independent stereo length");
        int leftTransitions = 0;
        int rightTransitions = 0;
        for (int frame = 1; frame < split.Length / 4; frame++)
        {
            int at = frame * 4;
            int prior = at - 4;
            short leftNow = (short)(split[at] | (split[at + 1] << 8));
            short leftBefore = (short)(split[prior] | (split[prior + 1] << 8));
            short rightNow = (short)(split[at + 2] | (split[at + 3] << 8));
            short rightBefore = (short)(split[prior + 2] | (split[prior + 3] << 8));
            Expect(leftNow == 4096 || leftNow == -4096, "left amplitude");
            Expect(rightNow == 4096 || rightNow == -4096, "right amplitude");
            if ((leftNow < 0) != (leftBefore < 0)) leftTransitions++;
            if ((rightNow < 0) != (rightBefore < 0)) rightTransitions++;
        }
        double leftPitch = leftTransitions * 48000.0 / (2 * (split.Length / 4 - 1));
        double rightPitch = rightTransitions * 48000.0 / (2 * (split.Length / 4 - 1));
        Expect(Math.Abs(leftPitch - 440) < 5, "left channel 440Hz");
        Expect(Math.Abs(rightPitch - 880) < 5, "right channel 880Hz");
        Expect(rightTransitions > leftTransitions * 1.9, "independent stereo periods");
        // Four distinct, adjacent PCM tones for the nonperiodic HDA ring
        // replay probe. Check the production mixer, not a Python model.
        int[] sequenceHz = { 400, 700, 1000, 1300 };
        byte[][] sequenceClips = new byte[4][];
        int[] sequenceStarts = new int[4];
        int[] sequenceGains = { 100, 100, 100, 100 };
        int[] sequenceFades = { 5, 5, 5, 5 };
        for (int i = 0; i < 4; i++)
        {
            sequenceClips[i] = AudioTonePcm.GenerateSquareStereo16(48000, sequenceHz[i], 90, 4096);
            sequenceStarts[i] = 12000 + i * 4320;
            Expect(sequenceClips[i].Length == 4320 * 4, "90ms sequence clip size");
        }
        byte[] sequencePcm = AudioPcmMixer.MixScheduledStereo16WithIndividualFades(
            sequenceClips, sequenceStarts, sequenceGains, sequenceFades, 4096);
        Expect(sequencePcm.Length == (12000 + 4 * 4320 + 4096) * 4,
            "bounded four-tone sequence with pre-roll and HDA drain");
        Expect(sequencePcm[0] == 0 && sequencePcm[12000 * 4 - 1] == 0,
            "sequence starts with silent pre-roll");
        for (int i = 0; i < 4; i++)
        {
            int source = 2400 * 4; // 50ms: safely inside the unfaded segment
            int target = (sequenceStarts[i] + 2400) * 4;
            for (int b = 0; b < 4; b++)
                Expect(sequencePcm[target + b] == sequenceClips[i][source + b],
                    "sequence preserves interior PCM samples");
        }
        Expect(sequencePcm[sequencePcm.Length - 1] == 0,
            "sequence ends in silent HDA drain");
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
        // The probe must never allocate more than two seconds of 48k stereo16,
        // even when a caller supplies an unusually high sample rate.
        Expect(AudioTonePcm.GenerateSquareStereo16(48000, 440, 2000, 4096).Length == 384000,
            "single-tone exact two-second PCM allocation boundary");
        rejected = false;
        try { AudioTonePcm.GenerateSquareStereo16(96000, 440, 2000, 4096); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "single-tone high-rate output cap");

        // Pure C# RIFF/WAVE round-trip; no Cosmos HAL or filesystem access.
        byte[] encoded = AudioWavePcm.EncodeStereo16(pcm);
        byte[] decoded = AudioWavePcm.DecodeStereo16(encoded);
        Expect(encoded.Length == pcm.Length + 44, "canonical WAV header length");
        Expect(decoded.Length == pcm.Length, "WAV PCM length");
        for (int i = 0; i < pcm.Length; i++)
            Expect(decoded[i] == pcm[i], "WAV round-trip preserves PCM");

        // A large but valid RIFF metadata chunk is allowed up to the shared
        // stream/direct-decoder limit; one byte beyond it must be rejected
        // before the decoder scans chunks or allocates PCM output.
        int junkBytes = AudioWavePcm.MaxRiffBytes - encoded.Length - 8;
        Expect(junkBytes > 0 && junkBytes % 2 == 0, "RIFF limit fixture has even JUNK padding");
        byte[] maxRiff = new byte[AudioWavePcm.MaxRiffBytes];
        Array.Copy(encoded, 0, maxRiff, 0, 12);
        WriteLittle32(maxRiff, 4, maxRiff.Length - 8);
        maxRiff[12] = (byte)'J'; maxRiff[13] = (byte)'U';
        maxRiff[14] = (byte)'N'; maxRiff[15] = (byte)'K';
        WriteLittle32(maxRiff, 16, junkBytes);
        Array.Copy(encoded, 12, maxRiff, 20 + junkBytes, encoded.Length - 12);
        Expect(AudioWavePcm.DecodeStereo16(maxRiff).Length == pcm.Length,
            "direct decoder accepts exact maximum RIFF file size");
        using (var maxRiffStream = new MemoryStream(maxRiff, false))
            Expect(AudioWaveInput.DecodeBounded(maxRiffStream).Length == pcm.Length,
                "read-only stream accepts exact maximum RIFF file size");
        byte[] oversizedRiff = new byte[maxRiff.Length + 2];
        Array.Copy(maxRiff, oversizedRiff, maxRiff.Length);
        WriteLittle32(oversizedRiff, 4, oversizedRiff.Length - 8);
        rejected = false;
        try { AudioWavePcm.DecodeStereo16(oversizedRiff); }
        catch (ArgumentException ex) { rejected = ex.Message.Contains("file limit"); }
        Expect(rejected, "direct decoder rejects oversized RIFF before chunk scanning");
        rejected = false;
        try
        {
            using var oversizedStream = new MemoryStream(oversizedRiff, false);
            AudioWaveInput.DecodeBounded(oversizedStream);
        }
        catch (ArgumentException ex) { rejected = ex.Message.Contains("file limit"); }
        Expect(rejected, "read-only stream rejects oversized RIFF before allocation");

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
        byte[] invalidEncoder20 = (byte[])pcm20.Clone();
        invalidEncoder20[0] = 1;
        rejected = false;
        try { AudioWavePcm.EncodeExtensiblePcm24AtRate(invalidEncoder20, 48000, 2, 20); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "encoder rejects nonzero 20-in-24 padding");
        byte[] encoded16in24 = AudioWavePcm.EncodeExtensiblePcm24AtRate(pcm20, 48000, 2, 16);
        byte[] decoded16in24 = AudioWavePcm.DecodeStereo16(encoded16in24);
        for (int i = 0; i < pcm.Length; i++)
            Expect(decoded16in24[i] == pcm[i], "16-in-24 valid padding round-trip");
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
        byte[] invalidRate44100 = (byte[])encoded.Clone();
        WriteLittle32(invalidRate44100, 24, 12345);
        WriteLittle32(invalidRate44100, 28, 12345 * 4);
        rejected = false;
        try { AudioWavePcm.DecodeStereo16(invalidRate44100); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "unsupported 12.345kHz WAV still refused");
        byte[] overlong44100 = new byte[44 + 44100 * 2 * 4 + 4];
        Array.Copy(wave44100, overlong44100, wave44100.Length);
        WriteLittle32(overlong44100, 4, overlong44100.Length - 8);
        WriteLittle32(overlong44100, 40, overlong44100.Length - 44);
        rejected = false;
        try { AudioWavePcm.DecodeStereo16(overlong44100); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "44.1k WAV longer than two seconds refused");
        rejected = false;
        try { AudioWavePcm.EncodeStereo16AtRate(source44100, 12345); }
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
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "truncated stream must not be accepted");
        byte[] trailingStream = new byte[encoded.Length + 1];
        Array.Copy(encoded, trailingStream, encoded.Length);
        rejected = false;
        try { using var stream = new MemoryStream(trailingStream, false); AudioWaveInput.DecodeBounded(stream); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "trailing stream data must not be accepted");
        // A seekable FAT32-style read-only handle can reject inconsistent
        // RIFF lengths from the 12-byte header alone. This must happen
        // before allocating/reading the declared payload, unlike the
        // non-seekable fragmented stream path tested below.
        using (var shortFile = new MemoryStream(truncatedStream, false))
        {
            rejected = false;
            try { AudioWaveInput.DecodeBounded(shortFile); }
            catch (ArgumentException) { rejected = true; }
            Expect(rejected && shortFile.Position == 12,
                "seekable truncated RIFF rejected before payload read");
        }
        using (var longFile = new MemoryStream(trailingStream, false))
        {
            rejected = false;
            try { AudioWaveInput.DecodeBounded(longFile); }
            catch (ArgumentException) { rejected = true; }
            Expect(rejected && longFile.Position == 12,
                "seekable trailing RIFF rejected before payload read");
        }
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
        try { AudioWavePcm.EncodePcm24AtRate(new byte[3], 12345, 1); }
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
        byte[] invalidEncoder32 = (byte[])pcm32valid24.Clone();
        invalidEncoder32[0] = 1;
        rejected = false;
        try { AudioWavePcm.EncodeExtensiblePcm32AtRate(invalidEncoder32, 48000, 2, 24); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "encoder rejects nonzero 24-in-32 padding");
        byte[] pcm32valid16 = (byte[])pcm32valid24.Clone();
        for (int i = 0; i < pcm32valid16.Length; i += 4)
            pcm32valid16[i + 1] = 0;
        byte[] encoded16in32 = AudioWavePcm.EncodeExtensiblePcm32AtRate(pcm32valid16, 48000, 2, 16);
        byte[] decoded16in32 = AudioWavePcm.DecodeStereo16(encoded16in32);
        for (int i = 0; i < pcm.Length; i++)
            Expect(decoded16in32[i] == pcm[i], "16-in-32 valid padding round-trip");

        // Independent mono 24-valid-bits-in-32 fixture: the valid low
        // precision byte is nonzero, but the unused lowest byte is zero.
        // Check the same path that the diskless HDA QEMU command exercises.
        byte[] source24in32 = AudioTonePcm.GenerateSquareStereo16(48000, 3700, 250, 4096);
        byte[] mono24in32 = new byte[source24in32.Length];
        for (int frame = 0; frame < mono24in32.Length / 4; frame++)
        {
            int at = frame * 4;
            mono24in32[at] = 0;
            mono24in32[at + 1] = 0x5A;
            mono24in32[at + 2] = source24in32[at];
            mono24in32[at + 3] = source24in32[at + 1];
        }
        byte[] wav24in32 = AudioWavePcm.EncodeExtensiblePcm32AtRate(mono24in32, 48000, 1, 24);
        byte[] decoded24in32;
        using (var readOnly24in32 = new MemoryStream(wav24in32, false))
            decoded24in32 = AudioWaveInput.DecodeBounded(readOnly24in32);
        Expect(decoded24in32.Length == source24in32.Length, "24-in-32 mono QEMU fixture duration");
        for (int i = 0; i < source24in32.Length; i++)
            Expect(decoded24in32[i] == source24in32[i], "24-in-32 mono upmix bit-exact");
        byte[] invalid24in32 = (byte[])wav24in32.Clone();
        invalid24in32[68] = 1;
        rejected = false;
        try { AudioWavePcm.DecodeStereo16(invalid24in32); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "24-in-32 mono rejects nonzero unused bits");

        // The same 24-in-32 mono container also passes the 44.1->48 kHz
        // bounded resampler, preserving both output channels and signed PCM.
        byte[] source24in32at44100 = AudioTonePcm.GenerateSquareStereo16(44100, 3700, 250, 4096);
        byte[] mono24in32at44100 = new byte[source24in32at44100.Length];
        for (int frame = 0; frame < mono24in32at44100.Length / 4; frame++)
        {
            int at = frame * 4;
            mono24in32at44100[at] = 0;
            mono24in32at44100[at + 1] = 0x5A;
            mono24in32at44100[at + 2] = source24in32at44100[at];
            mono24in32at44100[at + 3] = source24in32at44100[at + 1];
        }
        byte[] wav24in32at44100 =
            AudioWavePcm.EncodeExtensiblePcm32AtRate(mono24in32at44100, 44100, 1, 24);
        byte[] decoded24in32at44100 = AudioWavePcm.DecodeStereo16(wav24in32at44100);
        byte[] expected24in32at44100 = AudioWavePcm.DecodeStereo16(
            AudioWavePcm.EncodeStereo16AtRate(source24in32at44100, 44100));
        Expect(decoded24in32at44100.Length == expected24in32at44100.Length,
            "24-in-32 mono 44.1k resampler duration");
        for (int i = 0; i < expected24in32at44100.Length; i++)
            Expect(decoded24in32at44100[i] == expected24in32at44100[i],
                "24-in-32 mono 44.1k resampler signed samples");

        // Real QEMU stereo extensible 24-in-32 fixture: two independent
        // signed 44.1kHz channels must survive 24->16 conversion and
        // 44.1->48kHz interpolation byte-for-byte. Low valid precision
        // differs between channels and unused container bits remain zero.
        byte[] sourceExtStereo44 = AudioTonePcm.GenerateDualSquareStereo16(
            44100, 440, 880, 250, 4096);
        byte[] packedExtStereo44 = new byte[sourceExtStereo44.Length * 2];
        for (int frame = 0; frame < sourceExtStereo44.Length / 4; frame++)
        {
            int src = frame * 4;
            int dst = frame * 8;
            packedExtStereo44[dst] = 0;
            packedExtStereo44[dst + 1] = 0x5A;
            packedExtStereo44[dst + 2] = sourceExtStereo44[src];
            packedExtStereo44[dst + 3] = sourceExtStereo44[src + 1];
            packedExtStereo44[dst + 4] = 0;
            packedExtStereo44[dst + 5] = 0xA5;
            packedExtStereo44[dst + 6] = sourceExtStereo44[src + 2];
            packedExtStereo44[dst + 7] = sourceExtStereo44[src + 3];
        }
        byte[] waveExtStereo44 = AudioWavePcm.EncodeExtensiblePcm32AtRate(
            packedExtStereo44, 44100, 2, 24);
        byte[] decodedExtStereo44;
        using (var inputExtStereo44 = new MemoryStream(waveExtStereo44, false))
            decodedExtStereo44 = AudioWaveInput.DecodeBounded(inputExtStereo44);
        byte[] expectedExtStereo44 = AudioWavePcm.DecodeStereo16(
            AudioWavePcm.EncodeStereo16AtRate(sourceExtStereo44, 44100));
        Expect(decodedExtStereo44.Length == expectedExtStereo44.Length,
            "extensible 24-in-32 44.1k stereo duration");
        for (int i = 0; i < expectedExtStereo44.Length; i++)
            Expect(decodedExtStereo44[i] == expectedExtStereo44[i],
                "extensible 24-in-32 44.1k stereo sample/channel match");
        byte[] badExtStereoPadding = (byte[])waveExtStereo44.Clone();
        badExtStereoPadding[68 + 4] = 1; // right channel's unused low byte
        rejected = false;
        try { AudioWavePcm.DecodeStereo16(badExtStereoPadding); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "extensible 24-in-32 stereo rejects right-channel padding");
        byte[] badExtStereoMask = (byte[])waveExtStereo44.Clone();
        badExtStereoMask[40] = 4; // mono front-center mask on a stereo container
        rejected = false;
        try { AudioWavePcm.DecodeStereo16(badExtStereoMask); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "extensible 24-in-32 stereo rejects mismatched speaker mask");

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
        malformed32[20] = 7; // Unknown format must not be treated as integer or IEEE float.
        rejected = false;
        try { AudioWavePcm.DecodeStereo16(malformed32); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "reject unknown PCM32-like format tag");
        rejected = false;
        try { AudioWavePcm.EncodePcm32AtRate(new byte[7], 48000, 2); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "reject incomplete PCM32 stereo frame");
        rejected = false;
        try { AudioWavePcm.EncodePcm32AtRate(new byte[48000 * 2 * 2 * 4 + 8], 48000, 2); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "reject PCM32 duration exceeding two seconds");
        rejected = false;
        try { AudioWavePcm.EncodePcm32AtRate(new byte[4], 12345, 1); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "reject unsupported PCM32 sample rate");

        // A full two-second 11.025kHz PCM8 mono voice clip must stay inside
        // the 48kHz stereo PCM16 output budget. One extra source frame is
        // rejected before allocating a resampled output buffer.
        byte[] max11025Mono8 = new byte[11025 * 2];
        byte[] max11025Wave = AudioWavePcm.EncodePcm8AtRate(max11025Mono8, 11025, 1);
        using (var max11025Input = new MemoryStream(max11025Wave, false))
            Expect(AudioWaveInput.DecodeBounded(max11025Input).Length == 48000 * 2 * 4,
                "two-second 11.025kHz PCM8 mono stays within output cap");
        rejected = false;
        try { AudioWavePcm.EncodePcm8AtRate(new byte[11025 * 2 + 1], 11025, 1); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "11.025kHz PCM8 mono refuses a source longer than two seconds");

        // Common 8/11.025/16/22.05/24/32 kHz PCM16 WAV inputs must use the real bounded
        // read-only stream loader and the existing integer 48 kHz resampler.
        foreach (int inputRate in new[] { 8000, 11025, 16000, 22050, 24000, 32000 })
        {
            int expectedPitch = inputRate == 8000 ? 400 : inputRate == 11025 ? 550 : inputRate == 16000 ? 1500 : inputRate == 22050 ? 1100 :
                inputRate == 24000 ? 740 : 3520;
            byte[] sourceLowRate = AudioTonePcm.GenerateSquareStereo16(inputRate, expectedPitch, 250, 4096);
            byte[] lowRateWave = AudioWavePcm.EncodeStereo16AtRate(sourceLowRate, inputRate);
            using (var lowRateInput = new MemoryStream(lowRateWave, false))
            {
                byte[] resampled = AudioWaveInput.DecodeBounded(lowRateInput);
                int expectedFrames = (int)(((long)(sourceLowRate.Length / 4) * 48000 + inputRate - 1) / inputRate);
                Expect(resampled.Length == expectedFrames * 4, "low-rate WAV preserves source frame duration after resampling");
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
                Expect(Math.Abs(pitch - expectedPitch) < 60,
                    "low-rate resampler preserves 8/16/22.05/24/32kHz tone pitch");
                if (inputRate == 8000 || inputRate == 11025 || inputRate == 16000 || inputRate == 24000)
                {
                    // The QEMU validator requires 95% active samples in
                    // two disjoint 80ms windows. Guard the actual C# output,
                    // not only the pitch, against interpolation-induced zeros.
                    for (int startFrame = 0; startFrame <= 4000; startFrame += 4000)
                    {
                        int active = 0;
                        for (int frame = startFrame; frame < startFrame + 3840; frame++)
                        {
                            int at = frame * 4;
                            short sample = (short)(resampled[at] | (resampled[at + 1] << 8));
                            if (Math.Abs((int)sample) >= 256) active++;
                        }
                        Expect(active * 100 >= 3840 * 95,
                            "8/11.025/16/24kHz resampled tone has sustained 80ms activity");
                    }
                }
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
        try { AudioWavePcm.EncodeStereo16AtRate(new byte[4], 12345); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "unsupported 12.345kHz rate remains rejected");

        // Voice-rate mono PCM8 must decode to the same bounded 48kHz
        // stereo duration without accessing an audio device or a disk.
        foreach (int voiceRate in new[] { 8000, 11025, 16000, 24000 })
        {
            byte[] voicePcm8 = new byte[voiceRate / 4];
            for (int i = 0; i < voicePcm8.Length; i++)
                voicePcm8[i] = (byte)((i & 1) == 0 ? 192 : 64);
            byte[] voiceWave = AudioWavePcm.EncodePcm8AtRate(voicePcm8, voiceRate, 1);
            using var voiceStream = new MemoryStream(voiceWave, false);
            byte[] voiceOutput = AudioWaveInput.DecodeBounded(voiceStream);
            int expectedVoiceFrames = (int)(((long)voicePcm8.Length * 48000 + voiceRate - 1) / voiceRate);
            Expect(voiceOutput.Length == expectedVoiceFrames * 4, "voice-rate PCM8 mono keeps source-frame duration");
            for (int i = 0; i < voiceOutput.Length; i += 4)
                Expect(voiceOutput[i] == voiceOutput[i + 2] &&
                    voiceOutput[i + 1] == voiceOutput[i + 3],
                    "voice-rate mono PCM8 upmix keeps left/right equal");
        }

        // The real PCM16 decoder must preserve channel independence when
        // resampling a 32 kHz RIFF source to the 48 kHz HDA stream.
        byte[] sourceStereo32 = AudioTonePcm.GenerateDualSquareStereo16(32000, 440, 880, 250, 4096);
        byte[] stereoWave32 = AudioWavePcm.EncodeStereo16AtRate(sourceStereo32, 32000);
        byte[] stereoDecoded32;
        using (var stereoStream32 = new MemoryStream(stereoWave32, false))
            stereoDecoded32 = AudioWaveInput.DecodeBounded(stereoStream32);
        Expect(stereoDecoded32.Length == 48000, "32kHz true stereo resamples to 250ms at 48kHz");
        int resampledLeftTransitions = 0;
        int resampledRightTransitions = 0;
        int differentStereoFrames = 0;
        for (int frame = 1; frame < stereoDecoded32.Length / 4; frame++)
        {
            int at = frame * 4;
            int prior = at - 4;
            short l0 = (short)(stereoDecoded32[prior] | (stereoDecoded32[prior + 1] << 8));
            short l1 = (short)(stereoDecoded32[at] | (stereoDecoded32[at + 1] << 8));
            short r0 = (short)(stereoDecoded32[prior + 2] | (stereoDecoded32[prior + 3] << 8));
            short r1 = (short)(stereoDecoded32[at + 2] | (stereoDecoded32[at + 3] << 8));
            if ((l0 < 0) != (l1 < 0)) resampledLeftTransitions++;
            if ((r0 < 0) != (r1 < 0)) resampledRightTransitions++;
            if (l1 != r1) differentStereoFrames++;
        }
        double resampledLeftHz = resampledLeftTransitions * 48000.0 / (2 * (stereoDecoded32.Length / 4 - 1));
        double resampledRightHz = resampledRightTransitions * 48000.0 / (2 * (stereoDecoded32.Length / 4 - 1));
        Expect(Math.Abs(resampledLeftHz - 440) < 12, "32kHz stereo left pitch survives resampling");
        Expect(Math.Abs(resampledRightHz - 880) < 12, "32kHz stereo right pitch survives resampling");
        Expect(differentStereoFrames > 3000, "32kHz stereo channels remain independent");

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

        // Exercise the production master-volume/balance resolver across
        // every valid integer combination. The expected gains are computed
        // independently from the definition of left/right attenuation.
        for (int master = 0; master <= 100; master++)
        {
            for (int balance = -100; balance <= 100; balance++)
            {
                AudioPcmVolume.ResolveStereoGains(
                    master, balance, out int leftGain, out int rightGain);
                int expectedLeft = master * (100 - Math.Max(0, balance)) / 100;
                int expectedRight = master * (100 - Math.Max(0, -balance)) / 100;
                Expect(leftGain == expectedLeft && rightGain == expectedRight,
                    "volume/balance gains match independent integer reference");
                Expect(leftGain >= 0 && leftGain <= master &&
                    rightGain >= 0 && rightGain <= master,
                    "volume/balance never amplifies above master");
            }
        }
        AudioPcmVolume.ResolveStereoGains(25, 50, out int quarterLeft, out int quarterRight);
        Expect(quarterLeft == 12 && quarterRight == 25,
            "non-unity master with partial balance uses bounded integer gain");
        AudioPcmVolume.ResolveStereoGains(100, -100, out int fullLeft, out int silentRight);
        Expect(fullLeft == 100 && silentRight == 0, "left-only balance extreme");
        AudioPcmVolume.ResolveStereoGains(100, 100, out int silentLeft, out int fullRight);
        Expect(silentLeft == 0 && fullRight == 100, "right-only balance extreme");
        foreach (int badMaster in new[] { -1, 101, int.MinValue, int.MaxValue })
        {
            rejected = false;
            try { AudioPcmVolume.ResolveStereoGains(badMaster, 0, out _, out _); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Expect(rejected, "invalid master gain rejected");
        }
        foreach (int badBalance in new[] { -101, 101, int.MinValue, int.MaxValue })
        {
            rejected = false;
            try { AudioPcmVolume.ResolveStereoGains(100, badBalance, out _, out _); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Expect(rejected, "invalid stereo balance rejected");
        }

        // Real production C# channel balance: independent gains, signed
        // extremes and source immutability, without any hardware access.
        byte[] extremes = { 0, 128, 255, 127, 255, 127, 0, 128 };
        byte[] onlyRight = AudioPcmVolume.ScaleStereo16Channels(extremes, 0, 100);
        Expect(onlyRight[0] == 0 && onlyRight[1] == 0 &&
            onlyRight[2] == 255 && onlyRight[3] == 127, "right-only first frame");
        Expect(onlyRight[4] == 0 && onlyRight[5] == 0 &&
            onlyRight[6] == 0 && onlyRight[7] == 128, "right-only signed minimum");
        byte[] onlyLeft = AudioPcmVolume.ScaleStereo16Channels(extremes, 100, 0);
        Expect(onlyLeft[0] == 0 && onlyLeft[1] == 128 &&
            onlyLeft[2] == 0 && onlyLeft[3] == 0, "left-only first frame");
        Expect(onlyLeft[4] == 255 && onlyLeft[5] == 127 &&
            onlyLeft[6] == 0 && onlyLeft[7] == 0, "left-only second frame");
        byte[] halfRight = AudioPcmVolume.ScaleStereo16Channels(extremes, 100, 50);
        Expect(halfRight[0] == 0 && halfRight[1] == 128 &&
            halfRight[2] == 255 && halfRight[3] == 63, "half right gain");
        Expect(halfRight[6] == 0 && halfRight[7] == 192, "half right signed minimum");
        Expect(extremes[0] == 0 && extremes[1] == 128 &&
            extremes[2] == 255 && extremes[3] == 127, "source remains unchanged");
        byte[] equalGain = AudioPcmVolume.ScaleStereo16Channels(pcm, 25, 25);
        for (int i = 0; i < quarter.Length; i++)
            Expect(equalGain[i] == quarter[i], "equal channel gains match master volume");

        // The isolated playback probe exclusively owns each freshly built
        // PCM buffer. Unity gain can reuse it without a second allocation;
        // all non-unity gains must still return an independent scaled copy.
        byte[] reusedUnity = AudioPcmVolume.ScaleStereo16ChannelsOrReuse(extremes, 100, 100);
        Expect(ReferenceEquals(reusedUnity, extremes), "unity gain reuses owned PCM");
        Expect(reusedUnity[0] == 0 && reusedUnity[1] == 128 &&
            reusedUnity[2] == 255 && reusedUnity[3] == 127,
            "unity gain retains exact signed samples");
        byte[] copiedBalance = AudioPcmVolume.ScaleStereo16ChannelsOrReuse(extremes, 100, 50);
        Expect(!ReferenceEquals(copiedBalance, extremes), "non-unity gain makes independent copy");
        for (int i = 0; i < copiedBalance.Length; i++)
            Expect(copiedBalance[i] == halfRight[i], "non-unity fast path matches existing gain");
        byte[] copiedMute = AudioPcmVolume.ScaleStereo16ChannelsOrReuse(extremes, 0, 0);
        Expect(!ReferenceEquals(copiedMute, extremes) &&
            Array.TrueForAll(copiedMute, b => b == 0), "mute never aliases source");
        rejected = false;
        try { AudioPcmVolume.ScaleStereo16ChannelsOrReuse(new byte[3], 100, 100); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "unity fast path rejects incomplete frame");
        rejected = false;
        try { AudioPcmVolume.ScaleStereo16ChannelsOrReuse(extremes, 100, 101); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "unity fast path rejects invalid right gain");
        rejected = false;
        try { AudioPcmVolume.ScaleStereo16ChannelsOrReuse(new byte[48000 * 2 * 4 + 4], 100, 100); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "unity fast path enforces two-second PCM bound");
        rejected = false;
        try { AudioPcmVolume.ScaleStereo16Channels(extremes, -1, 100); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "reject negative left gain");
        rejected = false;
        try { AudioPcmVolume.ScaleStereo16Channels(extremes, 100, 101); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "reject excessive right gain");
        rejected = false;
        try { AudioPcmVolume.ScaleStereo16Channels(new byte[2], 100, 100); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "reject partial stereo frame");

        // Every software-gain entry point must enforce the same 2-second
        // 48 kHz stereo budget before allocating an output buffer.
        byte[] maxGainPcm = new byte[48000 * 2 * 4];
        byte[] maxGainOutput = AudioPcmVolume.ScaleStereo16Channels(maxGainPcm, 100, 0);
        Expect(maxGainOutput.Length == maxGainPcm.Length,
            "accept exactly two seconds of stereo gain input");
        Expect(!ReferenceEquals(maxGainOutput, maxGainPcm),
            "bounded gain returns an independent output");
        byte[] tooLongGainPcm = new byte[48000 * 2 * 4 + 4];
        rejected = false;
        try { AudioPcmVolume.ScaleStereo16(tooLongGainPcm, 100); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "master gain rejects more than two seconds");
        rejected = false;
        try { AudioPcmVolume.ScaleStereo16Channels(tooLongGainPcm, 100, 100); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "channel gain rejects more than two seconds");
        rejected = false;
        try { AudioPcmVolume.ScaleStereo16ChannelsWithFade(tooLongGainPcm, 100, 100, 20); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "combined gain/fade rejects more than two seconds");

        // Production C# PCM envelope: verify both channel edges, plateau,
        // short-clip clamping, signed extremes and immutable input.
        byte[] fadeSource = AudioTonePcm.GenerateDualSquareStereo16(48000, 440, 880, 250, 4096);
        byte[] fadeCopy = (byte[])fadeSource.Clone();
        byte[] faded = AudioPcmVolume.FadeStereo16(fadeSource, 20);
        Expect(faded.Length == fadeSource.Length, "fade preserves frame count");
        int fadeFrames = 20 * 48;
        for (int frame = 0; frame < fadeSource.Length / 4; frame++)
        {
            int at = frame * 4;
            int envelope = Math.Min(fadeFrames, Math.Min(frame, fadeSource.Length / 4 - 1 - frame));
            for (int channel = 0; channel < 2; channel++)
            {
                int offset = at + channel * 2;
                short original = (short)(fadeSource[offset] | (fadeSource[offset + 1] << 8));
                short actual = (short)(faded[offset] | (faded[offset + 1] << 8));
                Expect(actual == original * envelope / fadeFrames, "fade exact signed sample");
                Expect(fadeSource[offset] == fadeCopy[offset] &&
                    fadeSource[offset + 1] == fadeCopy[offset + 1], "fade source immutable");
            }
        }
        byte[] unchangedFade = AudioPcmVolume.FadeStereo16(fadeSource, 0);
        Expect(!ReferenceEquals(unchangedFade, fadeSource), "zero fade returns distinct buffer");
        for (int i = 0; i < fadeSource.Length; i++)
            Expect(unchangedFade[i] == fadeSource[i], "zero fade is bit exact");
        byte[] shortFade = AudioPcmVolume.FadeStereo16(new byte[] { 255, 127, 0, 128 }, 50);
        Expect(shortFade.Length == 4 && shortFade[0] == 0 && shortFade[1] == 0 &&
            shortFade[2] == 0 && shortFade[3] == 0, "single frame fades to silence");
        byte[] twoFrames = AudioPcmVolume.FadeStereo16(new byte[] { 255, 127, 0, 128, 255, 127, 0, 128 }, 50);
        Expect(Array.TrueForAll(twoFrames, b => b == 0), "two-frame fade has no edge transient");
        rejected = false;
        try { AudioPcmVolume.FadeStereo16(fadeSource, -1); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "reject negative fade");
        rejected = false;
        try { AudioPcmVolume.FadeStereo16(fadeSource, 51); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "reject fade above 50ms");
        rejected = false;
        try { AudioPcmVolume.FadeStereo16(new byte[3], 20); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "reject incomplete fade frame");
        rejected = false;
        try { AudioPcmVolume.FadeStereo16(new byte[48000 * 2 * 4 + 4], 20); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "reject fade buffer longer than two seconds");


        // The optimized true-stereo generator must remain bit-for-bit equal
        // to two independently generated reference tones at both source rates.
        foreach (int rate in new[] { 32000, 48000 })
        {
            byte[] referenceLeft = AudioTonePcm.GenerateSquareStereo16(rate, 440, 250, 4096);
            byte[] referenceRight = AudioTonePcm.GenerateSquareStereo16(rate, 880, 250, 4096);
            byte[] generated = AudioTonePcm.GenerateDualSquareStereo16(rate, 440, 880, 250, 4096);
            Expect(generated.Length == referenceLeft.Length, "single-buffer stereo length");
            for (int at = 0; at < generated.Length; at += 4)
            {
                Expect(generated[at] == referenceLeft[at] &&
                    generated[at + 1] == referenceLeft[at + 1],
                    "optimized left channel bit exact");
                Expect(generated[at + 2] == referenceRight[at + 2] &&
                    generated[at + 3] == referenceRight[at + 3],
                    "optimized right channel bit exact");
            }
        }
        // Host .NET allocation measurement, not a Cosmos runtime benchmark.
        AudioTonePcm.GenerateDualSquareStereo16(48000, 440, 880, 250, 4096);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        byte[] oneBuffer = AudioTonePcm.GenerateDualSquareStereo16(48000, 440, 880, 250, 4096);
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Expect(allocatedBytes >= oneBuffer.Length && allocatedBytes < oneBuffer.Length + 1024,
            "dual-tone generator allocates only one PCM output array");
        Console.WriteLine("[AUDIO-PCI-HOST] true-stereo allocated=" + allocatedBytes +
            " bytes for " + oneBuffer.Length + " PCM bytes");
        rejected = false;
        try { AudioTonePcm.GenerateDualSquareStereo16(48000, 440, 24000, 250, 4096); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "optimized right-channel Nyquist guard");
        Expect(AudioTonePcm.GenerateDualSquareStereo16(48000, 440, 880, 2000, 4096).Length == 384000,
            "dual-tone exact two-second PCM allocation boundary");
        rejected = false;
        try { AudioTonePcm.GenerateDualSquareStereo16(96000, 440, 880, 2000, 4096); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "dual-tone high-rate output cap");


        // One-pass gain + envelope must match the two-stage reference for
        // both channels, including negative samples and integer truncation.
        foreach (int fadeMs in new[] { 0, 1, 20, 50 })
        {
            foreach (int leftGain in new[] { 0, 25, 100 })
            {
                foreach (int rightGain in new[] { 0, 50, 100 })
                {
                    byte[] reference = AudioPcmVolume.FadeStereo16(
                        AudioPcmVolume.ScaleStereo16Channels(fadeSource, leftGain, rightGain),
                        fadeMs);
                    byte[] combined = AudioPcmVolume.ScaleStereo16ChannelsWithFade(
                        fadeSource, leftGain, rightGain, fadeMs);
                    Expect(combined.Length == reference.Length, "combined fade length");
                    for (int i = 0; i < combined.Length; i++)
                        Expect(combined[i] == reference[i], "one-pass PCM gain/fade bit exact");
                }
            }
        }
        // This is the actual no-fade path used by volume 0 in the probe.
        // Compare against the pre-optimization sample loop independently.
        foreach (int leftGain in new[] { 0, 25, 100 })
        {
            foreach (int rightGain in new[] { 0, 50, 100 })
            {
                byte[] expected = ReferenceGainFadeDrain(
                    fadeSource, leftGain, rightGain, 0, 0);
                byte[] actual = AudioPcmVolume.ScaleStereo16Channels(
                    fadeSource, leftGain, rightGain);
                Expect(!ReferenceEquals(actual, fadeSource),
                    "direct gain must preserve output ownership");
                Expect(actual.Length == expected.Length, "direct gain length");
                for (int i = 0; i < actual.Length; i++)
                    Expect(actual[i] == expected[i],
                        "direct gain equals independent sample reference");
            }
        }
        foreach (int gain in new[] { 0, 100 })
        {
            int capturedGain = gain;
            double referenceMs = MedianPcmMilliseconds(
                () => ReferenceGainFadeDrain(fadeSource, capturedGain, capturedGain, 0, 0), 96);
            double fastMs = MedianPcmMilliseconds(
                () => AudioPcmVolume.ScaleStereo16Channels(
                    fadeSource, capturedGain, capturedGain), 96);
            Console.WriteLine("[AUDIO-PCI-HOST] direct PCM gain=" + capturedGain +
                "% baseline=" + referenceMs.ToString("F3") + "ms fast=" +
                fastMs.ToString("F3") + "ms iterations=96 (host only; no speed assertion)");
        }
        // Compare the new mute/unity fast paths against an independent
        // per-sample implementation, including negative PCM and zero tails.
        byte[] fastPathSource = (byte[])fadeSource.Clone();
        foreach (int fadeMs in new[] { 0, 20 })
        {
            foreach (int leftGain in new[] { 0, 25, 100 })
            {
                foreach (int rightGain in new[] { 0, 50, 100 })
                {
                    foreach (int tail in new[] { 0, 16 * 1024 })
                    {
                        byte[] expected = ReferenceGainFadeDrain(
                            fastPathSource, leftGain, rightGain, fadeMs, tail);
                        byte[] actual = AudioPcmVolume.ScaleStereo16ChannelsWithFadeAndSilence(
                            fastPathSource, leftGain, rightGain, fadeMs, tail);
                        Expect(!ReferenceEquals(actual, fastPathSource),
                            "gain/fade drain output must not alias caller PCM");
                        Expect(actual.Length == expected.Length,
                            "gain/fade drain length matches independent reference");
                        for (int i = 0; i < actual.Length; i++)
                            Expect(actual[i] == expected[i],
                                "gain/fade drain matches independent reference PCM");
                    }
                }
            }
        }
        for (int i = 0; i < fastPathSource.Length; i++)
            Expect(fastPathSource[i] == fadeSource[i],
                "gain/fade drain fast paths preserve caller PCM");
        const int benchIterations = 96;
        foreach (int gain in new[] { 0, 100 })
        {
            // Only the 0% and 100% no-fade cases take the new fast paths.
            // These are host-side measurements, not HDA or QEMU throughput.
            int capturedGain = gain;
            double referenceMs = MedianPcmMilliseconds(
                () => ReferenceGainFadeDrain(fastPathSource, capturedGain, capturedGain, 0, 16384),
                benchIterations);
            double fastMs = MedianPcmMilliseconds(
                () => AudioPcmVolume.ScaleStereo16ChannelsWithFadeAndSilence(
                    fastPathSource, capturedGain, capturedGain, 0, 16384),
                benchIterations);
            Console.WriteLine("[AUDIO-PCI-HOST] PCM drain gain=" + capturedGain +
                "% baseline=" + referenceMs.ToString("F3") + "ms fast=" +
                fastMs.ToString("F3") + "ms iterations=" + benchIterations +
                " (host only; no speed assertion)");
        }
        // The opt-in HDA fade path reserves exactly one 16 KiB DMA-ring
        // duration of silence, without losing the release envelope or
        // allocating a second intermediate PCM buffer.
        const int hdaSilentTail = 16 * 1024;
        byte[] fadedWithDrain = AudioPcmVolume.ScaleStereo16ChannelsWithFadeAndSilence(
            fadeSource, 75, 25, 20, hdaSilentTail);
        byte[] fadedReference = AudioPcmVolume.ScaleStereo16ChannelsWithFade(
            fadeSource, 75, 25, 20);
        Expect(fadedWithDrain.Length == fadedReference.Length + hdaSilentTail,
            "fade HDA ring drain has exact bounded length");
        for (int i = 0; i < fadedReference.Length; i++)
            Expect(fadedWithDrain[i] == fadedReference[i],
                "fade ring drain preserves every actual PCM sample");
        for (int i = fadedReference.Length; i < fadedWithDrain.Length; i++)
            Expect(fadedWithDrain[i] == 0, "fade ring drain contains only silence");
        Expect(fadeSource[0] == fadeCopy[0] && fadeSource[fadeSource.Length - 1] ==
            fadeCopy[fadeCopy.Length - 1], "ring drain does not mutate input");
        byte[] maxDrainInput = new byte[48000 * 2 * 4 - hdaSilentTail];
        byte[] maxDrainOutput = AudioPcmVolume.ScaleStereo16ChannelsWithFadeAndSilence(
            maxDrainInput, 100, 100, 20, hdaSilentTail);
        Expect(maxDrainOutput.Length == 48000 * 2 * 4,
            "fade drain accepts exact two-second total budget");
        byte[] fullLengthFade = AudioPcmVolume.ScaleStereo16ChannelsWithFadeAndSilence(
            maxGainPcm, 100, 100, 20, 0);
        Expect(fullLengthFade.Length == maxGainPcm.Length,
            "two-second fade remains valid when no silent headroom exists");
        byte[] nearFullLengthFade = AudioPcmVolume.ScaleStereo16ChannelsWithFadeAndSilence(
            new byte[48000 * 2 * 4 - 4], 100, 100, 20, 4);
        Expect(nearFullLengthFade.Length == 48000 * 2 * 4 &&
            nearFullLengthFade[nearFullLengthFade.Length - 1] == 0,
            "near-two-second fade accepts a frame of silent headroom");
        rejected = false;
        try
        {
            AudioPcmVolume.ScaleStereo16ChannelsWithFadeAndSilence(
                new byte[48000 * 2 * 4 - hdaSilentTail + 4], 100, 100, 20, hdaSilentTail);
        }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "fade drain rejects input exceeding total two-second budget");
        foreach (int invalidTail in new[] { -4, 1, 16 * 1024 + 4 })
        {
            rejected = false;
            try
            {
                AudioPcmVolume.ScaleStereo16ChannelsWithFadeAndSilence(
                    fadeSource, 100, 100, 20, invalidTail);
            }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Expect(rejected, "fade drain rejects invalid silence length");
        }
        AudioPcmVolume.ScaleStereo16ChannelsWithFadeAndSilence(
            fadeSource, 75, 25, 20, hdaSilentTail);
        long drainBefore = GC.GetAllocatedBytesForCurrentThread();
        byte[] measuredDrain = AudioPcmVolume.ScaleStereo16ChannelsWithFadeAndSilence(
            fadeSource, 75, 25, 20, hdaSilentTail);
        long drainAllocated = GC.GetAllocatedBytesForCurrentThread() - drainBefore;
        Expect(drainAllocated >= measuredDrain.Length &&
            drainAllocated < measuredDrain.Length + 1024,
            "fade ring drain uses one PCM allocation");
        Console.WriteLine("[AUDIO-PCI-HOST] fade ring drain allocated=" +
            drainAllocated + " bytes for " + measuredDrain.Length + " PCM bytes");

        AudioPcmVolume.ScaleStereo16ChannelsWithFade(fadeSource, 100, 100, 20);
        long combinedBefore = GC.GetAllocatedBytesForCurrentThread();
        byte[] combinedMeasured = AudioPcmVolume.ScaleStereo16ChannelsWithFade(
            fadeSource, 75, 25, 20);
        long combinedAllocated = GC.GetAllocatedBytesForCurrentThread() - combinedBefore;
        Expect(combinedAllocated >= combinedMeasured.Length &&
            combinedAllocated < combinedMeasured.Length + 1024,
            "combined gain/fade uses one PCM allocation");
        Console.WriteLine("[AUDIO-PCI-HOST] combined gain/fade allocated=" +
            combinedAllocated + " bytes for " + combinedMeasured.Length + " PCM bytes");
        rejected = false;
        try { AudioPcmVolume.ScaleStereo16ChannelsWithFade(fadeSource, 100, 100, 51); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "combined gain/fade rejects invalid envelope");
        rejected = false;
        try { AudioPcmVolume.ScaleStereo16ChannelsWithFade(fadeSource, 101, 100, 20); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "combined gain/fade rejects invalid gain");

        // Independent PCM clips can overlap without wrapping signed PCM16.
        // This is pure host-side arithmetic; the QEMU HDA capture is separate.
        byte[] mixA = { 0xff, 0x7f, 0x00, 0x80, 0x10, 0x27, 0x10, 0x27 };
        byte[] mixB = { 0x01, 0x00, 0xff, 0xff, 0x10, 0x27, 0xf0, 0xd8 };
        byte[] mixACopy = (byte[])mixA.Clone();
        byte[] mixBCopy = (byte[])mixB.Clone();
        byte[] mixed = AudioPcmMixer.MixStereo16(mixA, mixB, 100, 100);
        Expect(mixed.Length == 8, "mix retains the longest clip duration");
        Expect(BitConverter.ToInt16(mixed, 0) == 32767, "mix saturates positive PCM16");
        Expect(BitConverter.ToInt16(mixed, 2) == -32768, "mix saturates negative PCM16");
        Expect(BitConverter.ToInt16(mixed, 4) == 20000, "mix sums independent sources");
        Expect(BitConverter.ToInt16(mixed, 6) == 0, "mix cancels opposite-polarity samples");
        Expect(mixA.AsSpan().SequenceEqual(mixACopy) &&
            mixB.AsSpan().SequenceEqual(mixBCopy), "mix leaves both sources untouched");

        byte[] halfMix = AudioPcmMixer.MixStereo16(mixA, mixB, 50, 100);
        Expect(BitConverter.ToInt16(halfMix, 0) == 16384, "mix applies first gain before addition");
        Expect(BitConverter.ToInt16(halfMix, 2) == -16385, "mix rounds negative gain toward zero");
        byte[] mutedMix = AudioPcmMixer.MixStereo16(mixA, mixB, 0, 0);
        Expect(Array.TrueForAll(mutedMix, value => value == 0), "two muted sources produce silence");

        byte[] shorterMix = AudioPcmMixer.MixStereo16(mixA, new byte[4], 100, 100);
        Expect(shorterMix.Length == mixA.Length, "mix retains longer first clip");
        Expect(BitConverter.ToInt16(shorterMix, 4) == 10000 &&
            BitConverter.ToInt16(shorterMix, 6) == 10000, "shorter second clip becomes silence");
        byte[] longerMix = AudioPcmMixer.MixStereo16(new byte[4], mixB, 100, 100);
        Expect(longerMix.Length == mixB.Length &&
            BitConverter.ToInt16(longerMix, 4) == 10000, "shorter first clip becomes silence");

        byte[] maxMix = AudioPcmMixer.MixStereo16(new byte[384000], new byte[4], 100, 100);
        Expect(maxMix.Length == 384000, "mix accepts exactly two seconds");
        rejected = false;
        try { AudioPcmMixer.MixStereo16(new byte[384004], new byte[4], 100, 100); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "mix rejects clips over two seconds");
        rejected = false;
        try { AudioPcmMixer.MixStereo16(new byte[3], new byte[4], 100, 100); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "mix rejects partial PCM frames");
        rejected = false;
        try { AudioPcmMixer.MixStereo16(Array.Empty<byte>(), new byte[4], 100, 100); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "mix rejects empty sources");
        rejected = false;
        try { AudioPcmMixer.MixStereo16(null, new byte[4], 100, 100); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "mix rejects null sources");
        rejected = false;
        try { AudioPcmMixer.MixStereo16(mixA, mixB, -1, 100); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "mix rejects negative first gain");
        rejected = false;
        try { AudioPcmMixer.MixStereo16(mixA, mixB, 100, 101); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "mix rejects second gain over 100");
        // Delayed mixing must preserve whole PCM frames and source immutability.
        // Frame zero is first-only; frame one overlaps; frame two is second-only.
        byte[] delayed = AudioPcmMixer.MixStereo16WithDelay(mixA, mixB, 100, 100, 1);
        Expect(delayed.Length == 12, "one-frame delay extends mixed duration");
        Expect(BitConverter.ToInt16(delayed, 0) == 32767 &&
            BitConverter.ToInt16(delayed, 2) == -32768, "delay preserves first-only frame");
        Expect(BitConverter.ToInt16(delayed, 4) == 10001 &&
            BitConverter.ToInt16(delayed, 6) == 9999, "delay mixes only overlapping frame");
        Expect(BitConverter.ToInt16(delayed, 8) == 10000 &&
            BitConverter.ToInt16(delayed, 10) == -10000, "delay preserves second-only tail");
        Expect(mixA.AsSpan().SequenceEqual(mixACopy) &&
            mixB.AsSpan().SequenceEqual(mixBCopy), "delay leaves both inputs unchanged");
        Expect(AudioPcmMixer.MixStereo16WithDelay(mixA, mixB, 100, 100, 0)
            .AsSpan().SequenceEqual(mixed), "zero-delay mixing is byte-exact with legacy API");
        byte[] delayedNoOverlap = AudioPcmMixer.MixStereo16WithDelay(
            mixA, mixB, 100, 100, 2);
        Expect(delayedNoOverlap.Length == 16 &&
            BitConverter.ToInt16(delayedNoOverlap, 8) == 1 &&
            BitConverter.ToInt16(delayedNoOverlap, 10) == -1,
            "non-overlapping clips retain independent samples");
        byte[] delayedBoundary = AudioPcmMixer.MixStereo16WithDelay(
            new byte[4], new byte[4], 100, 100, 95999);
        Expect(delayedBoundary.Length == 384000, "delay accepts exact two-second boundary");
        rejected = false;
        try { AudioPcmMixer.MixStereo16WithDelay(mixA, mixB, 100, 100, -1); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "delay rejects negative frame offset");
        rejected = false;
        try { AudioPcmMixer.MixStereo16WithDelay(mixA, mixB, 100, 100, 96000); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "delay rejects clip extending beyond output budget");
        byte[] longFirstMix = AudioPcmMixer.MixStereo16WithDelay(
            new byte[384000], new byte[4], 100, 100, 1);
        Expect(longFirstMix.Length == 384000,
            "delayed short second clip fits within existing long first clip");
        rejected = false;
        try { AudioPcmMixer.MixStereo16WithDelay(new byte[4], new byte[384000],
            100, 100, 1); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "delay refuses truncation of second clip");

        // Reproduce the exact two independent 440/880 Hz sources used by
        // the isolated QEMU 'mix' command, then compare every PCM byte with
        // the already validated true-stereo reference generator.
        byte[] mixLeft = AudioTonePcm.GenerateSquareStereo16(48000, 440, 250, 4096);
        byte[] mixRight = AudioTonePcm.GenerateSquareStereo16(48000, 880, 250, 4096);
        for (int at = 0; at < mixLeft.Length; at += 4)
        {
            mixLeft[at + 2] = 0;
            mixLeft[at + 3] = 0;
            mixRight[at] = 0;
            mixRight[at + 1] = 0;
        }
        byte[] mixedStereoProbe = AudioPcmMixer.MixStereo16(mixLeft, mixRight, 100, 100);
        byte[] referenceStereoProbe = AudioTonePcm.GenerateDualSquareStereo16(48000, 440, 880, 250, 4096);
        Expect(mixedStereoProbe.AsSpan().SequenceEqual(referenceStereoProbe),
            "mixed stereo probe matches independent 440/880 Hz reference byte-for-byte");

        // Exact PCM fixture used by QEMU mixdelay: 2400 frames = 50 ms.
        // Check channel onset and tail at frame boundaries before HDA playback.
        byte[] delayedStereoProbe = AudioPcmMixer.MixStereo16WithDelay(
            mixLeft, mixRight, 100, 100, 2400);
        Expect(delayedStereoProbe.Length == 57600,
            "50ms delay extends 250ms fixture to exactly 300ms");
        foreach (int frame in new[] { 0, 2399 })
        {
            int at = frame * 4;
            Expect(BitConverter.ToInt16(delayedStereoProbe, at + 2) == 0,
                "right channel remains silent before delayed onset");
            Expect(BitConverter.ToInt16(delayedStereoProbe, at) ==
                BitConverter.ToInt16(mixLeft, at), "left begins immediately");
        }
        foreach (int frame in new[] { 2400, 11999 })
        {
            int at = frame * 4;
            Expect(BitConverter.ToInt16(delayedStereoProbe, at + 2) ==
                BitConverter.ToInt16(mixRight, at - 2400 * 4 + 2),
                "right source starts on exact scheduled PCM frame");
        }
        foreach (int frame in new[] { 12000, 14399 })
        {
            int at = frame * 4;
            Expect(BitConverter.ToInt16(delayedStereoProbe, at) == 0,
                "left clip ends after its original 250ms duration");
            Expect(BitConverter.ToInt16(delayedStereoProbe, at + 2) ==
                BitConverter.ToInt16(mixRight, at - 2400 * 4 + 2),
                "right clip retains its 50ms trailing interval");
        }
        // Multi-effect GUI scheduling is a pure bounded PCM operation. Verify
        // that simultaneous clipping is applied only after all sources sum.
        byte[] positive30000 = { 0x30, 0x75, 0x30, 0x75 };
        byte[] negative30000 = { 0xd0, 0x8a, 0xd0, 0x8a };
        byte[] scheduledCancellation = AudioPcmMixer.MixScheduledStereo16(
            new[] { positive30000, positive30000, negative30000 },
            new[] { 0, 0, 0 }, new[] { 100, 100, 100 });
        Expect(BitConverter.ToInt16(scheduledCancellation, 0) == 30000 &&
            BitConverter.ToInt16(scheduledCancellation, 2) == 30000,
            "three-source cancellation happens before final PCM16 saturation");
        byte[] reorderedCancellation = AudioPcmMixer.MixScheduledStereo16(
            new[] { negative30000, positive30000, positive30000 },
            new[] { 0, 0, 0 }, new[] { 100, 100, 100 });
        Expect(scheduledCancellation.AsSpan().SequenceEqual(reorderedCancellation),
            "scheduled mixer does not depend on source order");
        byte[] scheduledTwo = AudioPcmMixer.MixScheduledStereo16(
            new[] { mixA, mixB }, new[] { 0, 1 }, new[] { 100, 100 });
        Expect(scheduledTwo.AsSpan().SequenceEqual(delayed),
            "two-source schedule matches existing delayed mixer byte-for-byte");
        // Regression: an HDA startup pre-roll must preserve the exact
        // scheduled sample positions without introducing extra audio.
        const int preRollFrames = 12000; // 250 ms at 48 kHz
        byte[] preRolled = AudioPcmMixer.MixScheduledStereo16(
            new[] { mixA, mixB }, new[] { preRollFrames, preRollFrames + 1 },
            new[] { 100, 100 });
        Expect(preRolled.Length == preRollFrames * 4 + delayed.Length,
            "HDA pre-roll extends the scheduled output by exactly 250ms");
        bool leadingSilence = true;
        for (int at = 0; at < preRollFrames * 4; at++)
        {
            if (preRolled[at] != 0) { leadingSilence = false; break; }
        }
        Expect(leadingSilence, "HDA pre-roll contains only silent PCM frames");
        Expect(preRolled.AsSpan(preRollFrames * 4).SequenceEqual(delayed),
            "HDA pre-roll preserves the complete mixed PCM waveform");
        byte[] thirdScheduled = AudioPcmMixer.MixScheduledStereo16(
            new[] { mixA, mixB, positive30000 },
            new[] { 0, 1, 3 }, new[] { 100, 50, 50 });
        Expect(thirdScheduled.Length == 16, "three clips extend timeline to last scheduled frame");
        Expect(BitConverter.ToInt16(thirdScheduled, 0) == 32767 &&
            BitConverter.ToInt16(thirdScheduled, 2) == -32768,
            "first frame remains untouched by future clips");
        Expect(BitConverter.ToInt16(thirdScheduled, 4) == 10000 &&
            BitConverter.ToInt16(thirdScheduled, 6) == 10000,
            "second frame applies independent gain to delayed clip");
        Expect(BitConverter.ToInt16(thirdScheduled, 12) == 15000 &&
            BitConverter.ToInt16(thirdScheduled, 14) == 15000,
            "third scheduled clip begins on its exact frame");
        Expect(mixA.AsSpan().SequenceEqual(mixACopy) &&
            mixB.AsSpan().SequenceEqual(mixBCopy) &&
            positive30000[0] == 0x30 && negative30000[0] == 0xd0,
            "multi-source schedule never mutates input PCM");

        byte[] scheduledMuted = AudioPcmMixer.MixScheduledStereo16(
            new[] { mixA, mixB }, new[] { 0, 3 }, new[] { 0, 0 });
        Expect(scheduledMuted.Length == 20 &&
            Array.TrueForAll(scheduledMuted, value => value == 0),
            "muted clips retain scheduled timeline but produce silence");
        byte[] scheduledBoundary = AudioPcmMixer.MixScheduledStereo16(
            new[] { new byte[4] }, new[] { 95999 }, new[] { 100 });
        Expect(scheduledBoundary.Length == 384000,
            "schedule accepts a final single frame at the two-second boundary");
        rejected = false;
        try { AudioPcmMixer.MixScheduledStereo16(
            new[] { new byte[8] }, new[] { 95999 }, new[] { 100 }); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "schedule refuses a clip extending beyond the two-second limit");
        rejected = false;
        try { AudioPcmMixer.MixScheduledStereo16(
            new[] { mixA }, new[] { -1 }, new[] { 100 }); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "schedule rejects negative frame offsets");
        rejected = false;
        try { AudioPcmMixer.MixScheduledStereo16(
            new[] { mixA }, new[] { 0 }, new[] { 101 }); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "schedule rejects gains outside 0..100");
        rejected = false;
        try { AudioPcmMixer.MixScheduledStereo16(
            new[] { mixA }, Array.Empty<int>(), new[] { 100 }); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "schedule rejects mismatched array lengths");
        rejected = false;
        try { AudioPcmMixer.MixScheduledStereo16(
            new byte[9][], new int[9], new int[9]); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "schedule rejects more than eight sources");
        rejected = false;
        try { AudioPcmMixer.MixScheduledStereo16(
            new[] { new byte[3] }, new[] { 0 }, new[] { 100 }); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "schedule rejects partial PCM frames");
        rejected = false;
        try { AudioPcmMixer.MixScheduledStereo16(
            new byte[][] { null }, new[] { 0 }, new[] { 100 }); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "schedule rejects null clips");
        byte[][] measuredScheduleClips = { mixA, mixB, positive30000 };
        int[] measuredScheduleStarts = { 0, 1, 3 };
        int[] measuredScheduleGains = { 100, 50, 50 };
        AudioPcmMixer.MixScheduledStereo16(measuredScheduleClips,
            measuredScheduleStarts, measuredScheduleGains);
        long scheduledBefore = GC.GetAllocatedBytesForCurrentThread();
        byte[] scheduledMeasured = AudioPcmMixer.MixScheduledStereo16(
            measuredScheduleClips, measuredScheduleStarts, measuredScheduleGains);
        long scheduledAllocated = GC.GetAllocatedBytesForCurrentThread() - scheduledBefore;
        Expect(scheduledAllocated >= scheduledMeasured.Length &&
            scheduledAllocated < scheduledMeasured.Length + 1024,
            "scheduled mixer allocates only the bounded output PCM buffer");


        // Per-effect attack/release prevents hard-edged GUI effects while
        // preserving exact scheduling and one final saturating mix.
        byte[] zeroFade = AudioPcmMixer.MixScheduledStereo16WithFade(
            measuredScheduleClips, measuredScheduleStarts, measuredScheduleGains, 0);
        Expect(zeroFade.AsSpan().SequenceEqual(scheduledMeasured),
            "zero fade is byte-exact with the original scheduled mixer");
        byte[] envelopeClip = new byte[16];
        for (int frame = 0; frame < 4; frame++)
        {
            int at = frame * 4;
            envelopeClip[at] = 0xe0; envelopeClip[at + 1] = 0x2e; // +12000
            envelopeClip[at + 2] = 0x20; envelopeClip[at + 3] = 0xd1; // -12000
        }
        byte[] envelopeCopy = (byte[])envelopeClip.Clone();
        byte[] fadedSchedule = AudioPcmMixer.MixScheduledStereo16WithFade(
            new[] { envelopeClip }, new[] { 2 }, new[] { 50 }, 5);
        Expect(fadedSchedule.Length == 24,
            "fade preserves scheduled onset and output duration");
        foreach (int frame in new[] { 0, 1, 2, 5 })
        {
            Expect(BitConverter.ToInt16(fadedSchedule, frame * 4) == 0 &&
                BitConverter.ToInt16(fadedSchedule, frame * 4 + 2) == 0,
                "per-clip fade has silent edges and silent pre-roll");
        }
        foreach (int frame in new[] { 3, 4 })
        {
            Expect(BitConverter.ToInt16(fadedSchedule, frame * 4) == 3000 &&
                BitConverter.ToInt16(fadedSchedule, frame * 4 + 2) == -3000,
                "gain is applied before symmetric per-clip fade");
        }
        Expect(envelopeClip.AsSpan().SequenceEqual(envelopeCopy),
            "per-clip fade never mutates source PCM");

        byte[] fadedCancellation = AudioPcmMixer.MixScheduledStereo16WithFade(
            new[] { envelopeClip, envelopeClip, envelopeClip },
            new[] { 0, 0, 0 }, new[] { 100, 100, 100 }, 5);
        Expect(BitConverter.ToInt16(fadedCancellation, 4) == 18000 &&
            BitConverter.ToInt16(fadedCancellation, 6) == -18000,
            "three faded contributions are summed before final clipping");
        byte[] reversedCancellation = AudioPcmMixer.MixScheduledStereo16WithFade(
            new[] { envelopeClip, envelopeClip, envelopeClip },
            new[] { 0, 0, 0 }, new[] { 100, 100, 100 }, 5);
        Expect(fadedCancellation.AsSpan().SequenceEqual(reversedCancellation),
            "faded scheduled mix is deterministic");

        byte[] oneFrameFade = AudioPcmMixer.MixScheduledStereo16WithFade(
            new[] { envelopeClip.AsSpan(0, 4).ToArray() },
            new[] { 95999 }, new[] { 100 }, 5);
        Expect(oneFrameFade.Length == 384000 &&
            oneFrameFade[oneFrameFade.Length - 4] == 0,
            "one-frame effect at exact two-second boundary fades to silence");
        rejected = false;
        try { AudioPcmMixer.MixScheduledStereo16WithFade(
            new[] { envelopeClip }, new[] { 0 }, new[] { 100 }, -1); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "reject negative per-effect fade");
        rejected = false;
        try { AudioPcmMixer.MixScheduledStereo16WithFade(
            new[] { envelopeClip }, new[] { 0 }, new[] { 100 }, 51); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "reject per-effect fade over 50 ms");
        rejected = false;
        try { AudioPcmMixer.MixScheduledStereo16WithFade(
            new[] { envelopeClip }, new[] { 95999 }, new[] { 100 }, 5); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "faded schedule cannot overrun two-second budget");
        rejected = false;
        try { AudioPcmMixer.MixScheduledStereo16WithFade(
            new[] { new byte[3] }, new[] { 0 }, new[] { 100 }, 5); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "faded schedule rejects incomplete PCM frames");
        rejected = false;
        try { AudioPcmMixer.MixScheduledStereo16WithFade(
            new[] { envelopeClip }, new[] { 0 }, new[] { 101 }, 5); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "faded schedule rejects invalid gain");
        byte[][] fadeMeasuredClips = { envelopeClip, envelopeClip };
        int[] fadeMeasuredStarts = { 0, 1 };
        int[] fadeMeasuredGains = { 100, 50 };
        AudioPcmMixer.MixScheduledStereo16WithFade(
            fadeMeasuredClips, fadeMeasuredStarts, fadeMeasuredGains, 5);
        long fadeBefore = GC.GetAllocatedBytesForCurrentThread();
        byte[] fadeMeasured = AudioPcmMixer.MixScheduledStereo16WithFade(
            fadeMeasuredClips, fadeMeasuredStarts, fadeMeasuredGains, 5);
        long fadeAllocated = GC.GetAllocatedBytesForCurrentThread() - fadeBefore;
        Expect(fadeAllocated >= fadeMeasured.Length &&
            fadeAllocated < fadeMeasured.Length + 1024,
            "per-effect fade allocates only its bounded output PCM array");

        byte[] fadedWithTail = AudioPcmMixer.MixScheduledStereo16WithFade(
            new[] { envelopeClip }, new[] { 2 }, new[] { 50 }, 5, 4096);
        Expect(fadedWithTail.Length == fadedSchedule.Length + 16384 &&
            fadedWithTail.AsSpan(0, fadedSchedule.Length).SequenceEqual(fadedSchedule),
            "16KiB HDA drain tail preserves exact scheduled PCM prefix");
        bool tailZero = true;
        for (int i = fadedSchedule.Length; i < fadedWithTail.Length; i++)
            if (fadedWithTail[i] != 0) { tailZero = false; break; }
        Expect(tailZero, "scheduled PCM drain tail is entirely silent");
        byte[] zeroFadeWithTail = AudioPcmMixer.MixScheduledStereo16WithFade(
            new[] { envelopeClip }, new[] { 0 }, new[] { 100 }, 0, 4);
        Expect(zeroFadeWithTail.Length == envelopeClip.Length + 16 &&
            zeroFadeWithTail.AsSpan(0, envelopeClip.Length).SequenceEqual(envelopeClip),
            "zero-fade scheduling also supports a silent tail without modifying PCM");
        rejected = false;
        try { AudioPcmMixer.MixScheduledStereo16WithFade(
            new[] { envelopeClip }, new[] { 0 }, new[] { 100 }, 5, 4097); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "reject drain tail larger than the stock 16KiB HDA ring");
        rejected = false;
        try { AudioPcmMixer.MixScheduledStereo16WithFade(
            new[] { new byte[384000] }, new[] { 0 }, new[] { 100 }, 5, 1); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "reject silent drain exceeding the two-second PCM budget");

        // Independent fades allow short notifications and longer alerts to
        // share one frame-accurate PCM schedule without extra clip buffers.
        byte[][] individualClips = { envelopeClip, envelopeClip };
        int[] individualStarts = { 0, 0 };
        int[] individualGains = { 100, 100 };
        int[] individualFades = { 5, 0 };
        byte[] individualMixed = AudioPcmMixer.MixScheduledStereo16WithIndividualFades(
            individualClips, individualStarts, individualGains, individualFades);
        Expect(individualMixed.Length == envelopeClip.Length,
            "independent fades preserve the scheduled duration");
        foreach (int frame in new[] { 0, 3 })
        {
            Expect(BitConverter.ToInt16(individualMixed, frame * 4) == 12000 &&
                BitConverter.ToInt16(individualMixed, frame * 4 + 2) == -12000,
                "unfaded effect remains audible at faded clip boundaries");
        }
        foreach (int frame in new[] { 1, 2 })
        {
            Expect(BitConverter.ToInt16(individualMixed, frame * 4) == 18000 &&
                BitConverter.ToInt16(individualMixed, frame * 4 + 2) == -18000,
                "independent fades sum before final clipping");
        }
        byte[] equalFades = AudioPcmMixer.MixScheduledStereo16WithIndividualFades(
            individualClips, individualStarts, individualGains, new[] { 5, 5 });
        byte[] uniformFades = AudioPcmMixer.MixScheduledStereo16WithFade(
            individualClips, individualStarts, individualGains, 5);
        Expect(equalFades.AsSpan().SequenceEqual(uniformFades),
            "identical per-clip fades match the original uniform envelope exactly");
        byte[] noIndividualFades = AudioPcmMixer.MixScheduledStereo16WithIndividualFades(
            individualClips, individualStarts, individualGains, new[] { 0, 0 });
        Expect(noIndividualFades.AsSpan().SequenceEqual(
            AudioPcmMixer.MixScheduledStereo16(individualClips, individualStarts, individualGains)),
            "zero individual fades match the original unfaded mixer exactly");
        byte[] individualTail = AudioPcmMixer.MixScheduledStereo16WithIndividualFades(
            individualClips, individualStarts, individualGains, individualFades, 4);
        Expect(individualTail.Length == individualMixed.Length + 16 &&
            individualTail.AsSpan(0, individualMixed.Length).SequenceEqual(individualMixed) &&
            individualTail.AsSpan(individualMixed.Length).IndexOfAnyExcept((byte)0) == -1,
            "independent fades retain output and add only silent drain frames");
        Expect(envelopeClip.AsSpan().SequenceEqual(envelopeCopy) &&
            individualFades[0] == 5 && individualFades[1] == 0,
            "independent fades do not mutate source PCM or scheduling parameters");
        rejected = false;
        try { AudioPcmMixer.MixScheduledStereo16WithIndividualFades(
            individualClips, individualStarts, individualGains, new[] { 5 }); }
        catch (ArgumentException) { rejected = true; }
        Expect(rejected, "independent fades reject mismatched parameter lengths");
        rejected = false;
        try { AudioPcmMixer.MixScheduledStereo16WithIndividualFades(
            individualClips, individualStarts, individualGains, new[] { 5, 51 }); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "independent fades reject overlong release");
        rejected = false;
        try { AudioPcmMixer.MixScheduledStereo16WithIndividualFades(
            individualClips, individualStarts, individualGains, new[] { -1, 5 }); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "independent fades reject negative attack");
        rejected = false;
        try { AudioPcmMixer.MixScheduledStereo16WithIndividualFades(
            individualClips, individualStarts, individualGains, individualFades, 4097); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "independent fades reject oversized HDA drain");
        rejected = false;
        try { AudioPcmMixer.MixScheduledStereo16WithIndividualFades(
            new[] { new byte[384000] }, new[] { 0 }, new[] { 100 }, new[] { 5 }, 1); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Expect(rejected, "independent fades reject PCM plus drain beyond two seconds");
        AudioPcmMixer.MixScheduledStereo16WithIndividualFades(
            individualClips, individualStarts, individualGains, individualFades);
        long individualBefore = GC.GetAllocatedBytesForCurrentThread();
        byte[] individualMeasured = AudioPcmMixer.MixScheduledStereo16WithIndividualFades(
            individualClips, individualStarts, individualGains, individualFades);
        long individualAllocated = GC.GetAllocatedBytesForCurrentThread() - individualBefore;
        Expect(individualAllocated >= individualMeasured.Length &&
            individualAllocated < individualMeasured.Length + 1024,
            "independent fades allocate only one bounded PCM output buffer");

        AudioMixerFastPathSmoke.Run();
        AudioProbeFeederSmoke.Run();
        AudioRingTimelineSmoke.Run(Expect);
        AudioDirectProbeSmoke.Run(Expect);

        Console.WriteLine("[AUDIO-PCI-HOST] PCM16 mixer saturation, gains, bounds and source immutability PASS");

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
