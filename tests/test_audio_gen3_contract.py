#!/usr/bin/env python3
"""Static safety gates for the opt-in Gen3 audio probe; not a boot/audio test."""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return (ROOT / path).read_text(encoding="utf-8")


class AudioGen3Contract(unittest.TestCase):
    def test_stock_cosmos_and_disabled_production(self):
        project = ET.parse(ROOT / "ZonderqOS.csproj").getroot()
        self.assertEqual(project.attrib["Sdk"], "Cosmos.Sdk/3.0.95")
        self.assertIn('<CosmosEnableNetwork>false</CosmosEnableNetwork>', read("ZonderqOS.csproj"))
        self.assertIn('<CosmosEnableAudio Condition="\'$(ZonderqAudioProbe)\' != \'true\'">false</CosmosEnableAudio>',
                      read("ZonderqOS.csproj"))
        self.assertIn('<CosmosEnableAudio Condition="\'$(ZonderqAudioProbe)\' == \'true\'">true</CosmosEnableAudio>',
                      read("ZonderqOS.csproj"))
        self.assertIn("ZONDERQ_AUDIO_PROBE", read("ZonderqOS.csproj"))

    def test_probe_requires_diskless_profile(self):
        project = read("ZonderqOS.csproj")
        self.assertIn("GuardAudioProbeProfile", project)
        self.assertIn("ZonderqReadOnlyHardwareProbe)' != 'true'", project)
        self.assertIn("ZonderqRamRoot)' == 'true'", project)
        self.assertIn("ZonderqInputDiagnosticBoot)' == 'true'", project)
        kernel = read("Kernel.cs")
        self.assertIn("#if ZONDERQ_READONLY_HARDWARE_PROBE", kernel)
        self.assertIn("RunReadOnlyInputConsole();", kernel)
        self.assertIn("if (!Disk.Initialize())", kernel)
        self.assertLess(kernel.index("RunReadOnlyInputConsole();"), kernel.index("if (!SecurityContext.IsAuthenticated)"))

    def test_pci_inventory_does_not_bind_or_write(self):
        diag = read("source/Core/AudioPciDiagnostics.cs")
        self.assertIn("PciSysfsProvider.TryDiscover(out devices)", diag)
        self.assertIn("classCode == 0x04 && (subclass == 0x01 || subclass == 0x03)", diag)
        self.assertIn("d.ProgrammingInterface.ToString(\"X2\")", diag)
        self.assertIn("driver=UNVERIFIED", diag)
        for forbidden in ("WriteBlock(", "TryMount(", "AllocateDma(", "EnableBusMastering(",
                          "MapRegisters(", "NetworkManager.", "Disk.Initialize(", "File.Write"):
            self.assertNotIn(forbidden, diag)

    def test_audio_command_wired_in_both_shells(self):
        shell = read("source/Shell/Command.cs")
        cmd = read("source/Shell/Commands/CmdAudioDiag.cs")
        kernel = read("Kernel.cs")
        self.assertIn("new CmdAudioDiag()", shell)
        self.assertIn('public string Name => "audiodiag";', cmd)
        self.assertIn("AudioPciDiagnostics.PrintReport(true)", cmd)
        self.assertIn('if (command == "audiodiag")', kernel)
        self.assertIn("AudioPciDiagnostics.PrintReport(false)", kernel)

    def test_builder_and_qemu_keep_guest_diskless(self):
        builder = read("tools/build-cosmos-3095-local.sh")
        launcher = read("run.sh")
        self.assertIn("disk|probe|audio)", builder)
        self.assertIn("audio_probe=true", builder)
        self.assertIn("read_only=true", builder)
        self.assertIn('ZonderqInputDiagnosticBoot="$diagnostic"', builder)
        self.assertIn('ZonderqAudioProbe="$audio_probe"', builder)
        self.assertIn('output_file="ZonderqOS-3095-audio.iso"', builder)
        self.assertIn("--build-audio", launcher)
        self.assertIn("--qemu-audio", launcher)
        self.assertIn("-nic none", launcher)
        self.assertIn("-device intel-hda -device hda-duplex,audiodev=audio0", launcher)
        self.assertIn("verify-audio-wav-capture.py", launcher)
        audio_section = launcher.split("  --qemu-audio)", 1)[1].split("  --qemu)", 1)[0]
        self.assertNotIn("-drive ", audio_section)
        self.assertNotIn("ZONDERQDATA", audio_section)

    def test_real_pcm_path_is_opt_in_only(self):
        runtime = read("source/Core/AudioRuntimeDiagnostics.cs")
        kernel = read("Kernel.cs")
        self.assertTrue(runtime.startswith("#if ZONDERQ_AUDIO_PROBE"))
        self.assertIn("AudioManager.Primary", runtime)
        self.assertIn("AudioTonePcm.GenerateSquareStereo16(sampleRate, 440, 250, 4096)", runtime)
        tone = read("source/Core/AudioTonePcm.cs")
        self.assertIn("(long)i * frequency * 2", tone)
        self.assertNotIn("Disk.", tone)
        self.assertIn("AudioManager.Play(new MemoryAudioStream(AudioFormat.Stereo16, sampleRate, scaled))", runtime)
        self.assertIn("PCM ACCEPTED_AND_DRAINED (not proof of audible output)", runtime)
        self.assertIn('if (command == "tone")', kernel)
        self.assertIn('if (command == "audioinfo")', kernel)
        for forbidden in ("Disk.", "File.", "WriteBlock(", "TryMount(", "Format(", "NetworkManager."):
            self.assertNotIn(forbidden, runtime)


    def test_qemu_stereo_and_mute_gates_are_not_serial_only(self):
        workflow = read(".github/workflows/audio-qemu.yml")
        harness = read("tools/smoke-audio-qemu.py")
        validator = read("tools/verify-audio-wav-capture.py")
        mute = read("tools/verify-audio-mute-capture.py")
        self.assertIn("audio-qemu-muted-out", workflow)
        self.assertIn("--volume 0", workflow)
        self.assertIn("tests/test_audio_mute_validator.py", workflow)
        self.assertIn("mute_capture.inspect(wav)", harness)
        self.assertIn("if args.volume == 0:", harness)
        self.assertIn("stereo channel imbalance", validator)
        self.assertIn("stereo right channel has no sustained", validator)
        self.assertIn("stereo channels are not phase-aligned", validator)
        self.assertIn("frames < rate // 10", mute)
        self.assertIn("peak > 32", mute)
        self.assertIn("stream.readframes(frames)", mute)
        self.assertIn("captured.wav", harness)
        for forbidden in ("-hda ", "-drive file=/dev/", "ZONDERQDATA"):
            self.assertNotIn(forbidden, harness)

    def test_diskless_wav_decode_is_opt_in_and_bounded(self):
        parser = read("source/Core/AudioWavePcm.cs")
        runtime = read("source/Core/AudioRuntimeDiagnostics.cs")
        kernel = read("Kernel.cs")
        project = read("tests/AudioPciSmoke/AudioPciSmoke.csproj")
        workflow = read(".github/workflows/audio-qemu.yml")
        harness = read("tools/smoke-audio-qemu.py")
        self.assertIn("MaxDurationSeconds = 2", parser)
        self.assertIn("sourceChannels != 1 && sourceChannels != 2", parser)
        self.assertIn("dataSize > sourceRate * MaxDurationSeconds * sourceFrameBytes", parser)
        self.assertIn("byte[] upmixed = new byte[dataSize * 2]", parser)
        self.assertIn("public static byte[] EncodeMono16(byte[] pcm)", parser)
        self.assertIn("AudioWavePcm.EncodeMono16(mono)", runtime)
        self.assertIn("GenerateSquareStereo16(48000, 660, 250, 4096)", runtime)
        self.assertIn('expected_hz=660 if args.command == "wavmono" else 880 if args.command == "wav44" else 440', harness)
        self.assertIn('if (command == "wav44")', kernel)
        self.assertIn("AudioWavePcm.EncodeStereo16AtRate(source, 44100)", runtime)
        self.assertIn("--command wav44", workflow)
        self.assertIn("AudioWavePcm.DecodeStereo16(wave)", runtime)
        self.assertIn('if (command == "wavmono")', kernel)
        self.assertIn("--command wavmono", workflow)
        self.assertIn("riffEnd != wave.Length", parser)
        # Mono uses 2-byte frames; stereo uses 4-byte frames. Validate after fmt parsing.
        self.assertIn("int sourceFrameBytes = channels * (sourceBits / 8);", parser)
        self.assertIn("dataSize % sourceFrameBytes != 0", parser)
        self.assertIn("pcm.Length % FrameBytes != 0", parser)
        self.assertIn("rate != SampleRate && rate != 44100", parser)
        self.assertIn("ResampleToStereo16(wave, dataOffset, dataSize, channels, sourceRate, sourceBits)", parser)
        self.assertIn("targetFrames > SampleRate * MaxDurationSeconds", parser)
        self.assertIn("EncodeStereo16AtRate(byte[] pcm, int sampleRate)", parser)
        self.assertIn("EncodePcm8AtRate(byte[] pcm, int sampleRate, int channels)", parser)
        self.assertIn("(wave[offset] - 128) << 8", parser)
        self.assertIn("PlayWave8TestTone()", runtime)
        self.assertIn('if (command == "wav8")', kernel)
        self.assertIn("--command wav8", workflow)
        self.assertIn("1320 if args.command == \"wav8\"", harness)
        reader = read("source/Core/AudioWaveInput.cs")
        self.assertIn("MaxFileBytes = 512 * 1024", reader)
        self.assertIn("ReadExact(input, wave, header.Length", reader)
        self.assertIn("AudioWavePcm.DecodeStereo16(wave)", reader)
        self.assertIn("AudioWaveInput.DecodeBounded(input)", runtime)
        self.assertIn("new MemoryStream(wave, false)", runtime)
        for forbidden in ("File.Open", "File.Read", "File.Write", "Disk.", "TryMount(", "WriteBlock("):
            self.assertNotIn(forbidden, reader)
        self.assertIn("AudioWavePcm.DecodeStereo16(wave)", runtime)
        self.assertIn('if (command == "wavtone")', kernel)
        self.assertIn("PlayWaveTestTone()", kernel)
        self.assertIn("AudioWavePcm.cs", project)
        self.assertIn("AudioPcmVolume.cs", project)
        self.assertIn("AudioPcmVolume.ScaleStereo16(pcm, volumePercent)", runtime)
        self.assertIn('if (command == "volume")', kernel)
        self.assertIn('command.StartsWith("volume ", StringComparison.Ordinal)', kernel)
        self.assertIn("--volume", harness)
        self.assertIn("verify-audio-volume-capture.py", workflow)
        gain = read("source/Core/AudioPcmVolume.cs")
        self.assertIn("percent < 0 || percent > 100", gain)
        for forbidden in ("Disk.", "File.", "TryMount(", "WriteBlock("):
            self.assertNotIn(forbidden, gain)

        self.assertIn("--command wavtone", workflow)
        self.assertIn('choices=("tone", "wavtone", "wavmono", "wav44", "wav8")', harness)
        for forbidden in ("System.IO", "File.", "Disk.", "TryMount(", "WriteBlock(",
                          "NetworkManager.", "PciSysfsProvider"):
            self.assertNotIn(forbidden, parser)
        for forbidden in ("Disk.", "File.", "TryMount(", "WriteBlock("):
            self.assertNotIn(forbidden, runtime)


if __name__ == "__main__":
    unittest.main(verbosity=2)
