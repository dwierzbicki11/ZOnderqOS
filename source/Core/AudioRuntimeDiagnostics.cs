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
        /// <summary>
        /// Exercise two independent simultaneous PCM sources through the
        /// bounded software mixer and the unmodified Cosmos HDA output.
        /// The left-only 440 Hz and right-only 880 Hz inputs make both
        /// contributions independently verifiable in QEMU's WAV capture.
        /// No filesystem, new DMA path or production GUI activation.
        /// </summary>
        public static bool PlayMixedTestTone()
        {
            byte[] left = AudioTonePcm.GenerateSquareStereo16(48000, 440, 250, 4096);
            byte[] right = AudioTonePcm.GenerateSquareStereo16(48000, 880, 250, 4096);
            for (int at = 0; at < left.Length; at += 4)
            {
                left[at + 2] = 0;
                left[at + 3] = 0;
                right[at] = 0;
                right[at + 1] = 0;
            }
            byte[] mixed = AudioPcmMixer.MixStereo16(left, right, 100, 100);
            Console.WriteLine("[AUDIO] MIX two independent 48kHz stereo16 PCM sources left=440Hz right=880Hz bytes=" + mixed.Length);
            return PlayPcm(mixed);
        }

        /// <summary>
        /// Two independent PCM sources with a frame-accurate 50 ms offset.
        /// Left 440 Hz begins immediately; right 880 Hz begins 2400 frames
        /// later. QEMU capture must verify the relative channel onset.
        /// No guest filesystem, DMA configuration or GUI audio activation.
        /// </summary>
        public static bool PlayDelayedMixedTestTone()
        {
            byte[] left = AudioTonePcm.GenerateSquareStereo16(48000, 440, 250, 4096);
            byte[] right = AudioTonePcm.GenerateSquareStereo16(48000, 880, 250, 4096);
            for (int at = 0; at < left.Length; at += 4)
            {
                left[at + 2] = 0;
                left[at + 3] = 0;
                right[at] = 0;
                right[at + 1] = 0;
            }
            const int delayFrames = 2400; // 50 ms at 48 kHz
            byte[] mixed = AudioPcmMixer.MixStereo16WithDelay(
                left, right, 100, 100, delayFrames);
            Console.WriteLine("[AUDIO] MIXDELAY 48kHz stereo16 left=440Hz right=880Hz rightDelayFrames=" +
                delayFrames + " bytes=" + mixed.Length);
            return PlayPcm(mixed);
        }

        /// <summary>
        /// Three scheduled, non-identical PCM effects for an end-to-end HDA
        /// capture: left 440 Hz (0..250 ms), right 880 Hz (50..300 ms),
        /// then left 660 Hz (300..400 ms). The 50 ms left-channel gap and
        /// different final pitch prove that the third clip was scheduled.
        /// This is an opt-in diskless test, not production GUI sound.
        /// </summary>
        public static bool PlayScheduledMixedTestTones()
        {
            // Real QEMU HDA capture on 3.0.95 verified the stock scheduler-
            // thread AudioPlayback path while boot-thread ring feeding replayed
            // stale DMA data. Keep the direct 1ms path opt-in for diagnosis.
            // STARTED is not proof of audible output: CI checks captured WAV.
            return PlayScheduledMixedTestTones(false, true);
        }

        /// <summary>
        /// Use stock AudioPlayback's scheduler thread and atomic output claim,
        /// rather than the boot/idle thread. This A/B diagnostic still uses
        /// the unmodified 5ms AudioPlayer ring-full wait.
        /// </summary>
        // Preserve the measured boot-thread A/B baseline without making it
        // the default scheduled playback path. Diagnostic ISO only.
        public static bool PlayScheduledMixedTestTonesBootThread()
        {
            return PlayScheduledMixedTestTones(false, false);
        }

        public static bool PlayScheduledMixedTestTonesAsync()
        {
            return PlayScheduledMixedTestTones(false, true);
        }

        public static bool PlayScheduledMixedTestTones(bool useOneMillisecondDiagnostic, bool useAsyncDiagnostic = false)
        {
            byte[] left = AudioTonePcm.GenerateSquareStereo16(48000, 440, 250, 4096);
            byte[] right = AudioTonePcm.GenerateSquareStereo16(48000, 880, 250, 4096);
            byte[] finalLeft = AudioTonePcm.GenerateSquareStereo16(48000, 660, 100, 4096);
            for (int at = 0; at < left.Length; at += 4)
            {
                left[at + 2] = 0;
                left[at + 3] = 0;
                right[at] = 0;
                right[at + 1] = 0;
            }
            for (int at = 0; at < finalLeft.Length; at += 4)
            {
                finalLeft[at + 2] = 0;
                finalLeft[at + 3] = 0;
            }

            // QEMU's TCG HDA backend can underrun during initial stream
            // startup. Prime the unchanged stock AudioManager with 250 ms
            // of leading zero PCM before the three scheduled effects. This
            // does not change their relative frame offsets (0/50/300 ms),
            // the 50 ms inter-effect gap, or the production GUI audio path.
            // Keep the lead-in inside the existing two-second PCM budget.
            const int preRollFrames = 12000; // 250 ms at 48 kHz
            // Five-millisecond per-effect attack/release reduces hard PCM
            // edges while keeping the 50 ms onset offset and inter-clip gap.
            // A 16 KiB silent tail is required to observe the final 20..45ms
            // of silence in QEMU: a previous real backend WAV ended during
            // the last effect. The mixer allocates only one output array and
            // does not alter the stock HDA driver.
            const int clipFadeMilliseconds = 5;
            byte[] mixed = AudioPcmMixer.MixScheduledStereo16WithIndividualFades(
                new[] { left, right, finalLeft },
                new[] { preRollFrames, preRollFrames + 2400, preRollFrames + 14400 },
                new[] { 100, 100, 100 },
                new[] { clipFadeMilliseconds, clipFadeMilliseconds, clipFadeMilliseconds }, 4096);
            Console.WriteLine("[AUDIO] MIXQUEUE 48kHz stereo16 clips=3 preroll=250ms clipFade=5ms tail=4096frames " +
                "left=440Hz@0ms right=880Hz@50ms left=660Hz@300ms bytes=" + mixed.Length);
            return PlayPcm(mixed, useOneMillisecondDiagnostic, useAsyncDiagnostic);
        }

        /// <summary>
        /// Four distinct 90 ms PCM tones (400/700/1000/1300 Hz) with a
        /// 250 ms silent lead-in and 16 KiB drain. Unlike a single periodic
        /// tone, this changes frequency across successive HDA ring periods:
        /// a stale 16 KiB DMA replay cannot masquerade as all four tones.
        /// Diagnostic only; no hardware register, disk or GUI access.
        /// </summary>
        public static bool PlayFrequencySequenceTestTones()
        {
            // Match the measured scheduler-thread path used by mixqueue.
            // The 1ms boot-thread experiment remains separately selectable.
            return PlayFrequencySequenceTestTones(false, true);
        }

        public static bool PlayFrequencySequenceTestTonesBootThread()
        {
            return PlayFrequencySequenceTestTones(false, false);
        }

        public static bool PlayFrequencySequenceTestTonesAsync()
        {
            return PlayFrequencySequenceTestTones(false, true);
        }

        public static bool PlayFrequencySequenceTestTones(bool useOneMillisecondDiagnostic, bool useAsyncDiagnostic = false)
        {
            int[] frequencies = { 400, 700, 1000, 1300 };
            byte[][] clips = new byte[frequencies.Length][];
            int[] starts = new int[frequencies.Length];
            int[] gains = new int[frequencies.Length];
            int[] fades = new int[frequencies.Length];
            const int preRollFrames = 12000; // 250 ms at 48 kHz
            const int clipFrames = 4320; // 90 ms at 48 kHz
            for (int i = 0; i < frequencies.Length; i++)
            {
                clips[i] = AudioTonePcm.GenerateSquareStereo16(48000, frequencies[i], 90, 4096);
                starts[i] = preRollFrames + i * clipFrames;
                gains[i] = 100;
                fades[i] = 5;
            }
            byte[] pcm = AudioPcmMixer.MixScheduledStereo16WithIndividualFades(
                clips, starts, gains, fades, 4096);
            Console.WriteLine("[AUDIO] MIXSEQUENCE 48kHz stereo16 four 90ms tones=400/700/1000/1300Hz " +
                "preroll=250ms fade=5ms tail=4096frames bytes=" + pcm.Length);
            return PlayPcm(pcm, useOneMillisecondDiagnostic, useAsyncDiagnostic);
        }

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
        /// 11.025 kHz unsigned PCM8 mono voice WAV -> 48 kHz stereo PCM16.
        /// A distinct 550 Hz signal makes the QEMU capture attributable to
        /// this resampling path. Source is generated in RAM; no disk access.
        /// </summary>
        public static bool PlayWave11025MonoTestTone()
        {
            try
            {
                byte[] source = AudioTonePcm.GenerateSquareStereo16(11025, 550, 250, 4096);
                byte[] mono8 = new byte[source.Length / 4];
                for (int i = 0; i < mono8.Length; i++)
                {
                    int at = i * 4;
                    short sample = (short)(source[at] | (source[at + 1] << 8));
                    mono8[i] = (byte)(128 + (sample >> 8));
                }
                byte[] wave = AudioWavePcm.EncodePcm8AtRate(mono8, 11025, 1);
                Console.WriteLine("[AUDIO] WAV 11025Hz PCM8 mono -> bounded stream -> 48000Hz HDA");
                using (var input = new MemoryStream(wave, false))
                    return PlayWaveFromReadOnlyStream(input);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AUDIO] WAV 11025 ERROR: " + ex.Message);
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
        /// Canonical IEEE_FLOAT mono WAV (2860 Hz, +/-0.125) -> bounded
        /// finite PCM16 -> stock HDA. The raw little-endian float fixture is
        /// generated in RAM, not loaded from a guest or host disk. QEMU must
        /// validate the backend WAV capture, not merely a decoder log.
        /// </summary>
        public static bool PlayWaveFloat32TestTone()
        {
            try
            {
                byte[] source = AudioTonePcm.GenerateSquareStereo16(48000, 2860, 250, 4096);
                byte[] monoFloat = AudioFloatToneFixture.FromStereo16Left(source);
                byte[] wave = AudioWavePcm.EncodeFloat32AtRate(monoFloat, 48000, 1);
                Console.WriteLine("[AUDIO] WAV IEEE_FLOAT32 mono 2860Hz -> bounded PCM16 stereo HDA");
                using (var input = new MemoryStream(wave, false))
                    return PlayWaveFromReadOnlyStream(input);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AUDIO] WAV FLOAT32 ERROR: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// WAVE_FORMAT_EXTENSIBLE IEEE_FLOAT32 mono at 44.1 kHz -> bounded
        /// integer resampler -> 48 kHz stereo16 -> stock HDA. A distinct
        /// 3960 Hz test tone lets QEMU verify actual backend PCM. This is
        /// generated in RAM and never opens a disk or production GUI.
        /// </summary>
        public static bool PlayWaveExtensibleFloat32TestTone()
        {
            try
            {
                byte[] source = AudioTonePcm.GenerateSquareStereo16(44100, 3960, 250, 4096);
                byte[] monoFloat = AudioFloatToneFixture.FromStereo16Left(source);
                byte[] wave = AudioWavePcm.EncodeExtensibleFloat32AtRate(
                    monoFloat, 44100, 1);
                Console.WriteLine("[AUDIO] WAV extensible IEEE_FLOAT32 mono 44100Hz 3960Hz -> 48000Hz HDA");
                using (var input = new MemoryStream(wave, false))
                    return PlayWaveFromReadOnlyStream(input);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AUDIO] WAV EXTENSIBLE FLOAT32 ERROR: " + ex.Message);
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
        /// Distinct 3700 Hz mono WAVE_FORMAT_EXTENSIBLE PCM24 in a signed
        /// 32-bit container (24 valid bits). The low eight padding bits are
        /// zero; the next eight are nonzero valid precision. The upper 16
        /// bits carry the signed tone. RAM-only, bounded read-only stream,
        /// then the existing stock HDA output; no guest disk or codec writes.
        /// </summary>
        public static bool PlayWaveExtensible24In32TestTone()
        {
            try
            {
                byte[] source = AudioTonePcm.GenerateSquareStereo16(48000, 3700, 250, 4096);
                byte[] mono32 = new byte[source.Length];
                for (int i = 0; i < mono32.Length / 4; i++)
                {
                    int at = i * 4;
                    mono32[at] = 0; // eight unused low container bits
                    mono32[at + 1] = 0x5A; // additional valid PCM precision
                    mono32[at + 2] = source[at];
                    mono32[at + 3] = source[at + 1];
                }
                byte[] wave = AudioWavePcm.EncodeExtensiblePcm32AtRate(mono32, 48000, 1, 24);
                Console.WriteLine("[AUDIO] WAV extensible 24-in-32 mono -> 48000Hz PCM16 stereo HDA");
                using (var input = new MemoryStream(wave, false))
                    return PlayWaveFromReadOnlyStream(input);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AUDIO] WAV EXTENSIBLE24IN32 ERROR: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Independent 440 Hz left / 880 Hz right WAVE_FORMAT_EXTENSIBLE
        /// stereo PCM24-in-32 at 44.1 kHz. The low container byte is
        /// padding, the next byte is valid precision and the high 16 bits
        /// retain each channel's signed PCM. The bounded read-only loader
        /// resamples to 48 kHz before the stock HDA output is used.
        /// This is diskless, opt-in and does not configure DMA or IRQ.
        /// </summary>
        public static bool PlayWaveExtensible24In32Stereo44100TestTone()
        {
            try
            {
                byte[] source = AudioTonePcm.GenerateDualSquareStereo16(44100, 440, 880, 250, 4096);
                byte[] stereo32 = new byte[source.Length * 2];
                for (int frame = 0; frame < source.Length / 4; frame++)
                {
                    int src = frame * 4;
                    int dst = frame * 8;
                    stereo32[dst] = 0;       // unused low eight bits, left
                    stereo32[dst + 1] = 0x5A; // valid low precision, left
                    stereo32[dst + 2] = source[src];
                    stereo32[dst + 3] = source[src + 1];
                    stereo32[dst + 4] = 0;       // unused low eight bits, right
                    stereo32[dst + 5] = 0xA5;    // distinct valid precision, right
                    stereo32[dst + 6] = source[src + 2];
                    stereo32[dst + 7] = source[src + 3];
                }
                byte[] wave = AudioWavePcm.EncodeExtensiblePcm32AtRate(stereo32, 44100, 2, 24);
                Console.WriteLine("[AUDIO] WAV extensible stereo 24-in-32 44100Hz 440/880Hz -> 48000Hz HDA");
                using (var input = new MemoryStream(wave, false))
                    return PlayWaveFromReadOnlyStream(input);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AUDIO] WAV EXTENSIBLE STEREO 44100 ERROR: " + ex.Message);
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
            return PlayPcm(pcm, false);
        }

        private static bool PlayPcm(byte[] pcm, bool useOneMillisecondDiagnostic, bool useAsyncDiagnostic = false)
        {
            // Fail closed before touching the primary device or allocating
            // a scaled copy. The probe emits only 48 kHz stereo PCM16; a
            // different physical codec format must never be assumed safe.
            const int sampleRate = 48000;
            const int maxPcmBytes = sampleRate * 2 * 4;
            if (pcm == null || pcm.Length == 0 || pcm.Length > maxPcmBytes ||
                pcm.Length % 4 != 0)
            {
                Console.WriteLine("[AUDIO] PCM REFUSED: invalid stereo16 length.");
                return false;
            }
            if (!AudioManager.IsEnabled)
            {
                Console.WriteLine("[AUDIO] TONE REFUSED: audio subsystem disabled.");
                return false;
            }

            AudioDevice output = AudioManager.Primary;
            if (output == null || output.IsWithdrawn)
            {
                Console.WriteLine("[AUDIO] TONE REFUSED: no published output.");
                return false;
            }
            // The opt-in diagnostic only exercises the stock Cosmos HDA
            // backend already verified in isolated QEMU. A different audio
            // output must not receive probe PCM merely because it advertises
            // a compatible sample format. This does not identify physical
            // speakers or authorize production GUI playback.
            if (output.Name != "hda" || output.DriverName != "HdAudioDriver")
            {
                Console.WriteLine("[AUDIO] PCM REFUSED: isolated probe requires stock HDA output.");
                return false;
            }
            if (output.SampleRate != sampleRate || output.Format.Channels != 2 ||
                output.Format.ChannelSize != 2)
            {
                Console.WriteLine("[AUDIO] PCM REFUSED: output is not 48000Hz stereo16.");
                return false;
            }

            if (output.Player != null || output.IsRunning)
            {
                Console.WriteLine("[AUDIO] TONE REFUSED: output already busy.");
                return false;
            }

            try
            {
                // Apply channel balance without changing codec or DMA configuration.
                AudioPcmVolume.ResolveStereoGains(
                    volumePercent, balancePercent, out int leftGain, out int rightGain);
                // Fade-enabled playback uses one PCM output allocation
                // instead of creating an intermediate scaled copy.
                // The stock HDA output uses a 16 KiB cyclic DMA ring. A
                // short fade can lose its final frames when the player stops
                // while the ring still contains older full-level samples.
                // Keep one ring of zero PCM *inside* the bounded source stream
                // for opt-in fade probes, without changing the stock driver.
                const int hdaDrainSilenceBytes = 16 * 1024;
                // Long, otherwise-valid WAVs must remain playable: reserve
                // only the room left inside the existing two-second cap.
                // AudioPlayer itself still drains one silent ring at EOF.
                int drainBytes = Math.Min(hdaDrainSilenceBytes,
                    Math.Max(0, maxPcmBytes - pcm.Length));
                // All probe sources are freshly generated or decoded and
                // exclusively owned here. At 100/100 gain with no fade, reuse
                // the exact PCM array rather than allocating a second copy.
                // A fade or non-unity gain still gets a separate output buffer.
                byte[] scaled = fadeMilliseconds == 0
                    ? AudioPcmVolume.ScaleStereo16ChannelsOrReuse(pcm, leftGain, rightGain)
                    : AudioPcmVolume.ScaleStereo16ChannelsWithFadeAndSilence(
                        pcm, leftGain, rightGain, fadeMilliseconds, drainBytes);
                if (useOneMillisecondDiagnostic && useAsyncDiagnostic)
                {
                    Console.WriteLine("[AUDIO] REFUSED: conflicting diagnostic playback modes.");
                    return false;
                }
                if (useOneMillisecondDiagnostic)
                    return AudioProbeDirectPlayback.TryPlay(output, scaled, sampleRate);
                if (useAsyncDiagnostic)
                {
                    // Stock Cosmos claims the output atomically before starting
                    // a scheduler thread. This is safer than direct ring access.
                    // Returning true means STARTED only: QEMU must still inspect
                    // captured PCM and reject underruns or replay.
                    bool started = AudioPlayback.TryStart(output,
                        new MemoryAudioStream(AudioFormat.Stereo16, sampleRate, scaled),
                        null, out AudioPlayback playback);
                    Console.WriteLine(started
                        ? "[AUDIO] ASYNC STARTED (not proof of audible PCM)"
                        : "[AUDIO] ASYNC REFUSED (scheduler unavailable or output busy)");
                    return started;
                }
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
