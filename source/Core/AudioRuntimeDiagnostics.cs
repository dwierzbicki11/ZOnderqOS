#if ZONDERQ_AUDIO_PROBE
using System;
using System.IO;
using Cosmos.Kernel.HAL.Devices.Audio;
using Cosmos.Kernel.System.Audio;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Opt-in, diskless HDA playback probe. Only built with
    /// ZonderqAudioProbe=true and ZonderqReadOnlyHardwareProbe=true.
    /// Uses the unmodified Cosmos 3.0.95 audio driver and AudioManager.
    /// </summary>
    public static class AudioRuntimeDiagnostics
    {
        private static int volumePercent = 100;
        private static int balancePercent;
        private static int fadeMilliseconds;

        public static int FadeMilliseconds => fadeMilliseconds;

        public static bool SetFadeMilliseconds(int milliseconds)
        {
            if (milliseconds < 0 || milliseconds > 50)
            {
                Console.WriteLine("[AUDIO] FADE REFUSED: expected 0..50 ms.");
                return false;
            }
            fadeMilliseconds = milliseconds;
            Console.WriteLine("[AUDIO] software fade=" + fadeMilliseconds + "ms (PCM envelope only)");
            return true;
        }

        public static int VolumePercent => volumePercent;
        // -100 = left only, +100 = right only; zero = equal channels.
        public static int BalancePercent => balancePercent;

        public static bool SetBalancePercent(int percent)
        {
            if (percent < -100 || percent > 100)
            {
                Console.WriteLine("[AUDIO] BALANCE REFUSED: expected -100..100.");
                return false;
            }
            balancePercent = percent;
            Console.WriteLine("[AUDIO] software balance=" + balancePercent +
                " (-100=left only, +100=right only)");
            return true;
        }

        public static bool SetVolumePercent(int percent)
        {
            if (percent < 0 || percent > 100)
            {
                Console.WriteLine("[AUDIO] VOLUME REFUSED: expected 0..100.");
                return false;
            }
            volumePercent = percent;
            Console.WriteLine("[AUDIO] software volume=" + volumePercent + "% (PCM gain only)");
            return true;
        }

        public static bool PrintStatus()
        {
            if (!AudioManager.IsEnabled)
            {
                Console.WriteLine("[AUDIO] feature=DISABLED");
                return false;
            }

            Console.WriteLine("[AUDIO] feature=ENABLED outputs=" + AudioManager.Count);
            AudioDevice output = AudioManager.Primary;
            if (output == null || output.IsWithdrawn)
            {
                Console.WriteLine("[AUDIO] output=NONE; no driver binding confirmed.");
                return false;
            }

            Console.WriteLine("[AUDIO] output=" + output.Name + " driver=" + output.DriverName);
            Console.WriteLine("[AUDIO] node=" + (output.NodePath ?? "UNKNOWN"));
            Console.WriteLine("[AUDIO] format=" + output.SampleRate + "Hz " +
                output.Format.Channels + "ch " + (output.Format.ChannelSize * 8) + "bit");
            Console.WriteLine("[AUDIO] running=" + output.IsRunning + " writable=" + output.WritableBytes);
            Console.WriteLine("[AUDIO] A published output does not prove audible PCM.");
            return true;
        }

        /// <summary>
        /// Distinct left/right PCM16 signals (440/880 Hz) for independent
        /// stereo HDA capture verification. Diskless and opt-in only.
        /// </summary>
        public static bool PlayStereoSplitTestTone()
        {
            byte[] pcm = AudioTonePcm.GenerateDualSquareStereo16(48000, 440, 880, 250, 4096);
            return PlayPcm(pcm);
        }

        public static bool PlayTestTone()
        {
            const int sampleRate = 48000;
            byte[] pcm = AudioTonePcm.GenerateSquareStereo16(sampleRate, 440, 250, 4096);
            return PlayPcm(pcm);
        }

        /// <summary>
        /// End-to-end RIFF/WAVE decoder + stock HDA playback smoke. The WAV
        /// fixture is generated entirely in RAM: no FAT32 or host disk reads.
        /// A successful decode alone is not evidence of audible output.
        /// </summary>
        public static bool PlayWaveTestTone()
        {
            try
            {
                byte[] source = AudioTonePcm.GenerateSquareStereo16(48000, 440, 250, 4096);
                byte[] wave = AudioWavePcm.EncodeStereo16(source);
                byte[] decoded = AudioWavePcm.DecodeStereo16(wave);
                Console.WriteLine("[AUDIO] WAV decoded 48000Hz PCM16 stereo bytes=" + decoded.Length);
                return PlayPcm(decoded);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AUDIO] WAV ERROR: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Independent in-RAM mono RIFF fixture -> upmix to stereo -> stock HDA.
        /// No guest filesystem or physical codec access is added.
        /// </summary>
        public static bool PlayWaveMonoTestTone()
        {
            try
            {
                // Distinct from tone/wavtone (440 Hz): QEMU can verify this path ran.
                byte[] source = AudioTonePcm.GenerateSquareStereo16(48000, 660, 250, 4096);
                byte[] mono = new byte[source.Length / 2];
                for (int i = 0; i < mono.Length; i += 2)
                {
                    mono[i] = source[i * 2];
                    mono[i + 1] = source[i * 2 + 1];
                }
                byte[] wave = AudioWavePcm.EncodeMono16(mono);
                byte[] decoded = AudioWavePcm.DecodeStereo16(wave);
                Console.WriteLine("[AUDIO] WAV mono upmixed 48000Hz PCM16 stereo bytes=" + decoded.Length);
                return PlayPcm(decoded);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AUDIO] WAV MONO ERROR: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Independent 44.1 kHz RIFF/WAV fixture -> bounded 48 kHz resampling
        /// -> stock HDA. Distinct 880 Hz pitch makes QEMU capture attributable
        /// to this decoder path; the diagnostic image never reads guest disks.
        /// </summary>
        public static bool PlayWave44100TestTone()
        {
            try
            {
                byte[] source = AudioTonePcm.GenerateSquareStereo16(44100, 880, 250, 4096);
                byte[] wave = AudioWavePcm.EncodeStereo16AtRate(source, 44100);
                Console.WriteLine("[AUDIO] WAV 44100Hz input -> bounded read-only stream -> 48000Hz HDA");
                using (var input = new MemoryStream(wave, false))
                    return PlayWaveFromReadOnlyStream(input);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AUDIO] WAV 44100 ERROR: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Independent 440/880 Hz stereo channels in a 32 kHz PCM16 WAV.
        /// Exercises the bounded read-only RIFF loader, integer resampler,
        /// and stock HDA together. No guest disk or production GUI access.
        /// </summary>
        public static bool PlayWave32000StereoSplitTestTone()
        {
            try
            {
                byte[] source = AudioTonePcm.GenerateDualSquareStereo16(32000, 440, 880, 250, 4096);
                byte[] wave = AudioWavePcm.EncodeStereo16AtRate(source, 32000);
                Console.WriteLine("[AUDIO] WAV 32000Hz independent stereo 440/880Hz -> 48000Hz HDA");
                using (var input = new MemoryStream(wave, false))
                    return PlayWaveFromReadOnlyStream(input);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AUDIO] WAV 32000 STEREO ERROR: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Distinct 3520 Hz PCM16 stereo WAV at 32 kHz, resampled by the
        /// bounded read-only loader to 48 kHz and played via stock HDA.
        /// No filesystem, host disk or production GUI is accessed.
        /// </summary>
        public static bool PlayWave32000TestTone()
        {
            try
            {
                byte[] source = AudioTonePcm.GenerateSquareStereo16(32000, 3520, 250, 4096);
                byte[] wave = AudioWavePcm.EncodeStereo16AtRate(source, 32000);
                Console.WriteLine("[AUDIO] WAV 32000Hz input -> bounded read-only stream -> 48000Hz HDA");
                using (var input = new MemoryStream(wave, false))
                    return PlayWaveFromReadOnlyStream(input);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AUDIO] WAV 32000 ERROR: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 8 kHz unsigned PCM8 mono voice WAV -> 48 kHz stereo PCM16.
        /// The 400 Hz test fixture is RAM-only; QEMU captures actual HDA output.
        /// No guest filesystem, codec register or host disk access.
        /// </summary>
        public static bool PlayWave8000MonoTestTone()
        {
            try
            {
                byte[] source = AudioTonePcm.GenerateSquareStereo16(8000, 400, 250, 4096);
                byte[] mono8 = new byte[source.Length / 4];
                for (int i = 0; i < mono8.Length; i++)
                {
                    int at = i * 4;
                    short sample = (short)(source[at] | (source[at + 1] << 8));
                    mono8[i] = (byte)(128 + (sample >> 8));
                }
                byte[] wave = AudioWavePcm.EncodePcm8AtRate(mono8, 8000, 1);
                Console.WriteLine("[AUDIO] WAV 8000Hz PCM8 mono -> bounded stream -> 48000Hz HDA");
                using (var input = new MemoryStream(wave, false))
                    return PlayWaveFromReadOnlyStream(input);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AUDIO] WAV 8000 ERROR: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 16 kHz unsigned PCM8 mono voice-rate WAV -> 48 kHz stereo PCM16.
        /// Distinct 1500 Hz fixture is generated in RAM for QEMU HDA capture.
        /// No guest filesystem, codec register or host disk access.
        /// </summary>
        public static bool PlayWave16000MonoTestTone()
        {
            try
            {
                byte[] source = AudioTonePcm.GenerateSquareStereo16(16000, 1500, 250, 4096);
                byte[] mono8 = new byte[source.Length / 4];
                for (int i = 0; i < mono8.Length; i++)
                {
                    int at = i * 4;
                    short sample = (short)(source[at] | (source[at + 1] << 8));
                    mono8[i] = (byte)(128 + (sample >> 8));
                }
                byte[] wave = AudioWavePcm.EncodePcm8AtRate(mono8, 16000, 1);
                Console.WriteLine("[AUDIO] WAV 16000Hz PCM8 mono -> bounded stream -> 48000Hz HDA");
                using (var input = new MemoryStream(wave, false))
                    return PlayWaveFromReadOnlyStream(input);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AUDIO] WAV 16000 ERROR: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 24 kHz stereo PCM16 WAV -> 48 kHz stereo PCM16, with distinct
        /// 740 Hz fixture for QEMU backend verification. RAM-only.
        /// </summary>
        public static bool PlayWave24000TestTone()
        {
            try
            {
                byte[] source = AudioTonePcm.GenerateSquareStereo16(24000, 740, 250, 4096);
                byte[] wave = AudioWavePcm.EncodeStereo16AtRate(source, 24000);
                Console.WriteLine("[AUDIO] WAV 24000Hz stereo16 -> bounded stream -> 48000Hz HDA");
                using (var input = new MemoryStream(wave, false))
                    return PlayWaveFromReadOnlyStream(input);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AUDIO] WAV 24000 ERROR: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Independent 22.05 kHz PCM16 stereo WAV with a distinct 1100 Hz
        /// test tone. Resamples to 48 kHz through the same bounded read-only
        /// stream used by other probes; no guest disk or GUI is involved.
        /// </summary>
        public static bool PlayWave22050TestTone()
        {
            try
            {
                byte[] source = AudioTonePcm.GenerateSquareStereo16(22050, 1100, 250, 4096);
                byte[] wave = AudioWavePcm.EncodeStereo16AtRate(source, 22050);
                Console.WriteLine("[AUDIO] WAV 22050Hz input -> bounded read-only stream -> 48000Hz HDA");
                using (var input = new MemoryStream(wave, false))
                    return PlayWaveFromReadOnlyStream(input);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AUDIO] WAV 22050 ERROR: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Independent unsigned PCM8 mono WAV -> signed PCM16 stereo -> stock
        /// HDA. 1320 Hz distinguishes this QEMU capture from the other probes.
        /// No filesystem is opened or mounted.
        /// </summary>
        public static bool PlayWave8TestTone()
        {
            try
            {
                byte[] source = AudioTonePcm.GenerateSquareStereo16(48000, 1320, 250, 4096);
                byte[] mono8 = new byte[source.Length / 4];
                for (int i = 0; i < mono8.Length; i++)
                {
                    int at = i * 4;
                    short sample = (short)(source[at] | (source[at + 1] << 8));
                    mono8[i] = (byte)(128 + (sample >> 8));
                }
                byte[] wave = AudioWavePcm.EncodePcm8AtRate(mono8, 48000, 1);
                Console.WriteLine("[AUDIO] WAV unsigned PCM8 mono -> 48000Hz signed PCM16 stereo");
                using (var input = new MemoryStream(wave, false))
                    return PlayWaveFromReadOnlyStream(input);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AUDIO] WAV PCM8 ERROR: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Independent signed PCM24 mono WAV -> PCM16 stereo -> stock HDA.
        /// 1760 Hz makes the QEMU capture distinguishable from PCM8/PCM16.
        /// All source bytes are generated in RAM; no disk is attached.
        /// </summary>
        public static bool PlayWave24TestTone()
        {
            try
            {
                byte[] source = AudioTonePcm.GenerateSquareStereo16(48000, 1760, 250, 4096);
                byte[] mono24 = new byte[source.Length / 4 * 3];
                for (int i = 0; i < mono24.Length / 3; i++)
                {
                    int at = i * 4;
                    mono24[i * 3] = 0; // least-significant eight bits
                    mono24[i * 3 + 1] = source[at];
                    mono24[i * 3 + 2] = source[at + 1];
                }
                byte[] wave = AudioWavePcm.EncodePcm24AtRate(mono24, 48000, 1);
                Console.WriteLine("[AUDIO] WAV signed PCM24 mono -> 48000Hz signed PCM16 stereo");
                using (var input = new MemoryStream(wave, false))
                    return PlayWaveFromReadOnlyStream(input);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AUDIO] WAV PCM24 ERROR: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Independent signed PCM32 mono WAV -> PCM16 stereo -> stock HDA.
        /// The 2200 Hz fixture is generated entirely in RAM; no disk access.
        /// </summary>
        public static bool PlayWave32TestTone()
        {
            try
            {
                byte[] source = AudioTonePcm.GenerateSquareStereo16(48000, 2200, 250, 4096);
                byte[] mono32 = new byte[source.Length];
                for (int i = 0; i < mono32.Length / 4; i++)
                {
                    int at = i * 4;
                    mono32[at] = 0;
                    mono32[at + 1] = 0;
                    mono32[at + 2] = source[at];
                    mono32[at + 3] = source[at + 1];
                }
                byte[] wave = AudioWavePcm.EncodePcm32AtRate(mono32, 48000, 1);
                Console.WriteLine("[AUDIO] WAV signed PCM32 mono -> 48000Hz signed PCM16 stereo");
                using (var input = new MemoryStream(wave, false))
                    return PlayWaveFromReadOnlyStream(input);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AUDIO] WAV PCM32 ERROR: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// WAVE_FORMAT_EXTENSIBLE PCM16 stereo through the same bounded
        /// read-only stream and stock HDA path. Unique 2640 Hz tone allows
        /// an independent QEMU backend capture; no guest disk is attached.
        /// </summary>
        public static bool PlayWaveExtensibleTestTone()
        {
            try
            {
                byte[] source = AudioTonePcm.GenerateSquareStereo16(48000, 2640, 250, 4096);
                byte[] wave = AudioWavePcm.EncodeExtensibleStereo16AtRate(source, 48000);
                Console.WriteLine("[AUDIO] WAV extensible PCM16 stereo -> 48000Hz HDA");
                using (var input = new MemoryStream(wave, false))
                    return PlayWaveFromReadOnlyStream(input);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AUDIO] WAV EXTENSIBLE ERROR: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Distinct 3080 Hz, 20-valid-bit mono in a signed 24-bit extensible
        /// WAV container. RAM-only, through the bounded stream and stock HDA.
        /// </summary>
        public static bool PlayWaveExtensible20TestTone()
        {
            try
            {
                byte[] source = AudioTonePcm.GenerateSquareStereo16(48000, 3080, 250, 4096);
                byte[] mono24 = new byte[source.Length / 4 * 3];
                for (int i = 0; i < mono24.Length / 3; i++)
                {
                    int at = i * 4;
                    mono24[i * 3] = 0; // unused low four bits are zero
                    mono24[i * 3 + 1] = source[at];
                    mono24[i * 3 + 2] = source[at + 1];
                }
                byte[] wave = AudioWavePcm.EncodeExtensiblePcm24AtRate(mono24, 48000, 1, 20);
                Console.WriteLine("[AUDIO] WAV extensible 20-in-24 mono -> 48000Hz PCM16 stereo HDA");
                using (var input = new MemoryStream(wave, false))
                    return PlayWaveFromReadOnlyStream(input);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AUDIO] WAV EXTENSIBLE20 ERROR: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Caller owns the stream and is responsible for obtaining it from an
        /// explicitly authorized read-only source. No filesystem is opened here.
        /// The bounded loader rejects oversized, truncated and malformed WAVs.
        /// </summary>
        public static bool PlayWaveFromReadOnlyStream(Stream input)
        {
            try
            {
                byte[] decoded = AudioWaveInput.DecodeBounded(input);
                Console.WriteLine("[AUDIO] bounded WAV stream decoded to 48000Hz PCM16 stereo bytes=" + decoded.Length);
                return PlayPcm(decoded);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AUDIO] WAV STREAM ERROR: " + ex.Message);
                return false;
            }
        }

        private static bool PlayPcm(byte[] pcm)
        {
            AudioDevice output = AudioManager.Primary;
            if (!AudioManager.IsEnabled || output == null || output.IsWithdrawn)
            {
                Console.WriteLine("[AUDIO] TONE REFUSED: no published output.");
                return false;
            }

            if (output.Player != null || output.IsRunning)
            {
                Console.WriteLine("[AUDIO] TONE REFUSED: output already busy.");
                return false;
            }

            const int sampleRate = 48000;
            try
            {
                // Apply channel balance without changing codec or DMA configuration.
                int leftGain = volumePercent * (balancePercent > 0 ? 100 - balancePercent : 100) / 100;
                int rightGain = volumePercent * (balancePercent < 0 ? 100 + balancePercent : 100) / 100;
                // Fade-enabled playback uses one PCM output allocation
                // instead of creating an intermediate scaled copy.
                // The stock HDA output uses a 16 KiB cyclic DMA ring. A
                // short fade can lose its final frames when the player stops
                // while the ring still contains older full-level samples.
                // Keep one ring of zero PCM *inside* the bounded source stream
                // for opt-in fade probes, without changing the stock driver.
                const int hdaDrainSilenceBytes = 16 * 1024;
                byte[] scaled = fadeMilliseconds == 0
                    ? AudioPcmVolume.ScaleStereo16Channels(pcm, leftGain, rightGain)
                    : AudioPcmVolume.ScaleStereo16ChannelsWithFadeAndSilence(
                        pcm, leftGain, rightGain, fadeMilliseconds, hdaDrainSilenceBytes);
                bool accepted = AudioManager.Play(new MemoryAudioStream(AudioFormat.Stereo16, sampleRate, scaled));
                Console.WriteLine(accepted
                    ? "[AUDIO] PCM ACCEPTED_AND_DRAINED (not proof of audible output)"
                    : "[AUDIO] PCM REFUSED (format, driver or playback failure)");
                return accepted;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AUDIO] PCM ERROR: " + ex.Message);
                return false;
            }
        }
    }
}
#endif
