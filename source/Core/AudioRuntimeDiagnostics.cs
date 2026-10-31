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

            // 250 ms, 440 Hz, PCM16 stereo. Pure generator has host C# tests.
            const int sampleRate = 48000;
            byte[] pcm = AudioTonePcm.GenerateSquareStereo16(sampleRate, 440, 250, 4096);

            try
            {
                bool accepted = AudioManager.Play(new MemoryAudioStream(AudioFormat.Stereo16, sampleRate, pcm));
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
