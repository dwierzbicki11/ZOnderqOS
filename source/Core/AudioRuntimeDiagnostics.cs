#if ZONDERQ_AUDIO_PROBE
using System;
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

        public static int VolumePercent => volumePercent;

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
                byte[] scaled = AudioPcmVolume.ScaleStereo16(pcm, volumePercent);
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
